using ClipFlow.Core;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ClipFlow.Diagnostics;

internal static class FeatureScenario
{
    internal static async Task RunAsync(MainViewModel vm, MainWindow window, ThemeService themes, FrameworkElement root, Func<string, Task> setText, Action<string> navigate)
    {
        var original = vm.Settings;
        var added = new List<RecordViewModel>();
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 50; i++) { if (condition()) return; await Task.Delay(100); }
            throw new TimeoutException("新增功能验收未完成。");
        }
        try
        {
            var native = new FakeStartup();
            var startup = new StartupService(() => Task.FromResult<IStartupRegistration?>(native));
            Assert((await startup.RefreshAsync()) is { Enabled: false, CanChange: true } && native.Enables == 0, "读取状态不应启用开机启动。");
            var enabled = await Task.WhenAll(startup.SetEnabledAsync(true), startup.SetEnabledAsync(true));
            Assert(enabled.All(s => s.Enabled) && native.Enables == 1, "并发请求重复启用了开机启动。");
            Assert(!(await startup.SetEnabledAsync(false)).Enabled && native.Disables == 1, "应用关闭开机启动失败。");
            native.Current = StartupTaskState.DisabledByUser;
            Assert((await startup.SetEnabledAsync(true)) is { Enabled: false, CanChange: false } && native.Enables == 1, "不应覆盖任务管理器的用户禁用。");
            native.Current = StartupTaskState.DisabledByPolicy;
            Assert(!(await startup.SetEnabledAsync(true)).CanChange && native.Enables == 1, "不应覆盖系统禁用策略。");
            native.Current = StartupTaskState.EnabledByPolicy;
            Assert((await startup.SetEnabledAsync(false)) is { Enabled: true, CanChange: false } && native.Disables == 1, "不应覆盖系统启用策略。");
            var unsupported = new StartupService(() => Task.FromResult<IStartupRegistration?>(null));
            Assert((await unsupported.SetEnabledAsync(true)) is { Enabled: false, CanChange: false }, "无启动注册能力时不应伪造启用状态。");
            await vm.RefreshStartupAsync();
            Assert(!vm.CanChangeStartup && !vm.StartupEnabled, "隔离便携验收不能改变真实开机设置。");
            await CheckPortableStartupAsync(vm.Store.RootPath, Assert);

            await vm.UpdateSettingsAsync(vm.Settings with { Backdrop = BackdropLevel.Solid });
            Assert(window.SystemBackdrop is null && ((FrameworkElement)root.FindName("BackdropFallback")).Visibility == Visibility.Visible, "纯色模式未关闭材质。");
            var settingsView = (FrameworkElement)root.FindName("SettingsContent");
            var backdropBox = (ComboBox)settingsView.FindName("BackdropBox");
            navigate("settings"); await Task.Delay(100);
            var languageBox = (ComboBox)settingsView.FindName("LanguageBox");
            Assert(languageBox.IsEnabled && languageBox.Items.Count == 4 && languageBox.SelectedIndex == (int)original.Language,
                "设置页未显示当前语言和全部选项。");
            languageBox.SelectedIndex = (int)AppLanguage.English;
            await WaitAsync(() => vm.Settings.Language == AppLanguage.English);
            Assert(await new HistoryStore(vm.Store.RootPath).ReadLanguageAsync() == AppLanguage.English, "语言选择未写入本地设置。");
            languageBox.SelectedIndex = (int)original.Language;
            await WaitAsync(() => vm.Settings.Language == original.Language);
            var expand = (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(backdropBox).GetPattern(PatternInterface.ExpandCollapse);
            expand.Expand(); await Task.Delay(100);
            await NativeKeyboard.PressAsync(0x24); // Home, two Down keys, Enter: the actual keyboard selection path.
            await NativeKeyboard.PressAsync(0x28);
            await NativeKeyboard.PressAsync(0x28);
            await NativeKeyboard.PressAsync(0x0D);
            expand.Collapse();
            await WaitAsync(() => vm.Settings.Backdrop == BackdropLevel.Standard);
            foreach (var level in new[] { BackdropLevel.Solid, BackdropLevel.Subtle, BackdropLevel.Standard, BackdropLevel.Strong })
            {
                foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
                {
                    await vm.UpdateSettingsAsync(vm.Settings with { Backdrop = level, Theme = theme });
                    await Task.Delay(150);
                    var supported = level != BackdropLevel.Solid && !WindowCompatibility.ForceFallback && MicaController.IsSupported();
                    Assert(themes.MicaEnabledForTest == supported && backdropBox.SelectedIndex == (int)level, $"材质档位或控件双向同步未遵守设置。Level={level}, Theme={theme}, Active={themes.MicaEnabledForTest}, SelectedIndex={backdropBox.SelectedIndex}");
                    if (supported && level != BackdropLevel.Strong)
                        Assert(themes.CustomConfiguredForTest, "主题切换重置了材质档位参数。");
                    await NativeWindowCapture.SaveAsync(WinRT.Interop.WindowNative.GetWindowHandle(window), Path.Combine(vm.Store.RootPath, $"backdrop-{level}-{theme}.png"));
                }
            }
            await vm.UpdateSettingsAsync(vm.Settings with { Backdrop = BackdropLevel.Solid });
            foreach (var text in new[] { "ClipFlow favorite Alpha fixture", "ClipFlow favorite Beta fixture" })
            {
                navigate("recent"); await setText(text);
                await WaitAsync(() => vm.Records.Any(r => r.Record.Text == text));
                var record = vm.Records.Single(r => r.Record.Text == text); added.Add(record);
                await vm.FavoriteAsync(record);
            }
            navigate("favorites");
            await vm.UpdateSettingsAsync(vm.Settings with { FavoriteSort = FavoriteSortMode.OldestCopy });
            await vm.PinAsync(added[1]);
            Assert(vm.Records[0].Id == added[1].Id && added[1].PinnedVisibility == Visibility.Visible, "置顶没有高于时间排序。");
            vm.Search = "Alpha fixture";
            Assert(vm.Records.Count == 1 && vm.Records[0].Id == added[0].Id, "搜索不应显示不匹配的置顶记录。");
            vm.Search = "";
            await vm.PinAsync(added[1]);
            await vm.UpdateSettingsAsync(vm.Settings with { FavoriteSort = FavoriteSortMode.Content });
            var fixtureOrder = vm.Records.Where(r => added.Any(a => a.Id == r.Id)).Select(r => r.Id).ToArray();
            Assert(fixtureOrder.SequenceEqual(added.Select(a => a.Id)), "收藏内容排序错误。");
            await vm.PinAsync(added[1]);
            var restart = new HistoryStore(vm.Store.RootPath); await restart.InitializeAsync();
            Assert((await restart.LoadAsync()).Single(r => r.Id == added[1].Id).IsPinned
                && (await restart.LoadSettingsAsync()) is { Backdrop: BackdropLevel.Solid, FavoriteSort: FavoriteSortMode.Content }, "新增设置重开后丢失。");
            // Let the navigation transition and virtualized item realization finish before capture.
            await Task.Delay(250); root.UpdateLayout();
            await IntegrationScenario.RenderAsync(root, Path.Combine(vm.Store.RootPath, "favorites-pinned.png"));
            navigate("recent");
            Assert(added[1].PinActionVisibility == Visibility.Collapsed, "最近记录不应显示收藏置顶操作。");
        }
        finally
        {
            foreach (var item in added) await vm.DeleteAsync(item);
            await vm.ChangeLanguageAsync(original.Language);
            await vm.UpdateSettingsAsync(original);
            navigate("recent");
        }
    }

    private static async Task CheckPortableStartupAsync(string dataRoot, Action<bool, string> assert)
    {
        const string testParent = @"Software\ClipFlow\StartupAcceptance";
        var root = testParent + "\\" + Guid.NewGuid().ToString("N");
        var runPath = root + "\\Run";
        var approvalPath = root + "\\Approved";
        var executable = Path.Combine(dataRoot, "ClipFlow portable fixture.exe");
        File.WriteAllBytes(executable, []);
        try
        {
            var registration = new PortableStartupRegistration(executable, runPath, approvalPath);
            var startup = new StartupService(() => Task.FromResult<IStartupRegistration?>(registration));
            assert((await startup.RefreshAsync()) is { Enabled: false, CanChange: true }, "便携版自启动应默认关闭。");
            assert((await startup.SetEnabledAsync(true)).Enabled, "便携版未启用自启动。");
            using (var run = Registry.CurrentUser.OpenSubKey(runPath))
                assert(run?.GetValue(PortableStartupRegistration.ValueName) as string == $"\"{executable}\" --startup", "便携版启动命令未正确引用路径和后台参数。");

            using (var approval = Registry.CurrentUser.CreateSubKey(approvalPath))
            {
                approval.SetValue(PortableStartupRegistration.ValueName, BitConverter.GetBytes(3u));
                assert((await startup.SetEnabledAsync(true)) is { Enabled: false, CanChange: false }, "便携版应尊重 Windows 禁用状态。");
                approval.DeleteValue(PortableStartupRegistration.ValueName);
            }

            File.Delete(executable);
            assert((await startup.RefreshAsync()) is { Enabled: false, CanChange: true }, "便携版移动后应可重新注册自启动。");
            File.WriteAllBytes(executable, []);
            assert(!(await startup.SetEnabledAsync(false)).Enabled, "关闭便携版自启动失败。");
            using var cleared = Registry.CurrentUser.OpenSubKey(runPath);
            assert(cleared?.GetValue(PortableStartupRegistration.ValueName) is null, "便携版启动项没有被移除。");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKey(runPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKey(approvalPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKey(root, throwOnMissingSubKey: false);
            File.Delete(executable);
        }
    }

    private sealed class FakeStartup : IStartupRegistration
    {
        internal StartupTaskState Current = StartupTaskState.Disabled;
        internal int Enables, Disables;
        public StartupTaskState State => Current;
        public async Task<StartupTaskState> EnableAsync() { Enables++; await Task.Delay(20); return Current = StartupTaskState.Enabled; }
        public void Disable() { Disables++; Current = StartupTaskState.Disabled; }
    }
}
