using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace ClipFlow.Diagnostics;

internal static class TitleBarScenario
{
    internal static async Task RunAsync(MainWindow window, Grid root, string directory)
    {
        var report = new List<string>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Invoke(string name)
        {
            var button = (Button)root.FindName(name);
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        }
        async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 50; i++) { if (condition()) return; await Task.Delay(25); }
            throw new TimeoutException("标题栏操作未完成。");
        }
        try
        {
            window.RestoreWindow(); await Task.Delay(100);
            var title = (FrameworkElement)root.FindName("AppTitleBar");
            var dip = title.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(100, 24));
            var point = new Point { X = (int)(dip.X * root.XamlRoot.RasterizationScale), Y = (int)(dip.Y * root.XamlRoot.RasterizationScale) };
            Assert(ClientToScreen(hwnd, ref point), "无法读取标题栏测试坐标。");
            var packed = new IntPtr(unchecked((int)((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16))));
            Assert(SendMessage(hwnd, 0x84, UIntPtr.Zero, packed).ToInt64() == 2, "标题栏拖动区域未返回 HTCAPTION。");
            report.Add("PASS: native hit test recognizes the integrated title drag region");
            Invoke("CaptionMaximize");
            await WaitAsync(() => window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized });
            Assert(((FontIcon)root.FindName("CaptionMaximizeGlyph")).Glyph == "\uE923", "最大化后未显示还原图标。");
            Invoke("CaptionMaximize");
            await WaitAsync(() => window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored });
            report.Add("PASS: themed maximize/restore buttons operate the native presenter and update accessible state");
            Invoke("CaptionMinimize");
            await WaitAsync(() => window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized });
            window.RestoreWindow();
            await WaitAsync(() => window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored });
            report.Add("PASS: minimize and activation restore preserve the window");
            Invoke("CaptionClose"); await WaitAsync(() => !IsWindowVisible(hwnd));
            window.RestoreWindow(); await WaitAsync(() => IsWindowVisible(hwnd));
            report.Add("PASS: close routes through WM_CLOSE, hides to tray and restores without exiting");
            SendMessage(hwnd, 0xA3, new UIntPtr(2), packed);
            await WaitAsync(() => window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized });
            SendMessage(hwnd, 0xA3, new UIntPtr(2), packed);
            await WaitAsync(() => window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored });
            report.Add("PASS: native title double-click maximizes and restores");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); throw; }
        finally
        {
            window.RestoreWindow();
            await File.WriteAllLinesAsync(Path.Combine(directory, "titlebar-results.txt"), report);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr hwnd);
}
