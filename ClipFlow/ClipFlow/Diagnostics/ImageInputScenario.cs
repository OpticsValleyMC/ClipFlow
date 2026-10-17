using System.Runtime.InteropServices;
using ClipFlow.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ClipFlow.Diagnostics;

// Debug-only native input directed at the isolated app's verified viewport.
internal static class ImageInputScenario
{
    internal static async Task RunAsync(ImageDetailView detail, FrameworkElement root)
    {
        var scroll = (ScrollViewer)detail.FindName("PreviewScroll");
        var image = (Image)detail.FindName("OriginalImage");
        var window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out var process);
        if (process != (uint)Environment.ProcessId) throw new InvalidOperationException("图片输入测试窗口不在前台。");
        var location = scroll.TransformToVisual(root).TransformPoint(new Point(0, 0));
        var anchor = new Point(scroll.ViewportWidth * .37, scroll.ViewportHeight * .43);
        var point = new NativePoint
        {
            X = (int)Math.Round((location.X + anchor.X) * root.XamlRoot.RasterizationScale),
            Y = (int)Math.Round((location.Y + anchor.Y) * root.XamlRoot.RasterizationScale)
        };
        if (!ClientToScreen(window, ref point) || GetAncestor(WindowFromPoint(point), 2) != window)
            throw new InvalidOperationException("图片输入测试区域被其他窗口遮挡。");
        GetCursorPos(out var previous);
        async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 40; i++) { if (condition()) return; await Task.Delay(25); }
            throw new TimeoutException("图片滚轮未响应。");
        }
        void Input(params MouseInput[] values)
        {
            if (GetForegroundWindow() != window || GetAncestor(WindowFromPoint(point), 2) != window)
                throw new InvalidOperationException("图片输入测试失去焦点，停止输入。");
            var inputs = values.Select(v => new NativeInput { Data = new InputData { Mouse = v } }).ToArray();
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>()) != inputs.Length)
                throw new IOException("图片原生鼠标输入失败。");
        }
        void Wheel(int delta) => Input(new MouseInput { Data = unchecked((uint)delta), Flags = 0x800 });
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try
        {
            SetCursorPos(point.X, point.Y); await Task.Delay(100);
            var before = scroll.ZoomFactor;
            var pixelX = (scroll.HorizontalOffset + anchor.X) / before;
            var pixelY = (scroll.VerticalOffset + anchor.Y) / before;
            Wheel(120); await WaitAsync(() => scroll.ZoomFactor > before + .01);
            await Task.Delay(100);
            Assert(Math.Abs(scroll.ZoomFactor - before * 1.25) < .01, "同一次滚轮事件被重复处理。");
            Assert(Math.Abs((scroll.HorizontalOffset + anchor.X) / scroll.ZoomFactor - pixelX) < 3
                && Math.Abs((scroll.VerticalOffset + anchor.Y) / scroll.ZoomFactor - pixelY) < 3,
                "滚轮缩放未保留鼠标锚点。");
            Wheel(-120); await WaitAsync(() => Math.Abs(scroll.ZoomFactor - before) < .01);
            for (var i = 0; i < 4; i++) { Wheel(960); await Task.Delay(80); }
            Assert(Math.Abs(scroll.ZoomFactor - 8) < .01, "滚轮缩放超过上限或未达到上限。");
            for (var i = 0; i < 7; i++) { Wheel(-960); await Task.Delay(80); }
            Assert(Math.Abs(scroll.ZoomFactor - .1) < .01, "滚轮缩放超过下限或未达到下限。");
            Wheel(-120); await Task.Delay(100);
            Assert(Math.Abs(scroll.ZoomFactor - .1) < .01, "到达下限后仍响应为越界缩放。");
            // The image is now smaller than its viewport: wheel must also work on the blank margin.
            Wheel(120); await WaitAsync(() => scroll.ZoomFactor > .11);
            scroll.ChangeView(150, 150, 2, true); await Task.Delay(150);
            var horizontal = scroll.HorizontalOffset; var vertical = scroll.VerticalOffset;
            Input(new MouseInput { Flags = 2 }); await Task.Delay(80);
            Assert(detail.IsDragging, $"左键未开始拖动；viewport={scroll.ViewportWidth}/{scroll.ViewportHeight}; scrollable={scroll.ScrollableWidth}/{scroll.ScrollableHeight}; offsets={horizontal}/{vertical}");
            Input(new MouseInput { X = -45, Y = -35, Flags = 1 }); await Task.Delay(100);
            Input(new MouseInput { Flags = 4 }); await Task.Delay(100);
            Assert(scroll.HorizontalOffset > horizontal + 10 && scroll.VerticalOffset > vertical + 10
                && !detail.IsDragging && detail.CompletedDrags.Count > 0,
                $"滚轮缩放后左键拖动或释放失败：{horizontal}/{vertical} -> {scroll.HorizontalOffset}/{scroll.VerticalOffset}; dragging={detail.IsDragging}; completed={detail.CompletedDrags.Count}。");
            Assert(image.Source is not null, "缩放后原图丢失。");
        }
        finally
        {
            if (detail.IsDragging) SendInput(1, [new NativeInput { Data = new InputData { Mouse = new MouseInput { Flags = 4 } } }], Marshal.SizeOf<NativeInput>());
            SetCursorPos(previous.X, previous.Y);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32")] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32")] private static extern bool SetCursorPos(int x, int y);
}
