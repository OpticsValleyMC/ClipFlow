using System.Runtime.InteropServices;
using ClipFlow.Core;
using ClipFlow.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Windows.Graphics;

namespace ClipFlow.Views;

// The window only supplies a XamlRoot and activation target. WinUI owns the
// actual popup, its material, layout, check marks, focus and light dismissal.
internal sealed class TrayMenuWindow : Window
{
    private readonly Grid _root = new();
    private readonly MenuFlyout _menu = new() { ShouldConstrainToRootBounds = false };
    private readonly Dictionary<uint, MenuFlyoutItemBase> _commands = [];
    private readonly Action<uint> _invoke;
    private bool _disposed, _open, _popupOpen, _standardMenu, _loaded, _shutdown;
    internal IntPtr NativeHandle { get; }
    internal bool IsMenuOpen => !_disposed && _open && _popupOpen;
    internal event Action? Dismissed;

    internal TrayMenuWindow(Action<uint> invoke)
    {
        _invoke = invoke;
        NativeHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Content = _root;
        AppWindow.SetPresenter(OverlappedPresenter.CreateForContextMenu());
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            // The notification-area overflow is topmost. Its menu must share
            // that z-order band; foreground activation alone cannot raise it.
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
            presenter.PreferredMinimumWidth = 1; presenter.PreferredMinimumHeight = 1;
        }
        _menu.Opened += (_, _) =>
        {
            Trace("Opened");
            _standardMenu = _menu.Items.All(item => item is MenuFlyoutItem or ToggleMenuFlyoutItem or MenuFlyoutSeparator);
            _popupOpen = true;
        };
        _menu.Closing += (_, _) => Trace("Closing");
        _menu.Closed += (_, _) => { Trace("Closed"); _popupOpen = false; HideOwner(); };
        Activated += (_, args) => Trace("Activated: " + args.WindowActivationState);
        AppWindow.Closing += (_, args) => { if (!_shutdown) { args.Cancel = true; Dismiss(); } };
        Closed += (_, _) => { _disposed = true; _open = false; Dismissed?.Invoke(); };
    }

    internal async Task ShowAsync(int x, int y, ThemeMode theme, (bool Ready, bool Recording) recording, StartupSnapshot startup)
    {
        _root.RequestedTheme = theme switch { ThemeMode.Light => ElementTheme.Light, ThemeMode.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        _menu.Items.Clear(); _commands.Clear();
        _menu.Items.Add(new MenuFlyoutItem { Text = AppInfo.VersionLabel, IsEnabled = false });
        _menu.Items.Add(new MenuFlyoutSeparator());
        Add(TrayService.OpenCommand, "打开 ClipFlow", "\uE8A7");
        Add(TrayService.ToggleRecordingCommand, !recording.Ready ? "记录不可用" : recording.Recording ? "暂停记录" : "继续记录",
            recording.Recording ? "\uE769" : "\uE768", recording.Ready);
        _menu.Items.Add(new MenuFlyoutSeparator());
        var startupItem = new AlignedToggleMenuFlyoutItem { Text = "开机自启动", IsChecked = startup.Enabled, IsEnabled = startup.CanChange };
        ToolTipService.SetToolTip(startupItem, startup.Description);
        startupItem.Click += (_, _) => Select(TrayService.ToggleStartupCommand);
        _menu.Items.Add(startupItem); _commands[TrayService.ToggleStartupCommand] = startupItem;
        _menu.Items.Add(new MenuFlyoutSeparator());
        Add(TrayService.ExitCommand, "退出", "\uE7E8");

        var work = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(x, work.X, work.X + work.Width - 1),
            Math.Clamp(y, work.Y, work.Y + work.Height - 1), 1, 1));
        // Wait for a loaded XamlRoot before opening the framework's separate popup.
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object sender, RoutedEventArgs args) { _loaded = true; loaded.TrySetResult(); }
        _root.Loaded += OnLoaded;
        try
        {
            _open = true;
            AppWindow.Show();
            Activate(); SetForegroundWindow(NativeHandle);
            if (!_loaded) await loaded.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => activated.TrySetResult()))
                throw new InvalidOperationException("无法调度托盘菜单");
            await activated.Task;
            if (_disposed || !_open) return;
            Trace("ShowAt");
            _menu.ShowAt(_root, new Windows.Foundation.Point(0, 0));
        }
        finally { _root.Loaded -= OnLoaded; }
    }

    private void Add(uint command, string text, string glyph, bool enabled = true)
    {
        var icon = glyph == "\uE769"
            ? (IconElement)XamlReader.Load("<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M6,3 A0.65,0.65 0 0 1 6.65,3.65 L6.65,16.35 A0.65,0.65 0 0 1 5.35,16.35 L5.35,3.65 A0.65,0.65 0 0 1 6,3 Z M14,3 A0.65,0.65 0 0 1 14.65,3.65 L14.65,16.35 A0.65,0.65 0 0 1 13.35,16.35 L13.35,3.65 A0.65,0.65 0 0 1 14,3 Z'/>")
            : new FontIcon { Glyph = glyph };
        var item = new MenuFlyoutItem { Text = text, Icon = icon, IsEnabled = enabled };
        item.Click += (_, _) => Select(command);
        _menu.Items.Add(item); _commands[command] = item;
    }
    private void Select(uint command) { Dismiss(); if (!_disposed) _invoke(command); }
    private void HideOwner()
    {
        if (!_open || _disposed) return;
        _open = false; AppWindow.Hide(); Dismissed?.Invoke();
    }
    internal void Dismiss() { _menu.Hide(); HideOwner(); }
    internal void Shutdown() { _shutdown = true; Dismiss(); Close(); }

    [System.Diagnostics.Conditional("DEBUG")]
    private void Trace(string message)
    {
        var path = Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT");
        if (path is not null) File.AppendAllText(Path.Combine(path, "tray-menu-trace.txt"), $"{DateTime.UtcNow:O} {message}; foreground={GetForegroundWindow()}; owner={NativeHandle}\n");
    }

#if DEBUG
    internal bool StartupCheckAlignedForTest
    {
        get
        {
            var open = ((MenuFlyoutItem)_commands[TrayService.OpenCommand]).Icon;
            var check = ((ToggleMenuFlyoutItem)_commands[TrayService.ToggleStartupCommand]).Icon;
            var first = open.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(open.ActualWidth / 2, 0));
            var second = check.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(check.ActualWidth / 2, 0));
            return Math.Abs(first.X - second.X) < 1;
        }
    }
    internal (int X, int Y) CommandCenter(uint command)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(_commands[command]);
        var bounds = peer.GetBoundingRectangle();
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("标准菜单项尚未显示");
        // The in-process peer reports physical coordinates relative to the
        // XamlRoot's owner. UIA's external provider normally maps them to screen.
        var popups = new List<IntPtr>();
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (IsWindowVisible(window))
            {
                var name = new System.Text.StringBuilder(256);
                GetClassName(window, name, name.Capacity);
                Trace($"Visible window={window}; class={name}; owner={GetAncestor(window, 3)}");
                if (name.ToString().Contains("Popup", StringComparison.Ordinal) && GetAncestor(window, 3) == NativeHandle) popups.Add(window);
            }
            return true;
        }, IntPtr.Zero);
        if (popups.Count != 1 || !GetWindowRect(popups[0], out var rect)) throw new InvalidOperationException("无法唯一定位标准菜单弹出窗口");
        var center = new Point { X = (int)Math.Round(bounds.X + bounds.Width / 2), Y = (int)Math.Round(bounds.Y + bounds.Height / 2) };
        if (!ClientToScreen(NativeHandle, ref center)) throw new InvalidOperationException("无法读取标准菜单项的屏幕坐标");
        Trace($"Command {command:X}: bounds={bounds}; popup={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}; point={center.X},{center.Y}; hit={GetAncestor(WindowFromPoint(center), 2)}");
        var path = Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT");
        if (path is not null)
        {
            var file = command == TrayService.ToggleStartupCommand ? ((ToggleMenuFlyoutItem)_commands[command]).IsChecked ? "tray-menu-startup-checked.png" : "tray-menu-startup.png"
                : _commands[command] is MenuFlyoutItem { Text: "暂停记录" } ? "tray-menu-pause.png" : "tray-menu.png";
            _ = Diagnostics.NativeWindowCapture.SaveOwnWindowAsync(popups[0], Path.Combine(path, file));
        }
        if (GetAncestor(WindowFromPoint(center), 2) != popups[0]) throw new InvalidOperationException("标准菜单项被其他窗口遮挡，停止点击");
        return (center.X, center.Y);
    }
    internal bool UsesStandardMenu => IsMenuOpen && _standardMenu;
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowProc(IntPtr window, IntPtr data);
    [DllImport("user32")] private static extern bool EnumThreadWindows(uint thread, EnumWindowProc callback, IntPtr data);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
#endif
    [DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
}
