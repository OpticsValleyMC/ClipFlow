using Microsoft.UI.Windowing;

namespace ClipFlow.Services;

internal static class WindowCompatibility
{
    internal static bool UseCustomTitleBar => AppWindowTitleBar.IsCustomizationSupported();
    // Windows 10 ignores native caption colors. Use theme-aware WinUI buttons,
    // backed by native window operations, instead of leaving a white button strip.
    internal static bool UseLegacyCaptionButtons => !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || ForceFallback;
#if DEBUG
    internal static bool ForceFallback => Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null
        && Environment.GetEnvironmentVariable("CLIPFLOW_TEST_LEGACY_CHROME") == "1";
#else
    internal const bool ForceFallback = false;
#endif
}
