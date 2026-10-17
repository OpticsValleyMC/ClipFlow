using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Storage;

namespace ClipFlow.Services;

internal static class LocalDataPath
{
    internal static string GetRoot()
    {
        try { _ = Package.Current.Id; }
        catch (InvalidOperationException) { return GetUnpackagedRoot(); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80073D54))
        { return GetUnpackagedRoot(); } // APPMODEL_ERROR_NO_PACKAGE

        // MSIX owns this per-user folder. Never write into the read-only install directory.
        // Do not fall back on an access error: preserve a clear, recoverable storage failure.
        return ApplicationData.Current.LocalFolder.Path;
    }

    private static string GetUnpackagedRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipFlow");
}
