using ClipFlow.Core;
using ClipFlow.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Windows.Input;

namespace ClipFlow.ViewModels;

public sealed class RecordViewModel : ObservableObject
{
    public ClipboardRecord Record { get; private set; }
    public string Id => Record.Id;
    private readonly Func<string>? _query;
    public string SearchQuery => _query?.Invoke() ?? _owner.Search;
    public string Preview
    {
        get
        {
            if (Record.Text is not { } text) return "查看图片";
            var match = SearchQuery.Length > 0 ? text.IndexOf(SearchQuery, StringComparison.OrdinalIgnoreCase) : -1;
            var start = Math.Max(0, match - 80);
            if (start > 0 && char.IsLowSurrogate(text[start])) start--;
            var length = Math.Min(512, text.Length - start);
            if (length > 0 && char.IsHighSurrogate(text[start + length - 1])) length--;
            return (start > 0 ? "…" : "") + text.Substring(start, length) + (start + length < text.Length ? "…" : "");
        }
    }
    private int _copyVersion;
    public bool IsCopied { get; private set; }
    public string CopyGlyph => IsCopied ? "\uE73E" : "\uE8C8";
    public string CopyLabel => IsCopied ? "已复制" : "复制记录";
    internal async Task MarkCopiedAsync()
    {
        var version = ++_copyVersion;
        IsCopied = true; Raise(nameof(IsCopied)); Raise(nameof(CopyGlyph)); Raise(nameof(CopyLabel));
        await Task.Delay(1200);
        if (version != _copyVersion) return;
        IsCopied = false; Raise(nameof(IsCopied)); Raise(nameof(CopyGlyph)); Raise(nameof(CopyLabel));
    }
    public string DetailHint => Record.Kind == ContentKind.Text ? $"查看全文 · {Record.Text?.Length ?? 0:N0} 字符" : "查看高清原图";
    public string KindLabel => Record.Kind == ContentKind.Text ? "文本" : "图片";
    public string TypeGlyph => Record.Kind == ContentKind.Text ? "\uE8A5" : "\uEB9F";
    public string Timestamp => Record.CopiedAt.ToLocalTime().ToString("MM 月 dd 日 · HH:mm:ss");
    public string Dimensions => $"{Record.Width} × {Record.Height} 像素";
    public string FavoriteGlyph => Record.IsFavorite ? "\uE735" : "\uE734";
    public string FavoriteLabel => Record.IsFavorite ? "取消收藏" : "收藏";
    public Visibility FavoriteVisibility => Record.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    private readonly MainViewModel _owner;
    public Visibility PinActionVisibility => Record.IsFavorite && _owner.IsFavoritesPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PinnedVisibility => Record.IsPinned && _owner.IsFavoritesPage ? Visibility.Visible : Visibility.Collapsed;
    public string PinLabel => Record.IsPinned ? "取消置顶" : "置顶收藏";
    public string PinGlyph => Record.IsPinned ? "\uE77A" : "\uE718";
    public ICommand PinCommand { get; }
    public Visibility TextVisibility => Record.Kind == ContentKind.Text ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImageVisibility => Record.Kind == ContentKind.Image ? Visibility.Visible : Visibility.Collapsed;
    private bool _imageFailed;
    public Visibility ImageFailureVisibility => _imageFailed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImagePreviewVisibility => !_imageFailed ? Visibility.Visible : Visibility.Collapsed;
    private BitmapImage? _thumbnail;
    private readonly HistoryStore _store;
    public BitmapImage? Thumbnail
    {
        get
        {
            if (Record.Kind != ContentKind.Image || _imageFailed || _thumbnail is not null) return _thumbnail;
            try
            {
                var path = _store.ResolveFile(Record.ThumbnailPath ?? Record.ImagePath ?? "");
                if (!File.Exists(path)) { MarkImageFailed(); return null; }
                // Set only the longer axis; fixing both dimensions would distort the source aspect ratio.
                _thumbnail = new BitmapImage();
                if (Record.Width >= Record.Height) _thumbnail.DecodePixelWidth = Math.Clamp(Record.Width, 1, ImageService.ThumbnailMaxEdge);
                else _thumbnail.DecodePixelHeight = Math.Clamp(Record.Height, 1, ImageService.ThumbnailMaxEdge);
                _thumbnail.ImageFailed += (_, _) => MarkImageFailed();
                _thumbnail.UriSource = new Uri(path);
            }
            catch { MarkImageFailed(); }
            return _thumbnail;
        }
    }
    public ICommand CopyCommand { get; }
    public ICommand FavoriteCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DetailCommand { get; }
    public RecordViewModel(ClipboardRecord record, MainViewModel owner, Func<string>? query = null)
    {
        _query = query;
        Record = record;
        _owner = owner;
        _store = owner.Store;
        CopyCommand = new AsyncCommand(() => owner.CopyAsync(this), owner.ReportError);
        FavoriteCommand = new AsyncCommand(() => owner.FavoriteAsync(this), owner.ReportError);
        DeleteCommand = new AsyncCommand(() => owner.DeleteAsync(this), owner.ReportError);
        DetailCommand = new AsyncCommand(() => owner.ShowDetailAsync(this), owner.ReportError);
        PinCommand = new AsyncCommand(() => owner.PinAsync(this), owner.ReportError);
    }
    public void Update(ClipboardRecord record)
    {
        var retryImage = record.Kind == ContentKind.Image && record.CopiedAt != Record.CopiedAt;
        Record = record;
        if (retryImage)
        {
            _imageFailed = false; _thumbnail = null;
            Raise(nameof(Thumbnail)); Raise(nameof(ImageFailureVisibility)); Raise(nameof(ImagePreviewVisibility));
        }
        Raise(nameof(Timestamp)); Raise(nameof(FavoriteGlyph)); Raise(nameof(FavoriteLabel)); Raise(nameof(FavoriteVisibility));
        Raise(nameof(PinLabel)); Raise(nameof(PinGlyph)); RefreshPage();
    }
    public void RefreshPage() { Raise(nameof(PinActionVisibility)); Raise(nameof(PinnedVisibility)); Raise(nameof(Preview)); Raise(nameof(SearchQuery)); }
    public void MarkImageFailed()
    {
        _imageFailed = true; Raise(nameof(ImageFailureVisibility)); Raise(nameof(ImagePreviewVisibility));
    }
}
