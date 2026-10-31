using System.Runtime.InteropServices;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

namespace ClipFlow.Diagnostics;

internal static class StartupScenario
{
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 60; i++) { if (condition()) return; await Task.Delay(100); }
        throw new TimeoutException("启动功能验收未完成。");
    }
    internal static async Task<bool> CheckNativeAsync(MainViewModel vm, FrameworkElement root)
    {
        var report = new List<string>();
        StartupTask? task = null;
        try
        {
            Assert(Package.Current.Id.Name == "OpticsValley.ClipFlow.StartupAcceptance", "只允许在独立验收包中修改启动状态。");
            task = await StartupTask.GetAsync(StartupService.TaskId);
            Assert(task.State == StartupTaskState.Disabled, "独立验收包首次启动不应默认启用自启动。");
            report.Add("PASS: isolated packaged application resolves the registered StartupTask and defaults to disabled");
            await vm.RefreshStartupAsync();
            var settings = (FrameworkElement)root.FindName("SettingsContent");
            var toggle = (ToggleSwitch)settings.FindName("StartupToggle");
            Assert(toggle.IsEnabled && !toggle.IsOn, "设置页没有显示 Windows 的真实关闭状态。");
            var provider = (IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(toggle).GetPattern(PatternInterface.Toggle);
            provider.Toggle();
            await WaitAsync(() => vm.StartupEnabled && task.State == StartupTaskState.Enabled);
            report.Add("PASS: settings-page automation toggle enables the actual Windows StartupTask");
            task.Disable();
            await WaitAsync(() => task.State == StartupTaskState.Disabled);
            for (var i = 0; i < 30; i++)
            {
                await vm.RefreshStartupAsync(); await Task.Delay(100);
                if (!vm.StartupEnabled && !toggle.IsOn) break;
            }
            Assert(!vm.StartupEnabled && !toggle.IsOn, $"外部状态变化没有刷新到设置页。VM={vm.StartupEnabled}, Toggle={toggle.IsOn}, Native={task.State}, Fresh={(await StartupTask.GetAsync(StartupService.TaskId)).State}, CanChange={vm.CanChangeStartup}, Status={vm.StartupDescription}");
            report.Add("PASS: external StartupTask state changes refresh into the settings control");
            provider.Toggle(); await WaitAsync(() => vm.StartupEnabled);
            await vm.SetStartupAsync(false);
            Assert(task.State == StartupTaskState.Disabled && !vm.StartupEnabled, "关闭开机启动没有同步到 Windows。");
            report.Add("PASS: disabling in the application disables the actual Windows task");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); }
        finally
        {
            // The only task this fixture can enable belongs to the isolated acceptance identity.
            if (task?.State == StartupTaskState.Enabled) task.Disable();
            await File.WriteAllLinesAsync(Path.Combine(vm.Store.RootPath, "startup-results.txt"), report);
        }
        return report.Contains("SUCCESS");
    }
    internal static async Task CheckBackgroundAsync(MainViewModel vm, MainWindow window, TrayService tray, ThemeService themes)
    {
        var report = new List<string>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            Assert(vm.IsReady && tray.IsAvailable && tray.HasShellIcon() && !IsWindowVisible(hwnd)
                && vm.Settings.Backdrop == ClipFlow.Core.BackdropLevel.Subtle, "后台启动仍显示窗口或没有恢复本地偏好、记录与托盘。");
            await Task.Delay(300);
            Assert(!IsWindowVisible(hwnd), "后台启动后主窗口自行弹出。");
            report.Add("PASS: startup activation path initializes storage and tray without showing the main window");
            var marker = "ClipFlow silent startup fixture " + Guid.NewGuid();
            var package = new DataPackage(); package.SetText(marker);
            Assert(Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = true, IsRoamable = false }), "写入后台启动测试剪贴板失败。");
            Clipboard.Flush();
            await WaitAsync(() => vm.Records.Any(r => r.Record.Text == marker));
            Assert(!IsWindowVisible(hwnd), "后台采集弹出了窗口。");
            report.Add("PASS: real clipboard text is persisted while the never-shown main window remains hidden");
            window.RestoreWindow(); await WaitAsync(() => IsWindowVisible(hwnd));
            Assert(vm.Records.Any(r => r.Record.Text == marker), "首次打开窗口没有显示后台记录。");
            await Task.Delay(200);
            var supported = Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported();
            Assert(themes.MicaEnabledForTest == supported && (!supported || themes.CustomConfiguredForTest), "静默启动后的首次激活丢失可调云母状态。");
            await NativeWindowCapture.SaveAsync(hwnd, Path.Combine(vm.Store.RootPath, "background-first-open.png"));
            report.Add("PASS: first user activation restores the initialized window and background history");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); }
        await File.WriteAllLinesAsync(Path.Combine(vm.Store.RootPath, "background-startup-results.txt"), report);
    }
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr hwnd);
}
