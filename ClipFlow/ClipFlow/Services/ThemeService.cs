using ClipFlow.Core;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ClipFlow.Services;

public sealed class ThemeService
{
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private bool _mica;
    public ThemeService(Window window, FrameworkElement root)
    {
        _window = window; _root = root;
        ConfigureBackdrop(!WindowCompatibility.ForceFallback && MicaController.IsSupported());
        root.ActualThemeChanged += (_, _) => UpdateChrome();
        UpdateChrome();
    }
    private void ConfigureBackdrop(bool supported)
    {
        _mica = false;
        _window.SystemBackdrop = null;
        if (supported)
        {
            try
            {
                _window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                _mica = true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or NotSupportedException)
            {
                _window.SystemBackdrop = null;
            }
        }
        // Keep the root transparent in XAML. A theme resource on Root.Background can
        // obscure the compositor; fallback is a separate, theme-aware bottom layer.
        if (_root.FindName("BackdropFallback") is FrameworkElement fallback)
            fallback.Visibility = _mica ? Visibility.Collapsed : Visibility.Visible;
    }
#if DEBUG
    internal bool MicaEnabledForTest => _mica;
    internal void ConfigureBackdropForTest(bool supported) => ConfigureBackdrop(supported && !WindowCompatibility.ForceFallback && MicaController.IsSupported());
#endif
    public void Apply(ThemeMode mode)
    {
        _root.RequestedTheme = mode switch { ThemeMode.Light => ElementTheme.Light, ThemeMode.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        UpdateChrome();
    }
    private void UpdateChrome()
    {
        if (!_window.ExtendsContentIntoTitleBar || WindowCompatibility.UseLegacyCaptionButtons) return;
        var dark = _root.ActualTheme == ElementTheme.Dark;
        var title = _window.AppWindow.TitleBar;
        title.ButtonBackgroundColor = Colors.Transparent;
        title.ButtonInactiveBackgroundColor = Colors.Transparent;
        title.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        title.ButtonInactiveForegroundColor = dark ? Colors.LightGray : Colors.DimGray;
        title.ButtonHoverBackgroundColor = null;
        title.ButtonPressedBackgroundColor = null;
        title.ButtonHoverForegroundColor = null;
        title.ButtonPressedForegroundColor = null;
    }
}
