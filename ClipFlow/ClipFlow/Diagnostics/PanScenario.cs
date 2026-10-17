using ClipFlow.Core;
using ClipFlow.ViewModels;
using ClipFlow.Views;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipFlow.Diagnostics;

internal static class PanScenario
{
    internal static async Task RunAsync(MainViewModel vm, Func<RecordViewModel, ContentDialog> createDialog)
    {
        var path = Path.Combine(vm.Store.RootPath, "mouse-pan-results.txt");
        var report = new List<string>();
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            const int width = 1920, height = 1440;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)((x / 120 + y / 120) % 2 == 0 ? 200 : 70);
                pixels[offset + 1] = (byte)(y * 255 / height); pixels[offset + 2] = (byte)(x * 255 / width); pixels[offset + 3] = 255;
            }
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
            await encoder.FlushAsync(); stream.Seek(0);
            var captured = await new Services.ImageService().DecodeAsync(stream, CancellationToken.None);
            await vm.Store.SaveAsync(captured, 500);
            var record = (await vm.Store.LoadAsync()).First(r => r.Kind == ContentKind.Image);
            var dialog = createDialog(new RecordViewModel(record, vm));
            var operation = dialog.ShowAsync();
            for (var i = 0; i < 100 && !((ImageDetailView)dialog.Content).IsLoaded; i++) await Task.Delay(50);
            var detail = (ImageDetailView)dialog.Content; await detail.LoadTask;
            var scroll = (ScrollViewer)detail.FindName("PreviewScroll");
            scroll.ChangeView(100, 100, 2, true);
            await Task.Delay(150);
            report.Add("READY: mouse pan fixture at 200%, initial offsets 100/100");
            await File.WriteAllLinesAsync(path, report);
            var changed = 0;
            for (var i = 0; i < 3000 && detail.IsLoaded && detail.CompletedDrags.Count < 4; i++)
            {
                await Task.Delay(100);
                if (changed != detail.CompletedDrags.Count)
                {
                    changed = detail.CompletedDrags.Count;
                    var last = detail.CompletedDrags[^1];
                    report.Add($"DRAG {changed}: offsets {last.X:F1}/{last.Y:F1}; released={!detail.IsDragging}");
                    await File.WriteAllLinesAsync(path, report);
                }
            }
            if (detail.IsLoaded)
            {
                dialog.Hide();
                if (detail.CompletedDrags.Count < 4) throw new TimeoutException("鼠标验收超时。");
            }
            await operation;
            if (detail.CompletedDrags.Count < 3 || detail.IsDragging || !detail.CompletedDrags.Any(p => p.X > 130 && p.Y > 130))
                throw new InvalidOperationException("实际拖动未改变双轴偏移或未释放指针。");
            report.Add("PASS: real mouse input pans both axes, repeat drags and pointer release");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); }
        await File.WriteAllLinesAsync(path, report);
    }
}
