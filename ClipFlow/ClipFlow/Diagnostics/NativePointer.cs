using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ClipFlow.Diagnostics;

internal static class NativePointer
{
    internal static async Task HoverAsync(IntPtr window, FrameworkElement element)
    {
        var point = Center(window, element); Move(point.X, point.Y); await Task.Delay(180);
    }
    internal static async Task WheelAsync(IntPtr window, FrameworkElement element, int delta)
    {
        var point = Center(window, element); Move(point.X, point.Y);
        var input = Mouse(0x800); input.Data.Mouse.Data = unchecked((uint)delta);
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new IOException("鼠标滚轮输入失败");
        await Task.Delay(80);
    }
    internal static (int X, int Y) Center(IntPtr window, FrameworkElement element)
    {
        var position = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var scale = element.XamlRoot.RasterizationScale;
        var point = new Point { X = (int)Math.Round(position.X * scale), Y = (int)Math.Round(position.Y * scale) };
        if (!ClientToScreen(window, ref point) || GetAncestor(WindowFromPoint(point), 2) != window)
            throw new InvalidOperationException("测试控件被其他窗口遮挡，停止输入");
        return (point.X, point.Y);
    }
    internal static async Task ClickAsync(IntPtr window, FrameworkElement element)
    {
        var point = Center(window, element);
        Move(point.X, point.Y);
        if (SendInput(2, [Mouse(2), Mouse(4)], Marshal.SizeOf<Input>()) != 2) throw new IOException("鼠标点击失败");
        await Task.Delay(120);
    }
    internal static async Task DragAsync(IntPtr window, FrameworkElement element, int dx, int dy)
    {
        var point = Center(window, element);
        await Task.Run(async () =>
        {
            Move(point.X, point.Y);
            SendInput(1, [Mouse(2)], Marshal.SizeOf<Input>());
            try
            {
                for (var i = 1; i <= 10; i++) { await Task.Delay(20); Move(point.X + dx * i / 10, point.Y + dy * i / 10); }
            }
            finally { SendInput(1, [Mouse(4)], Marshal.SizeOf<Input>()); }
        });
        await Task.Delay(100);
    }
    private static void Move(int x, int y)
    {
        var input = Mouse(0xC001);
        input.Data.Mouse.X = (x - GetSystemMetrics(76)) * 65535 / Math.Max(1, GetSystemMetrics(78) - 1);
        input.Data.Mouse.Y = (y - GetSystemMetrics(77)) * 65535 / Math.Max(1, GetSystemMetrics(79) - 1);
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new IOException("鼠标移动失败");
    }
    private static Input Mouse(uint flags) => new() { Data = new Data { Mouse = new MouseInput { Flags = flags } } };
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Data Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Data { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [DllImport("user32")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32")] private static extern int GetSystemMetrics(int index);
}
