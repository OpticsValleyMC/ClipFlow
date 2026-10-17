using System.Runtime.InteropServices;

namespace ClipFlow.Services;

internal static class ClipboardPrivacy
{
    private static readonly uint Exclude = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint History = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint Ignore = RegisterClipboardFormat("Clipboard Viewer Ignore");
    private static readonly uint Password = RegisterClipboardFormat("application/x-keepassxc-sensitive");
    public static bool ShouldSkip()
    {
        if (!OpenClipboard(IntPtr.Zero)) throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
        try
        {
            if (IsClipboardFormatAvailable(Exclude) || IsClipboardFormatAvailable(Ignore) || IsClipboardFormatAvailable(Password)) return true;
            if (!IsClipboardFormatAvailable(History)) return false;
            var handle = GetClipboardData(History);
            if (handle == IntPtr.Zero || GlobalSize(handle).ToUInt64() < 4) return true;
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return true;
            try { return Marshal.ReadInt32(pointer) == 0; }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }
    [DllImport("user32")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32")] private static extern UIntPtr GlobalSize(IntPtr memory);
}
