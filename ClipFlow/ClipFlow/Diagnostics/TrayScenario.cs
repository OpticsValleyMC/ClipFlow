using System.Runtime.InteropServices;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace ClipFlow.Diagnostics;

internal static class TrayScenario
{
    internal static async Task RunAsync(MainViewModel vm, MainWindow window, TrayService tray)
    {
        var report = new List<string>();
        var path = Path.Combine(vm.Store.RootPath, "tray-results.txt");
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 60; i++) { if (condition()) return; await Task.Delay(100); }
            throw new TimeoutException("托盘行为超时。");
        }
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Assert(tray.IsAvailable && tray.HasShellIcon(), "Shell 未注册托盘图标。");
            Assert(tray.MenuLabelsForTest().SequenceEqual(new[] { "ClipFlow 1.0.0.0", "打开 ClipFlow", vm.RecordingButton, "退出" }), "原生菜单内容不匹配。");
            report.Add("PASS: Shell icon registered; native menu contains app/version, open, recording toggle, exit");
            await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = true });
            SendMessage(hwnd, 0x10, UIntPtr.Zero, IntPtr.Zero);
            await WaitAsync(() => !IsWindowVisible(hwnd));
            var marker = "ClipFlow background capture " + Guid.NewGuid();
            var package = new DataPackage(); package.SetText(marker);
            await ClipFlow.Core.RetryPolicy.RunAsync(() =>
            {
                if (!Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = true, IsRoamable = false }))
                    throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
                Clipboard.Flush(); return Task.FromResult(true);
            }, ex => ex.HResult is unchecked((int)0x800401D0) or unchecked((int)0x80070005));
            await WaitAsync(() => vm.Records.Any(record => record.Record.Text == marker));
            report.Add("PASS: system close hides window; real clipboard capture continues while hidden");
            var thread = GetWindowThreadProcessId(hwnd, out _);
            async Task CheckMenuAsync(bool mainVisible, uint selectedCommand = 0, uint selectedPosition = 2)
            {
                var menuCheck = Task.Run(async () =>
                {
                    bool ActiveMenu()
                    {
                        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
                        return GetGUIThreadInfo(thread, ref info) && (info.Flags & 0x10) != 0
                            && info.MenuOwner == tray.MenuOwnerForTest;
                    }
                    var clicked = false;
                    try
                    {
                        for (var i = 0; i < 40 && !ActiveMenu(); i++) await Task.Delay(50);
                        Assert(ActiveMenu(), "托盘菜单未进入原生菜单循环。");
                        Assert(GetForegroundWindow() == tray.MenuOwnerForTest, "菜单所有者未获得前台焦点。");
                        // A fleeting popup is not a pass. It must survive Shell follow-up messages.
                        tray.RequestLegacyRightButtonForTest();
                        tray.RequestMenuForTest();
                        for (var i = 0; i < 20; i++)
                        {
                            await Task.Delay(50);
                            Assert(ActiveMenu(), "托盘菜单闪退或被重复回调关闭。");
                            Assert(IsWindowVisible(hwnd) == mainVisible, "弹出托盘菜单改变了主窗口可见性。");
                        }
                        if (selectedCommand != 0)
                        {
                            // Click the actual native row, using its screen rectangle.
                            Assert(GetForegroundWindow() == tray.MenuOwnerForTest, "鼠标测试前菜单失去焦点，停止输入。");
                            var center = tray.MenuItemCenterForTest(selectedPosition);
                            Assert(SetCursorPos(center.X, center.Y), "定位原生菜单项失败。");
                            await Task.Delay(100);
                            Assert(GetForegroundWindow() == tray.MenuOwnerForTest, "菜单失去焦点，停止点击。");
                            var inputs = new[]
                            {
                                new Input { Data = new InputData { Mouse = new MouseInput { Flags = 2 } } },
                                new Input { Data = new InputData { Mouse = new MouseInput { Flags = 4 } } }
                            };
                            Assert(SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2, "原生菜单鼠标输入失败。");
                            clicked = true;
                            await Task.Delay(250);
                        }
                    }
                    finally
                    {
                        if (!clicked || ActiveMenu()) PostMessage(tray.MenuOwnerForTest, 0x1F, UIntPtr.Zero, IntPtr.Zero);
                    }
                });
                tray.RequestMenuForTest();
                await menuCheck;
                await WaitAsync(() => !tray.MenuOpenForTest);
                if (selectedCommand != 0) Assert(tray.LastMenuCommandForTest == selectedCommand,
                    $"菜单未返回 {selectedCommand:X} 命令，实际值 {tray.LastMenuCommandForTest:X}。");
                Assert(!IsWindowVisible(tray.MenuOwnerForTest), "菜单结束后辅助窗口未隐藏。");
            }
            await CheckMenuAsync(false);
            await CheckMenuAsync(false);
            report.Add("PASS: hidden-window popup stays open for 1 second, ignores duplicate callbacks, cancels and reopens");
            Assert(tray.MenuLabelsForTest()[2] == "暂停记录", "开启记录时托盘标签错误。");
            await CheckMenuAsync(false, TrayService.ToggleRecordingCommand, 3);
            await WaitAsync(() => !vm.RecordingEnabled);
            Assert(!IsWindowVisible(hwnd) && tray.MenuLabelsForTest()[2] == "继续记录", "托盘暂停未同步状态或打开了窗口。");
            Assert(!(await vm.Store.LoadSettingsAsync()).RecordingEnabled, "托盘暂停状态未持久化。");
            var countBeforePause = (await vm.Store.LoadAsync()).Count;
            var paused = new DataPackage(); paused.SetText("paused tray fixture " + Guid.NewGuid());
            await ClipFlow.Core.RetryPolicy.RunAsync(() =>
            {
                if (!Clipboard.SetContentWithOptions(paused, new ClipboardContentOptions { IsAllowedInHistory = true, IsRoamable = false }))
                    throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
                Clipboard.Flush(); return Task.FromResult(true);
            }, ex => ex.HResult is unchecked((int)0x800401D0) or unchecked((int)0x80070005));
            await Task.Delay(400);
            Assert((await vm.Store.LoadAsync()).Count == countBeforePause, "托盘暂停期间仍保存了内容。");
            await CheckMenuAsync(false, TrayService.ToggleRecordingCommand, 3);
            await WaitAsync(() => vm.RecordingEnabled);
            Assert(!IsWindowVisible(hwnd) && tray.MenuLabelsForTest()[2] == "暂停记录"
                && (await vm.Store.LoadSettingsAsync()).RecordingEnabled, "托盘继续记录未生效或未持久化。");
            await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = false });
            Assert(tray.MenuLabelsForTest()[2] == "继续记录", "界面修改状态后托盘标签未同步。");
            await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = true });
            report.Add("PASS: real mouse clicks pause/resume while hidden; capture stops, settings persist, UI/menu labels stay synchronized");
            await CheckMenuAsync(false, TrayService.OpenCommand);
            await WaitAsync(() => IsWindowVisible(hwnd));
            report.Add("PASS: real mouse click on native Open menu item restores the hidden main window");
            await CheckMenuAsync(true);
            report.Add("PASS: visible-window popup stays open and closes without changing main window visibility");
            tray.RequestRestoreForTest();
            await WaitAsync(() => IsWindowVisible(hwnd));
            report.Add("PASS: tray selection callback restores window");
            SendMessage(hwnd, 0x10, UIntPtr.Zero, IntPtr.Zero);
            // A second process must redirect to the hidden instance and exit, not start another recorder.
            using var second = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
            Assert(second is not null, "无法启动第二进程。");
            await WaitAsync(() => second!.HasExited && IsWindowVisible(hwnd));
            Assert(second!.ExitCode == 0, "第二进程未正常退出。");
            report.Add("PASS: relaunch restores the hidden instance and avoids a second recorder");
            tray.RecreateForTest();
            await Task.Delay(200);
            Assert(tray.IsAvailable && tray.HasShellIcon(), "任务栏重建后图标不可用。");
            report.Add("PASS: taskbar recreation callback preserves icon availability");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); }
        await File.WriteAllLinesAsync(path, report);
    }
    [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32")] private static extern bool SetCursorPos(int x, int y);
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32")] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
}
