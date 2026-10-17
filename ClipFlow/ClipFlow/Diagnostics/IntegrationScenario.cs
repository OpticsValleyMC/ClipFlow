using ClipFlow.Core;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using ClipFlow.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Storage;

namespace ClipFlow.Diagnostics;

// Opt-in real WinUI/clipboard test. Uses a separately selected data directory and non-sensitive fixtures.
internal static class IntegrationScenario
{
    public static async Task RunAsync(MainViewModel vm, FrameworkElement root, Action<string> navigate, Action<int,int> resize,
        Func<RecordViewModel, ContentDialog> createDetailDialog, Func<Task> checkBackdrop, Action activateWindow)
    {
        var report = new List<string>();
        var rootPath = vm.Store.RootPath;
        async Task CheckAsync(string name, Func<Task> action)
        {
            await action(); report.Add("PASS: " + name);
            await File.WriteAllLinesAsync(Path.Combine(rootPath, "integration-results.txt"), report);
        }
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 50; i++) { if (condition()) return; await Task.Delay(100); }
            throw new TimeoutException("剪贴板监听未在 5 秒内完成。");
        }
        async Task SetDataAsync(DataPackage data, bool allowed = true)
        {
            activateWindow();
            await Task.Delay(100);
            await RetryPolicy.RunAsync(() =>
            {
                if (!Clipboard.SetContentWithOptions(data, new ClipboardContentOptions { IsAllowedInHistory = allowed, IsRoamable = false }))
                    throw new System.Runtime.InteropServices.COMException("剪贴板被占用。", unchecked((int)0x800401D0));
                Clipboard.Flush();
                return Task.FromResult(true);
            }, ex => ex.HResult is unchecked((int)0x800401D0) or unchecked((int)0x80070005));
        }
        Task SetTextAsync(string text)
        {
            var data = new DataPackage(); data.SetText(text);
            return SetDataAsync(data);
        }
        try
        {
            Assert(vm.IsReady, "数据库初始化失败。");
            await CheckAsync("native Mica, themes, transparent layering, fallback and background restore", checkBackdrop);
            var history = (FrameworkElement)root.FindName("HistoryContent");
            var status = (InfoBar)history.FindName("RecordingStatus");
            var clearButton = (Button)history.FindName("ClearHistoryButton");
            Assert(!vm.CanClearHistory && !clearButton.IsEnabled, "空历史仍可清空。");
            Assert(status.Severity == InfoBarSeverity.Success && status.Message == "ClipFlow 正在记录", "记录状态提示错误。");
            Assert(((NavigationView)root.FindName("Navigation")).MenuItems.Count == 2, "独立图片页面未移除。");
            await CheckAsync("real text capture", async () =>
            {
                await SetTextAsync("ClipFlow 集成验收\n这是通过真实 Windows 剪贴板写入的文本。\n搜索、收藏与复制均可使用。");
                await WaitAsync(() => vm.Records.Count == 1);
            });
            var first = vm.Records[0];
            var originalTime = first.Record.CopiedAt;
            await CheckAsync("repeat copy updates time without inserting", async () =>
            {
                await SetTextAsync(first.Record.Text!);
                await WaitAsync(() => vm.Records[0].Record.CopiedAt > originalTime);
                Assert(vm.Records.Count == 1, "重复内容生成了新记录。");
            });
            await CheckAsync("long text capture, scroll, paging, full copy and restart", async () =>
            {
                var line = "长文本内容：ClipFlow 保留原文与换行。😀\r\n";
                var longText = "长文开头\r\n" + string.Concat(Enumerable.Repeat(line, 12_000)) + "长文末尾🚀";
                await SetTextAsync(longText);
                await WaitAsync(() => vm.Records.Count == 2);
                var item = vm.Records.Single(r => r.Record.Text == longText);
                Assert(item.Preview.Length <= 513, "列表仍在布局完整长文。");
                var dialog = createDetailDialog(item);
                var operation = dialog.ShowAsync();
                try
                {
                    var detail = (TextDetailView)dialog.Content;
                    await WaitAsync(() => detail.IsLoaded && detail.ActualHeight > 0);
                    var textBox = (TextBox)detail.FindName("FullText");
                    Assert(detail.PageCount > 1 && textBox.Text.StartsWith("长文开头"), "长文首部或分页丢失。");
                    var scroller = FindDescendant<ScrollViewer>(textBox);
                    await WaitAsync(() => scroller.ScrollableHeight > 0);
                    scroller.ChangeView(null, scroller.ScrollableHeight, null, true);
                    await WaitAsync(() => scroller.VerticalOffset > 0);
                    await RenderAsync(dialog, Path.Combine(rootPath, "long-text-detail.png"));
                    detail.GoToPage(detail.PageCount - 1);
                    Assert(textBox.Text.EndsWith("长文末尾🚀"), "长文尾部丢失。");
                    await vm.CopyAsync(item);
                    Assert(await Clipboard.GetContent().GetTextAsync() == longText, "复制的长文被截断。");
                }
                finally { dialog.Hide(); await operation; }
                resize(760, 560); await WaitAsync(() => root.ActualWidth < 800);
                var narrowDialog = createDetailDialog(item);
                var narrowOperation = narrowDialog.ShowAsync();
                try
                {
                    await WaitAsync(() => ((FrameworkElement)narrowDialog.Content).IsLoaded);
                    Assert(narrowDialog.ActualWidth <= root.ActualWidth && narrowDialog.ActualHeight <= root.ActualHeight, "窄窗口长文对话框超出窗口。");
                    await RenderAsync(narrowDialog, Path.Combine(rootPath, "long-text-narrow.png"));
                }
                finally { narrowDialog.Hide(); await narrowOperation; resize(1100, 760); }
                await WaitAsync(() => root.ActualWidth > 1000);
                var second = new HistoryStore(rootPath); await second.InitializeAsync();
                Assert((await second.LoadAsync()).Single(r => r.Id == item.Id).Text == longText, "重启后的长文被截断。");
                await vm.DeleteAsync(item);
                // Bounded oversized input must not compromise subsequent events.
                await SetTextAsync(new string('x', 2_000_001)); await Task.Delay(500);
                Assert(vm.Records.Count == 1, "超出上限的文本未正确跳过。");
            });
            await CheckAsync("favorite, search and own copy", async () =>
            {
                await vm.FavoriteAsync(first);
                navigate("favorites"); Assert(vm.Records.Count == 1 && vm.Records[0].Record.IsFavorite, "收藏筛选失败。");
                Assert(clearButton.Visibility == Visibility.Collapsed, "收藏夹仍显示清空历史。");
                vm.Search = "不会匹配的关键词"; Assert(vm.Records.Count == 0, "搜索空状态失败。");
                vm.Search = "集成验收"; Assert(vm.Records.Count == 1, "搜索失败。");
                await vm.CopyAsync(first); await Task.Delay(300);
                Assert((await vm.Store.LoadAsync()).Count == 1, "内部复制产生重复记录。");
                navigate("recent");
            });
            await CheckAsync("pause and privacy flags", async () =>
            {
                await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = false });
                Assert(status.Severity == InfoBarSeverity.Warning && ((FontIconSource)status.IconSource).Glyph == "\uE769", "暂停状态未切换为警告。");
                ((InfoBar)root.FindName("Feedback")).IsOpen = false;
                await Task.Delay(100);
                await RenderAsync(root, Path.Combine(rootPath, "recent-paused.png"));
                await SetTextAsync("此内容在暂停期间复制，不能保存。"); await Task.Delay(300);
                Assert((await vm.Store.LoadAsync()).Count == 1, "暂停未生效。");
                await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = true });
                Assert(status.Severity == InfoBarSeverity.Success && ((FontIconSource)status.IconSource).Glyph == "\uE768", "继续记录状态未恢复。");
                var excluded = new DataPackage(); excluded.SetText("明确禁止历史保存的测试内容");
                await SetDataAsync(excluded, false); await Task.Delay(400);
                Assert((await vm.Store.LoadAsync()).Count == 1, "隐私标记未生效。");
            });
            await CheckAsync("real image capture and duplicate pixels", async () =>
            {
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                var pixels = new byte[320 * 180 * 4];
                for (var y = 0; y < 180; y++) for (var x = 0; x < 320; x++)
                {
                    var offset = (y * 320 + x) * 4;
                    pixels[offset] = (byte)(160 + x / 4); pixels[offset + 1] = (byte)(90 + y / 2);
                    pixels[offset + 2] = (byte)(70 + x / 3); pixels[offset + 3] = 255;
                }
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 320, 180, 96, 96, pixels);
                await encoder.FlushAsync(); stream.Seek(0);
                var data = new DataPackage(); data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                await SetDataAsync(data); await WaitAsync(() => vm.Records.Count == 2);
                var image = vm.Records.Single(r => r.Record.Kind == ContentKind.Image);
                Assert(image.Record.Width == 320 && image.Record.Height == 180, "图片尺寸失败。");
                Assert(File.Exists(vm.Store.ResolveFile(image.Record.ThumbnailPath!)), "缩略图未保存。");
                var preview = image.Thumbnail!;
                await WaitAsync(() => preview.PixelWidth > 0);
                Assert(Math.Abs((double)preview.PixelWidth / preview.PixelHeight - 320d / 180) < .01, "图片预览宽高比不正确。");
                var before = image.Record.CopiedAt;
                await SetDataAsync(data);
                await WaitAsync(() => image.Record.CopiedAt > before);
                Assert(vm.Records.Count == 2, "图片重复保存。");
                await vm.CopyAsync(image);
                vm.TypeFilter = 2;
                Assert(vm.Records.Count == 1 && vm.Records[0].Record.Kind == ContentKind.Image, "最近记录的图片筛选失败。");
                vm.TypeFilter = 0;
                Assert(vm.Records.Count == 2, "最近记录未同时包含文本和图片。");
            });
            await CheckAsync("temporary image files become independent managed copies", async () =>
            {
                var sourceDirectory = Path.Combine(Path.GetTempPath(), "ClipFlow-fixture-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(sourceDirectory);
                var sourcePath = Path.Combine(sourceDirectory, "screenshot.png");
                var brokenPath = Path.Combine(sourceDirectory, "broken.png");
                var otherPath = Path.Combine(sourceDirectory, "note.txt");
                try
                {
                    using var stream = new InMemoryRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    var pixels = new byte[123 * 79 * 4];
                    for (var i = 0; i < pixels.Length; i += 4)
                    { pixels[i] = 37; pixels[i + 1] = (byte)(i % 251); pixels[i + 2] = 149; pixels[i + 3] = 255; }
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 123, 79, 96, 96, pixels);
                    await encoder.FlushAsync(); stream.Seek(0);
                    using var reader = new DataReader(stream.GetInputStreamAt(0));
                    await reader.LoadAsync((uint)stream.Size);
                    var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
                    await File.WriteAllBytesAsync(sourcePath, bytes);
                    await File.WriteAllBytesAsync(brokenPath, [1, 2, 3]);
                    await File.WriteAllTextAsync(otherPath, "non-image fixture");
                    var source = await StorageFile.GetFileFromPathAsync(sourcePath);
                    var broken = await StorageFile.GetFileFromPathAsync(brokenPath);
                    var other = await StorageFile.GetFileFromPathAsync(otherPath);
                    var data = new DataPackage(); data.SetStorageItems(new IStorageItem[] { other, broken, source });
                    await SetDataAsync(data);
                    await WaitAsync(() => vm.Records.Any(r => r.Record.Width == 123 && r.Record.Height == 79));
                    var image = vm.Records.Single(r => r.Record.Width == 123);
                    var before = image.Record.CopiedAt;
                    var count = vm.Records.Count;
                    await SetDataAsync(data); await WaitAsync(() => image.Record.CopiedAt > before);
                    Assert(vm.Records.Count == count, "临时图片文件重复保存。");
                    Assert(image.Record.ImagePath!.StartsWith("images") && !Path.IsPathRooted(image.Record.ImagePath), "保存了来源文件路径。");
                    var original = vm.Store.ResolveFile(image.Record.ImagePath);
                    var thumbnail = vm.Store.ResolveFile(image.Record.ThumbnailPath!);
                    File.Delete(sourcePath); File.Delete(brokenPath); File.Delete(otherPath);
                    var restarted = new HistoryStore(rootPath); await restarted.InitializeAsync();
                    var restored = (await restarted.LoadAsync()).Single(r => r.Id == image.Id);
                    Assert(File.Exists(original) && File.Exists(thumbnail) && restored.Hash == image.Record.Hash, "来源删除后本地图片丢失。");
                    var dialog = createDetailDialog(image); var operation = dialog.ShowAsync();
                    try
                    {
                        var detail = (ImageDetailView)dialog.Content;
                        await WaitAsync(() => detail.IsLoaded); await detail.LoadTask;
                        var bitmap = ((Microsoft.UI.Xaml.Controls.Image)detail.FindName("OriginalImage")).Source as BitmapImage;
                        Assert(bitmap?.PixelWidth == 123 && bitmap.PixelHeight == 79, "来源删除后原图无法预览。");
                    }
                    finally { dialog.Hide(); await operation; }
                    // A hidden test launch/modal teardown may leave no foreground owner.
                    // Exercise the copy API with the same active-window context as a user click.
                    activateWindow(); await Task.Delay(100);
                    await vm.CopyAsync(image);
                    var copyView = Clipboard.GetContent();
                    Assert(copyView.Contains(StandardDataFormats.Bitmap), "图片复制未提供 Bitmap 格式：" + string.Join(", ", copyView.AvailableFormats));
                    using var copied = await (await copyView.GetBitmapAsync()).OpenReadAsync();
                    var decoded = await BitmapDecoder.CreateAsync(copied);
                    Assert(decoded.PixelWidth == 123 && decoded.PixelHeight == 79, "来源删除后不能再次复制图片。");
                    await vm.DeleteAsync(image);
                    Assert(!File.Exists(original) && !File.Exists(thumbnail), "删除记录后图片副本未清理。");
                }
                finally
                {
                    foreach (var fixturePath in new[] { sourcePath, brokenPath, otherPath })
                        if (File.Exists(fixturePath)) File.Delete(fixturePath);
                    Directory.Delete(sourceDirectory);
                }
            });
            await CheckAsync("missing and corrupt thumbnail placeholders", async () =>
            {
                var record = vm.Records.Single(r => r.Record.Kind == ContentKind.Image).Record;
                var missing = new RecordViewModel(record with { ThumbnailPath = "images/missing-fixture.png" }, vm);
                _ = missing.Thumbnail;
                Assert(missing.ImageFailureVisibility == Visibility.Visible, "缺失文件未显示占位。");
                var corruptPath = vm.Store.ResolveFile("images/corrupt-fixture.png");
                await File.WriteAllBytesAsync(corruptPath, new byte[] { 1, 2, 3 });
                var corrupt = new RecordViewModel(record with { ThumbnailPath = "images/corrupt-fixture.png" }, vm);
                // WinUI defers decoding until an Image uses the BitmapImage in the visual tree.
                var fixture = new Microsoft.UI.Xaml.Controls.Image { Source = corrupt.Thumbnail, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                var panel = (Microsoft.UI.Xaml.Controls.Panel)root;
                panel.Children.Add(fixture);
                try { await WaitAsync(() => corrupt.ImageFailureVisibility == Visibility.Visible); }
                finally { panel.Children.Remove(fixture); File.Delete(corruptPath); }
            });
            await CheckAsync("HD thumbnail, original image zoom and corrupt original recovery", async () =>
            {
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                var pixels = new byte[1920 * 1080 * 4];
                for (var y = 0; y < 1080; y++) for (var x = 0; x < 1920; x++)
                {
                    var offset = (y * 1920 + x) * 4;
                    var stripe = (byte)((x / 2 + y / 2) % 2 == 0 ? 245 : 30);
                    pixels[offset] = stripe; pixels[offset + 1] = (byte)(y % 255); pixels[offset + 2] = (byte)(x % 255); pixels[offset + 3] = 255;
                }
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 1920, 1080, 96, 96, pixels);
                await encoder.FlushAsync(); stream.Seek(0);
                var data = new DataPackage(); data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                await SetDataAsync(data); await WaitAsync(() => vm.Records.Count == 3);
                var image = vm.Records.Single(r => r.Record.Width == 1920);
                var thumbnail = image.Thumbnail!;
                await WaitAsync(() => thumbnail.PixelWidth > 0);
                Assert(thumbnail.PixelWidth == ImageService.ThumbnailMaxEdge && thumbnail.PixelHeight == 576, "高清缩略图分辨率或比例错误。");
                var dialog = createDetailDialog(image);
                var operation = dialog.ShowAsync();
                try
                {
                    var detail = (ImageDetailView)dialog.Content;
                    await WaitAsync(() => detail.IsLoaded);
                    await detail.LoadTask;
                    var original = (Microsoft.UI.Xaml.Controls.Image)detail.FindName("OriginalImage");
                    var bitmap = original.Source as BitmapImage;
                    Assert(bitmap?.PixelWidth == 1920 && bitmap.PixelHeight == 1080, "详情未加载原始像素。");
                    var scroll = (ScrollViewer)detail.FindName("PreviewScroll");
                    await RenderAsync(dialog, Path.Combine(rootPath, "image-detail-fit.png"));
                    scroll.ChangeView(0, 0, 1, true);
                    await WaitAsync(() => Math.Abs(scroll.ZoomFactor - 1) < .01);
                    Assert(scroll.ScrollableWidth > 0 && scroll.ScrollableHeight > 0, "原始像素预览不能滚动。");
                    await RenderAsync(dialog, Path.Combine(rootPath, "image-detail-100.png"));
                    activateWindow(); await Task.Delay(100);
                    await ImageInputScenario.RunAsync(detail, root);
                    report.Add("PASS: native mouse wheel zoom in/out, pointer anchor, min/max bounds, empty margins and drag after zoom");
                }
                finally { dialog.Hide(); await operation; }
                var corruptPath = vm.Store.ResolveFile("images/corrupt-original.png");
                await File.WriteAllBytesAsync(corruptPath, [1, 2, 3]);
                var corrupt = new RecordViewModel(image.Record with { ImagePath = "images/corrupt-original.png" }, vm);
                var brokenDialog = createDetailDialog(corrupt);
                var brokenOperation = brokenDialog.ShowAsync();
                try
                {
                    var detail = (ImageDetailView)brokenDialog.Content;
                    await WaitAsync(() => detail.IsLoaded); await detail.LoadTask;
                    Assert(((FrameworkElement)detail.FindName("Failure")).Visibility == Visibility.Visible, "损坏原图未显示占位。");
                }
                finally { brokenDialog.Hide(); await brokenOperation; File.Delete(corruptPath); }
                await vm.DeleteAsync(image);
            });
            await CheckAsync("image aspect-fit, centered portrait, zoom buttons and narrow preview", async () =>
            {
                foreach (var (width, height) in new[] { (1632, 1131), (240, 1280), (1280, 240), (20, 4000) })
                {
                    using var stream = new InMemoryRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    var pixels = new byte[width * height * 4];
                    for (var i = 0; i < pixels.Length; i += 4)
                    {
                        pixels[i] = 100; pixels[i + 1] = (byte)((i / 4 / width) % 255);
                        pixels[i + 2] = (byte)((i / 4 % width) % 255); pixels[i + 3] = 255;
                    }
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, pixels);
                    await encoder.FlushAsync(); stream.Seek(0);
                    var count = vm.Records.Count;
                    var data = new DataPackage(); data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                    await SetDataAsync(data); await WaitAsync(() => vm.Records.Count == count + 1);
                    var image = vm.Records.Single(record => record.Record.Width == width && record.Record.Height == height);
                    foreach (var narrow in new[] { false, true })
                    {
                        resize(narrow ? 760 : 1100, narrow ? 560 : 760); await Task.Delay(200);
                        var dialog = createDetailDialog(image); var operation = dialog.ShowAsync();
                        try
                        {
                            var detail = (ImageDetailView)dialog.Content;
                            await WaitAsync(() => detail.IsLoaded); await detail.LoadTask; await Task.Delay(100);
                            var scroll = (ScrollViewer)detail.FindName("PreviewScroll");
                            var original = (Microsoft.UI.Xaml.Controls.Image)detail.FindName("OriginalImage");
                            var shownWidth = original.ActualWidth * scroll.ZoomFactor;
                            var shownHeight = original.ActualHeight * scroll.ZoomFactor;
                            Assert(shownWidth <= scroll.ViewportWidth + 3 && shownHeight <= scroll.ViewportHeight + 3, "适应窗口裁切了图片。");
                            Assert(scroll.HorizontalContentAlignment == HorizontalAlignment.Center && scroll.VerticalContentAlignment == VerticalAlignment.Center, "原图未居中。");
                            if (width == 1632) Assert(scroll.ViewportWidth - shownWidth < 12, "预览仍有大块横向空白。");
                            var minus = (Button)detail.FindName("ZoomOutButton"); var plus = (Button)detail.FindName("ZoomInButton");
                            Assert((string)minus.Content == "−" && (string)plus.Content == "+" && minus.Width == plus.Width, "缩放符号或尺寸不统一。");
                            var before = scroll.ZoomFactor;
                            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(plus);
                            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                            await WaitAsync(() => scroll.ZoomFactor > before);
                            peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(minus);
                            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                            await WaitAsync(() => Math.Abs(scroll.ZoomFactor - before) < .01);
                            if (width == 1632 || width == 240)
                                await RenderAsync(dialog, Path.Combine(rootPath, $"image-aspect-{width}{(narrow ? "-narrow" : "")}.png"));
                        }
                        finally { dialog.Hide(); await operation; }
                    }
                    await vm.DeleteAsync(image);
                }
                resize(1100, 760); await Task.Delay(200);
            });
            await CheckAsync("occupied clipboard retries and subsequent events recover", async () =>
            {
                await LockClipboardAsync("temporary clipboard lock fixture", 270);
                await WaitAsync(() => vm.Records.Count == 3);
                await vm.DeleteAsync(vm.Records.Single(r => r.Record.Text == "temporary clipboard lock fixture"));
                await LockClipboardAsync("persistent clipboard lock fixture", 1000);
                await Task.Delay(100);
                Assert(vm.Records.Count == 2, "持续占用应在有限重试后跳过。");
                await SetTextAsync("after clipboard lock fixture");
                await WaitAsync(() => vm.Records.Count == 3);
                await vm.DeleteAsync(vm.Records.Single(r => r.Record.Text == "after clipboard lock fixture"));
            });
            await SetTextAsync("一个更轻松的复制空间。\nClipFlow 使用 WinUI 3 原生控件，将文字与图片保存在本机。");
            await WaitAsync(() => vm.Records.Count == 3);
            await CheckAsync("light, dark, system themes and layout renders", async () =>
            {
                ((InfoBar)root.FindName("Feedback")).IsOpen = false;
                foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System })
                {
                    await vm.UpdateSettingsAsync(vm.Settings with { Theme = mode });
                    await Task.Delay(250);
                    await RenderAsync(root, Path.Combine(rootPath, $"recent-{mode}.png"));
                }
                navigate("settings"); await Task.Delay(250);
                await RenderAsync(root, Path.Combine(rootPath, "settings.png"));
                resize(760,560); await Task.Delay(300);
                await RenderAsync(root, Path.Combine(rootPath, "settings-narrow.png"));
                navigate("recent"); await Task.Delay(250);
                await RenderAsync(root, Path.Combine(rootPath, "recent-narrow.png"));
                await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = false });
                await Task.Delay(250);
                await RenderAsync(root, Path.Combine(rootPath, "recent-paused-narrow.png"));
                await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = true });
                resize(1100,760);
                await WaitAsync(() => ((NavigationView)root.FindName("Navigation")).IsPaneOpen
                    && ((FrameworkElement)root.FindName("PaneFooterText")).Visibility == Visibility.Visible);
            });
            await CheckAsync("image deletion removes both files", async () =>
            {
                var image = vm.Records.Single(r => r.Record.Kind == ContentKind.Image);
                var original = vm.Store.ResolveFile(image.Record.ImagePath!); var thumbnail = vm.Store.ResolveFile(image.Record.ThumbnailPath!);
                await vm.DeleteAsync(image);
                Assert(!File.Exists(original) && !File.Exists(thumbnail), "删除图片未清理文件。");
            });
            await CheckAsync("restart storage", async () =>
            {
                var second = new HistoryStore(rootPath); await second.InitializeAsync();
                var items = await second.LoadAsync();
                Assert(items.Count == 2 && items.Any(i => i.IsFavorite), "重启后记录或收藏丢失。");
            });
            await CheckAsync("clear cancel and confirm", async () =>
            {
                vm.ConfirmClear = _ => Task.FromResult(false); vm.ClearHistoryCommand.Execute(null); await Task.Delay(150);
                Assert((await vm.Store.LoadAsync()).Count == 2, "取消清空仍删除数据。");
                vm.ConfirmClear = _ => Task.FromResult(true); vm.ClearDataCommand.Execute(null);
                await WaitAsync(() => vm.Records.Count == 0);
                Assert(await vm.Store.LoadSettingsAsync() == new AppSettings(), "清空全部未重置设置。");
                Assert(!clearButton.IsEnabled, "清空后按钮仍然可用。");
                ((InfoBar)root.FindName("Feedback")).IsOpen = false;
                await Task.Delay(100);
                await RenderAsync(root, Path.Combine(rootPath, "recent-empty.png"));
                var emptyText = (TextBlock)history.FindName("EmptyDescriptionText");
                Assert(emptyText.ActualHeight <= 30 && emptyText.ActualWidth > 0 && !emptyText.IsTextTrimmed,
                    "正常窗口下空状态说明未完整显示在单行。");
            });
            await SetTextAsync("重启后保留的收藏记录");
            await WaitAsync(() => vm.Records.Count == 1);
            await vm.FavoriteAsync(vm.Records[0]);
            await vm.UpdateSettingsAsync(new AppSettings(ThemeMode.Dark, false, 50));
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); }
        finally { await File.WriteAllLinesAsync(Path.Combine(rootPath, "integration-results.txt"), report); }
    }
    public static async Task CheckRestartAsync(MainViewModel vm, FrameworkElement root, Action<string> navigate)
    {
        var report = new List<string>();
        try
        {
            if (!vm.IsReady || vm.Records.Count != 1 || vm.Records[0].Record.Text != "重启后保留的收藏记录" || !vm.Records[0].Record.IsFavorite)
                throw new InvalidOperationException("应用重启后记录或收藏未恢复。");
            if (vm.Settings != new AppSettings(ThemeMode.Dark, false, 50)) throw new InvalidOperationException("应用重启后设置未恢复。");
            navigate("favorites");
            if (vm.Records.Count != 1 || root.ActualTheme != ElementTheme.Dark) throw new InvalidOperationException("应用重启后收藏页或主题错误。");
            await Task.Delay(250);
            await RenderAsync(root, Path.Combine(vm.Store.RootPath, "restart.png"));
            report.Add("PASS: application process restart restores history, favorites, theme, pause and retention");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); }
        await File.WriteAllLinesAsync(Path.Combine(vm.Store.RootPath, "restart-results.txt"), report);
    }
    private static async Task RenderAsync(FrameworkElement root, string path)
    {
        // RenderTargetBitmap omits the compositor's Mica surface. Render the real theme fallback behind XAML.
        var fallback = root.FindName("RenderBackdrop") as FrameworkElement;
        if (fallback is not null) fallback.Visibility = Visibility.Visible;
        var target = new RenderTargetBitmap();
        try { await target.RenderAsync(root); }
        finally { if (fallback is not null) fallback.Visibility = Visibility.Collapsed; }
        var buffer = await target.GetPixelsAsync();
        var pixels = new byte[(int)buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
        using var file = File.Open(path, FileMode.Create, FileAccess.ReadWrite);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T found) return found;
            try { return FindDescendant<T>(child); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"找不到 {typeof(T).Name}。");
    }
    private static async Task LockClipboardAsync(string text, int milliseconds)
    {
        var helper = Environment.GetEnvironmentVariable("CLIPFLOW_LOCK_HELPER") ?? throw new InvalidOperationException("请通过 scripts/Test.ps1 -Integration 设置独立剪贴板占用测试程序。");
        var info = new System.Diagnostics.ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        info.ArgumentList.Add("--hold-clipboard"); info.ArgumentList.Add(text); info.ArgumentList.Add(milliseconds.ToString());
        using var process = System.Diagnostics.Process.Start(info) ?? throw new IOException("无法启动剪贴板测试程序。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new IOException("独立剪贴板占用测试程序失败：" + await process.StandardError.ReadToEndAsync());
    }
}
