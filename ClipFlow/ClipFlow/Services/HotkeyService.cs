using System.Runtime.InteropServices;

namespace ClipFlow.Services;

internal sealed class HotkeyService : IDisposable
{
    private readonly IntPtr _window;
    private readonly Action _open;
    private readonly SubclassProc _callback;
    private readonly bool _attached;
    private int _id;
    private uint _modifiers, _key;
    private bool _disposed;
    internal HotkeyService(IntPtr window, Action open)
    {
        _window = window; _open = open; _callback = WindowProc;
        _attached = SetWindowSubclass(window, _callback, 73, 0);
    }
    internal bool Register(uint modifiers, uint key)
    {
        // Keep the standard paste gesture available for the picker itself.
        if (modifiers == 2 && key == 0x56) return false;
        if (_disposed || !_attached || modifiers is 0 or > 7 || !(key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A)) return false;
        if (_id != 0 && _modifiers == modifiers && _key == key) return true;
        var next = _id == 0xCF1 ? 0xCF2 : 0xCF1;
        // Acquire the new binding before releasing the working one.
        if (!RegisterHotKey(_window, next, modifiers | 0x4000, key)) return false;
        if (_id != 0) UnregisterHotKey(_window, _id);
        _id = next; _modifiers = modifiers; _key = key;
        return true;
    }
    internal static string Label(uint modifiers, uint key)
    {
        var prefix = ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + " : "") + ((modifiers & 4) != 0 ? "Shift + " : "");
        return prefix + (key is >= 0x70 and <= 0x7A ? $"F{key - 0x6F}" : ((char)key).ToString());
    }
    private IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (!_disposed && message == 0x312 && (int)wParam.ToUInt64() == _id)
        {
            try { _open(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            return IntPtr.Zero;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_id != 0) UnregisterHotKey(_window, _id);
        if (_attached) RemoveWindowSubclass(_window, _callback, 73);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("user32", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("comctl32")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
}
