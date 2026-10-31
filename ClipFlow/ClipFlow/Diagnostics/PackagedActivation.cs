using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ClipFlow.Diagnostics;

internal static class PackagedActivation
{
    internal static void Relaunch(string root)
    {
        var arguments = "--clipflow-test=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Mode = "startup-check", Root = root })));
        var manager = (IActivationManager)new ActivationManager();
        try { Marshal.ThrowExceptionForHR(manager.ActivateApplication(Windows.ApplicationModel.AppInfo.Current.AppUserModelId, arguments, 0, out _)); }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] private class ActivationManager { }
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint process);
    }
}
