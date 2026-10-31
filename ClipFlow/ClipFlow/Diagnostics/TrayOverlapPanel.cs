using System.Runtime.InteropServices;

namespace ClipFlow.Diagnostics;

// An isolated topmost competitor reproduces the Shell overflow's z-order band
// without changing Explorer, the user's tray layout, or any system settings.
internal sealed class TrayOverlapPanel : IDisposable
{
    private readonly IntPtr _window;

    internal TrayOverlapPanel()
    {
        GetCursorPos(out var point);
        _window = CreateWindowEx(0x08000088, "STATIC", "ClipFlow tray overlap fixture", 0x80000000,
            point.X - 320, point.Y - 320, 640, 640, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new InvalidOperationException("无法创建托盘遮挡验收窗口");
        ShowWindow(_window, 4); // SW_SHOWNOACTIVATE; leave focus available to the menu.
    }

    public void Dispose() => DestroyWindow(_window);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32")] private static extern bool DestroyWindow(IntPtr window);
}
