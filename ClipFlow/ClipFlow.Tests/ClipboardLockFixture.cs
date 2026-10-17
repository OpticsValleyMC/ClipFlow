using System.Runtime.InteropServices;
using System.Text;

internal static class ClipboardLockFixture
{
    public static void Run(string text, int milliseconds)
    {
        var hwnd = CreateWindowEx(0, "STATIC", "ClipFlow test clipboard owner", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero || !TryOpen(hwnd, 100)) throw new IOException("Cannot open fixture clipboard.");
        var transferred = false;
        IntPtr allocation = IntPtr.Zero;
        try
        {
            EmptyClipboard();
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            allocation = GlobalAlloc(0x2, (UIntPtr)bytes.Length);
            var pointer = GlobalLock(allocation);
            if (pointer == IntPtr.Zero) throw new IOException("Cannot allocate fixture clipboard data.");
            Marshal.Copy(bytes, 0, pointer, bytes.Length); GlobalUnlock(allocation);
            if (SetClipboardData(13, allocation) == IntPtr.Zero) throw new IOException("Cannot set fixture clipboard data.");
            transferred = true;
            CloseClipboard();
            // Other clipboard monitors can briefly open the data after CloseClipboard broadcasts.
            // Reacquire within the app's 100 ms debounce, with a strictly bounded fixture retry.
            if (!TryOpen(hwnd, 15)) throw new IOException("Cannot lock fixture clipboard within 75 ms.");
            Thread.Sleep(milliseconds);
        }
        finally
        {
            CloseClipboard();
            if (!transferred && allocation != IntPtr.Zero) GlobalFree(allocation);
            DestroyWindow(hwnd);
        }
    }
    private static bool TryOpen(IntPtr hwnd, int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (OpenClipboard(hwnd)) return true;
            Thread.Sleep(5);
        }
        return false;
    }
    [DllImport("user32", CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32")] private static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32")] private static extern bool CloseClipboard();
    [DllImport("user32")] private static extern bool EmptyClipboard();
    [DllImport("user32")] private static extern IntPtr SetClipboardData(uint format,IntPtr data);
    [DllImport("kernel32")] private static extern IntPtr GlobalAlloc(uint flags,UIntPtr size);
    [DllImport("kernel32")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32")] private static extern IntPtr GlobalFree(IntPtr memory);
}
