using ClipFlow.Core;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ClipFlow.Services;

// One active read, one coalesced pending event, and a sequence check before persistence.
public sealed class ClipboardService : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _debounce;
    private readonly ImageService _images = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<CapturedContent, Task> _save;
    private bool _enabled, _pending, _suspended, _disposed, _started;
    private int _generation;
    private uint _lastSequence;
    private Task _active = Task.CompletedTask;
    private Task<bool>? _privacyRead;
    private readonly string _session = Guid.NewGuid().ToString("N");
    public event Action<string>? Notice;
    public ClipboardService(DispatcherQueue dispatcher, Func<CapturedContent, Task> save)
    {
        _dispatcher = dispatcher; _save = save;
        _debounce = dispatcher.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(100);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => { if (_active.IsCompleted && _pending && CanRecord) _active = ProcessAsync(); };
    }
    private bool CanRecord => _enabled && !_suspended && !_disposed;
    [System.Diagnostics.Conditional("DEBUG")]
    private static void Trace(string message)
    {
#if DEBUG
        var root = Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT");
        if (root is null) return;
        try { File.AppendAllText(Path.Combine(root, "clipboard-trace.txt"), $"{DateTimeOffset.Now:O} {message}\n"); } catch { }
#endif
    }
    public void Start(bool enabled)
    {
        if (_started) return;
        _enabled = enabled; _lastSequence = ClipboardPrivacy.GetClipboardSequenceNumber();
        Clipboard.ContentChanged += OnChanged; _started = true;
    }
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled; _generation++; _pending = false; _debounce.Stop();
        _lastSequence = ClipboardPrivacy.GetClipboardSequenceNumber();
    }
    private void OnChanged(object? sender, object e) => _dispatcher.TryEnqueue(() =>
    {
        Trace($"event seq={ClipboardPrivacy.GetClipboardSequenceNumber()} enabled={CanRecord} active={_active.IsCompleted}");
        if (!CanRecord) return;
        _pending = true;
        _debounce.Stop(); _debounce.Start();
    });
    public async Task SuspendAndDrainAsync()
    {
        _suspended = true; _generation++; _pending = false; _debounce.Stop();
        await _active;
    }
    public void Resume()
    {
        _lastSequence = ClipboardPrivacy.GetClipboardSequenceNumber();
        _suspended = false;
    }
    private async Task ProcessAsync()
    {
        _pending = false;
        var sequence = ClipboardPrivacy.GetClipboardSequenceNumber();
        var generation = _generation;
        Trace($"read seq={sequence} last={_lastSequence} generation={generation}");
        if (sequence == _lastSequence || !CanRecord) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var content = await RetryPolicy.RunAsync(() => ReadAsync(timeout.Token), IsTransient, timeout.Token);
            Trace($"read-complete kind={content?.Kind} seq={sequence} current={ClipboardPrivacy.GetClipboardSequenceNumber()} generation={generation}/{_generation}");
            if (CanRecord && generation == _generation && sequence == ClipboardPrivacy.GetClipboardSequenceNumber())
            {
                if (content is not null) await _save(content);
                _lastSequence = sequence;
                Trace($"saved seq={sequence}");
            }
        }
        catch (OperationCanceledException) { Trace("read-timeout"); if (!_disposed && CanRecord) Notice?.Invoke("此次剪贴板读取超时，后续复制仍会正常记录。"); }
        catch (Exception ex)
        {
            Trace($"read-error HRESULT={ex.HResult:X8} {ex.GetType().Name}");
            if (!_disposed) Notice?.Invoke(ex is InvalidDataException ? ex.Message : "暂时无法读取剪贴板，已跳过此次内容。后续复制仍会正常记录。");
        }
        finally
        {
            if (_pending && CanRecord) { _debounce.Stop(); _debounce.Start(); }
        }
    }
    private async Task<CapturedContent?> ReadAsync(CancellationToken token)
    {
        // At most one native privacy read, even if a delayed-rendering owner hangs.
        // The timeout releases this pipeline without blocking the UI or spawning unlimited workers.
        if (_privacyRead is not null && !_privacyRead.IsCompleted) await _privacyRead.WaitAsync(token);
        _privacyRead = Task.Run(ClipboardPrivacy.ShouldSkip);
        if (await _privacyRead.WaitAsync(token)) return null;
        var view = Clipboard.GetContent();
        if (view.Properties.TryGetValue("ClipFlow.Session", out var value) && value as string == _session) return null;
        Exception? imageFailure = null;
        if (view.Contains(StandardDataFormats.Bitmap))
        {
            try
            {
                var reference = await view.GetBitmapAsync().AsTask(token);
                using var stream = await reference.OpenReadAsync().AsTask(token);
                return await Task.Run(() => _images.DecodeAsync(stream, token), token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !IsTransient(ex)
                && view.Contains(StandardDataFormats.StorageItems))
            {
                // Some screenshot providers advertise a bitmap whose backing file is unavailable.
                // An independently supplied image file may still be readable.
                imageFailure = ex;
            }
        }
        if (view.Contains(StandardDataFormats.StorageItems))
        {
            var items = await view.GetStorageItemsAsync().AsTask(token);
            // Keep each clipboard read bounded; do not traverse folders or import other file types.
            foreach (var file in items.OfType<StorageFile>().Take(16))
            {
                if (!IsImageFile(file.FileType)) continue;
                try
                {
                    using var stream = await file.OpenReadAsync().AsTask(token);
                    // Decode to owned bytes immediately. HistoryStore writes its own PNG and
                    // thumbnail before committing metadata; the source path is never persisted.
                    return await Task.Run(() => _images.DecodeAsync(stream, token), token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !IsTransient(ex))
                { imageFailure = ex; }
            }
        }
        if (imageFailure is not null) throw imageFailure;
        if (view.Contains(StandardDataFormats.Text))
        {
            var text = await view.GetTextAsync().AsTask(token);
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Length > 2_000_000) throw new InvalidDataException("文本超过 200 万字符，已跳过。");
            var hash = await Task.Run(() => ContentIdentity.TextHash(text), token);
            return new CapturedContent(ContentKind.Text, text, hash);
        }
        return null;
    }
    private static bool IsImageFile(string extension) => extension.ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp" or ".jxr";
    private static bool IsTransient(Exception ex) => ex.HResult is unchecked((int)0x800401D0) or unchecked((int)0x80070005) or unchecked((int)0x8000000A) or unchecked((int)0x80070578) or unchecked((int)0x80070020);
    public async Task CopyAsync(ClipboardRecord record, HistoryStore store)
    {
        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.Properties["ClipFlow.Session"] = _session;
        if (record.Kind == ContentKind.Text) data.SetText(record.Text ?? "");
        else
        {
            var file = await StorageFile.GetFileFromPathAsync(store.ResolveFile(record.ImagePath ?? ""));
            data.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        }
        await RetryPolicy.RunAsync(() =>
        {
            if (!Clipboard.SetContentWithOptions(data, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }))
                throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
            return Task.FromResult(true);
        }, IsTransient, _lifetime.Token);
        _lastSequence = ClipboardPrivacy.GetClipboardSequenceNumber();
        try { Clipboard.Flush(); } catch (COMException) { Notice?.Invoke("已复制；系统暂时无法保存退出后的剪贴板副本。"); }
    }
    public void Dispose()
    {
        _disposed = true; _generation++; _debounce.Stop(); _lifetime.Cancel();
        if (_started) Clipboard.ContentChanged -= OnChanged;
    }
}
