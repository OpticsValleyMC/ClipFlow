using ClipFlow.Core;
using ClipFlow.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace ClipFlow.Diagnostics;

internal static class BackdropScenario
{
    internal static async Task RunAsync(MainWindow window, ThemeService themes, Grid root, string directory)
    {
        var report = new List<string>();
        var initialTheme = root.RequestedTheme;
        var supported = !WindowCompatibility.ForceFallback && MicaController.IsSupported();
        var highContrast = new AccessibilitySettings().HighContrast;
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try
        {
            report.Add($"ENV: MicaSupported={supported}; HighContrast={highContrast}");
            var fallback = (Border)root.FindName("BackdropFallback");
            var navigation = (NavigationView)root.FindName("Navigation");
            var render = (FrameworkElement)root.FindName("RenderBackdrop");
            window.RestoreWindow();
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System })
            {
                themes.Apply(mode); await Task.Delay(300);
                Assert(root.Background is SolidColorBrush { Color.A: 0 }, "主题切换后根背景遮盖系统材质。");
                Assert(navigation.Background is SolidColorBrush { Color.A: 0 }, "导航底层遮盖系统材质。");
                Assert(render.Visibility == Visibility.Collapsed, "截图用背景残留在实际界面。");
                Assert(themes.MicaEnabledForTest == supported && (window.SystemBackdrop is MicaBackdrop) == supported,
                    "系统材质与支持状态不一致。");
                Assert(fallback.Visibility == (supported ? Visibility.Collapsed : Visibility.Visible), "纯色回退层状态错误。");
                var split = FindNamed<SplitView>(navigation, "RootSplitView");
                var content = split.Content as Grid
                    ?? throw new InvalidOperationException("找不到导航主内容区域。");
                Assert(content.Background is SolidColorBrush layer && (highContrast || layer.Color.A < 255),
                    "主内容区域不是系统半透明内容层。");
                if (navigation.IsPaneOpen && !highContrast)
                    Assert(split.PaneBackground is SolidColorBrush { Color.A: 0 },
                        "展开的导航面板遮盖系统材质。");
                // Capture the actual composed desktop window, never RenderTargetBitmap's solid stand-in.
                await NativeWindowCapture.SaveAsync(WinRT.Interop.WindowNative.GetWindowHandle(window),
                    Path.Combine(directory, $"mica-native-{mode}.png"));
                report.Add($"PASS: {mode} theme uses {(supported ? "Mica Alt" : "opaque fallback")} and readable integrated title area; desktop screenshot saved");
            }
            themes.ConfigureBackdropForTest(false);
            Assert(window.SystemBackdrop is null && fallback.Visibility == Visibility.Visible, "不支持材质时没有启用纯色回退。");
            themes.Apply(ThemeMode.Light); await Task.Delay(150);
            var light = ((SolidColorBrush)fallback.Background).Color;
            themes.Apply(ThemeMode.Dark); await Task.Delay(150);
            var dark = ((SolidColorBrush)fallback.Background).Color;
            Assert(light.A == 255 && dark.A == 255 && (highContrast || light != dark), "回退颜色没有跟随主题。");
            report.Add("PASS: unsupported-material fallback is opaque and follows Light/Dark themes");
            themes.ConfigureBackdropForTest(supported);
            window.AppWindow.Hide(); await Task.Delay(100);
            window.RestoreWindow(); await Task.Delay(250);
            Assert(themes.MicaEnabledForTest == supported && (window.SystemBackdrop is MicaBackdrop) == supported,
                "后台恢复后材质丢失。");
            if (window.ExtendsContentIntoTitleBar && WindowCompatibility.UseLegacyCaptionButtons)
            {
                Assert(window.AppWindow.TitleBar.PreferredHeightOption == Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed
                    && ((FrameworkElement)root.FindName("LegacyCaptionButtons")).Visibility == Visibility.Visible
                    && ((FrameworkElement)root.FindName("AppTitleBar")).Visibility == Visibility.Visible,
                    "Windows 10 标题区域仍显示系统白条或缺少窗口按钮。");
                await TitleBarScenario.RunAsync(window, root, directory);
            }
            else if (window.ExtendsContentIntoTitleBar)
                Assert(window.AppWindow.TitleBar.ButtonBackgroundColor?.A == 0
                    && window.AppWindow.TitleBar.ButtonInactiveBackgroundColor?.A == 0, "标题栏按钮背景遮盖材质。");
            else
                Assert(((FrameworkElement)root.FindName("AppTitleBar")).Visibility == Visibility.Collapsed
                    && root.RowDefinitions[0].Height.Value == 0, "原生标题栏降级仍占用自定义标题栏空间。");
            report.Add("PASS: background hide/restore preserves backdrop and readable custom/native caption");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); throw; }
        finally
        {
            themes.ConfigureBackdropForTest(supported);
            root.RequestedTheme = initialTheme;
            await File.WriteAllLinesAsync(Path.Combine(directory, "mica-results.txt"), report);
        }
    }
    private static T FindNamed<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name) return element;
            try { return FindNamed<T>(child, name); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"找不到 {name}。");
    }
}
