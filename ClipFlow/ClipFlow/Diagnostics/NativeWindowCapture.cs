using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;

namespace ClipFlow.Diagnostics;

// Debug-only capture of this fixture's foreground window including the DWM backdrop.
// It does not change wallpaper, transparency, power or accessibility settings.
internal static class NativeWindowCapture
{
    internal static async Task SaveAsync(IntPtr window, string path)
    {
        if (GetForegroundWindow() != window)
        {
            // Only this isolated fixture is raised; click its verified title area to
            // activate through normal input rather than overriding Mica activation.
            if (!GetWindowRect(window, out var bounds)) throw new IOException("无法读取测试窗口位置。");
            GetCursorPos(out var cursor);
            try
            {
                SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                await Task.Delay(150);
                var point = new Point { X = bounds.Left + 100, Y = bounds.Top + 30 };
                if (GetAncestor(WindowFromPoint(point), 2) != window)
                    throw new InvalidOperationException($"测试窗口标题栏被遮挡，停止输入。Window={window}; Bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; Target={GetAncestor(WindowFromPoint(point), 2)}; Foreground={GetForegroundWindow()}");
                SetCursorPos(point.X, point.Y);
                var inputs = new[]
                {
                    new Input { Data = new InputData { Mouse = new MouseInput { Flags = 2 } } },
                    new Input { Data = new InputData { Mouse = new MouseInput { Flags = 4 } } }
                };
                if (SendInput(2, inputs, Marshal.SizeOf<Input>()) != 2) throw new IOException("测试窗口激活输入失败。");
                await Task.Delay(250);
            }
            finally
            {
                SetWindowPos(window, new IntPtr(-2), 0, 0, 0, 0, 0x13);
                SetCursorPos(cursor.X, cursor.Y);
            }
        }
        if (GetForegroundWindow() != window || !GetWindowRect(window, out var rect))
            throw new InvalidOperationException("测试窗口未持有前台焦点，停止桌面截图。");
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || (long)width * height > 16_000_000) throw new InvalidDataException("测试窗口截图尺寸无效。");
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = IntPtr.Zero; var previous = IntPtr.Zero;
        var pixels = new byte[checked(width * height * 4)];
        try
        {
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new IOException("无法分配桌面截图资源。");
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x40CC0020)) throw new IOException("无法读取测试窗口的合成图像。");
            GdiFlush(); Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
        using var file = File.Open(path, FileMode.Create, FileAccess.ReadWrite);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XResolution, YResolution; public uint UsedColors, ImportantColors;
    }
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint colors, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32")] private static extern bool GdiFlush();
    [DllImport("gdi32")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32")] private static extern bool DeleteDC(IntPtr dc);
}
