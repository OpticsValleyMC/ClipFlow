using System.Runtime.InteropServices;

namespace ClipFlow.Services;

internal static class SystemQueue
{
    // Retain one Windows dispatcher queue for this WinUI thread for the process lifetime.
    private static Windows.System.DispatcherQueueController? _owned;
    internal static void Ensure()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        var options = new Options { Size = Marshal.SizeOf<Options>(), ThreadType = 2, Apartment = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var pointer));
        try { _owned = WinRT.MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Options { public int Size, ThreadType, Apartment; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(Options options, out IntPtr controller);
}
