using ClipFlow.Core;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace ClipFlow.Services;

public sealed class ThemeService
{
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private bool _mica;
    private BackdropLevel _level = BackdropLevel.Strong;
    private MicaController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private bool _active;
    public ThemeService(Window window, FrameworkElement root)
    {
        _window = window; _root = root;
        ConfigureBackdrop(!WindowCompatibility.ForceFallback && MicaController.IsSupported());
        root.ActualThemeChanged += (_, _) => UpdateChrome();
        window.Activated += (_, args) =>
        {
            _active = args.WindowActivationState != WindowActivationState.Deactivated;
            if (_configuration is not null) _configuration.IsInputActive = _active;
        };
        window.Closed += (_, _) => ReleaseController();
        UpdateChrome();
    }
    private void ConfigureBackdrop(bool supported)
    {
        _mica = false;
        ReleaseController();
        _window.SystemBackdrop = null;
        if (supported)
        {
            try
            {
                if (_level == BackdropLevel.Strong) _window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                else
                {
                    SystemQueue.Ensure();
                    _controller = new MicaController { Kind = MicaKind.BaseAlt };
                    _configuration = new SystemBackdropConfiguration { IsInputActive = _active };
                    _controller.SetSystemBackdropConfiguration(_configuration);
                    if (!_controller.AddSystemBackdropTarget(_window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>()))
                        throw new NotSupportedException("无法附加系统背景材质。");
                    UpdateController();
                }
                _mica = true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or NotSupportedException)
            {
                _window.SystemBackdrop = null;
                ReleaseController();
            }
        }
        // Keep the root transparent in XAML. A theme resource on Root.Background can
        // obscure the compositor; fallback is a separate, theme-aware bottom layer.
        if (_root.FindName("BackdropFallback") is FrameworkElement fallback)
            fallback.Visibility = _mica ? Visibility.Collapsed : Visibility.Visible;
    }
#if DEBUG
    internal bool MicaEnabledForTest => _mica;
    internal bool CustomConfiguredForTest => _controller is not null
        && Math.Abs(_controller.TintOpacity - (_level == BackdropLevel.Subtle ? .85f : .55f)) < .001f
        && Math.Abs(_controller.LuminosityOpacity - (_level == BackdropLevel.Subtle ? .98f : .85f)) < .001f;
    internal void ConfigureBackdropForTest(bool supported) => ConfigureBackdrop(supported && !WindowCompatibility.ForceFallback && MicaController.IsSupported());
#endif
    public void Apply(ThemeMode mode)
    {
        _root.RequestedTheme = mode switch { ThemeMode.Light => ElementTheme.Light, ThemeMode.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        UpdateChrome();
    }
    public void ApplySettings(AppSettings settings)
    {
        Apply(settings.Theme);
        if (_level == settings.Backdrop) return;
        _level = settings.Backdrop;
        ConfigureBackdrop(_level != BackdropLevel.Solid && !WindowCompatibility.ForceFallback && MicaController.IsSupported());
    }
    private Windows.UI.Color TintColor() => _root.FindName("BackdropFallback") is Microsoft.UI.Xaml.Controls.Border { Background: SolidColorBrush brush }
        ? brush.Color : _root.ActualTheme == ElementTheme.Dark ? Colors.Black : Colors.White;
    private void UpdateChrome()
    {
        UpdateController();
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
    private void UpdateController()
    {
        if (_controller is null || _configuration is null) return;
        _configuration.Theme = _root.ActualTheme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        _controller.TintColor = TintColor();
        _controller.TintOpacity = _level == BackdropLevel.Subtle ? .85f : .55f;
        _controller.LuminosityOpacity = _level == BackdropLevel.Subtle ? .98f : .85f;
    }
    private void ReleaseController()
    {
        var controller = _controller; _controller = null; _configuration = null;
        controller?.Dispose();
    }
}
