using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Storage;

namespace ClipFlow.Services;

internal static class LocalDataPath
{
    internal static string GetRoot()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is { } testRoot) return testRoot;
#endif
        // MSIX owns this per-user folder. Never write into the read-only install directory.
        // Do not fall back on an access error: preserve a clear, recoverable storage failure.
        return IsPackaged() ? ApplicationData.Current.LocalFolder.Path : GetUnpackagedRoot();
    }

    internal static bool IsPackaged()
    {
        try { _ = Package.Current.Id; return true; }
        catch (InvalidOperationException) { return false; }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80073D54)) { return false; }
    }

    private static string GetUnpackagedRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipFlow");
}
