using System.Runtime.InteropServices;

namespace ClipFlow.Services;

// Native notification-area icon and menu. All calls and callbacks stay on the window's UI thread.
internal sealed class TrayService : IDisposable
{
    internal const uint CallbackMessage = 0x8000 + 71;
    internal const uint OpenCommand = 0xCF01, ExitCommand = 0xCF02, ToggleRecordingCommand = 0xCF03;
    private const uint ShowMenuMessage = 0x8000 + 72;
    private const uint IconId = 1;
    private readonly IntPtr _window, _icon, _menuWindow;
    private readonly Action _open, _exit, _toggleRecording;
    private readonly Func<(bool Ready, bool Recording)> _recordingState;
    private readonly SubclassProc _callback;
    private readonly uint _taskbarCreated;
    private bool _disposed, _subclassed, _version4, _menuQueued, _menuOpen;
    private Point _menuPoint;
    public bool IsAvailable { get; private set; }
    internal static string VersionLabel => AppInfo.VersionLabel;

    public TrayService(IntPtr window, Action open, Action exit, Action toggleRecording,
        Func<(bool Ready, bool Recording)> recordingState)
    {
        _window = window; _open = open; _exit = exit; _callback = WindowProc;
        _toggleRecording = toggleRecording; _recordingState = recordingState;
        // WinUI's hidden main window cannot reliably own an active native menu.
        // A separate off-screen tool window keeps menu activation out of WinUI.
        _menuWindow = CreateWindowEx(0x80, "STATIC", "ClipFlow Tray Menu Owner", 0x80000000,
            -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var iconSize = GetSystemMetricsForDpi(49, GetDpiForWindow(window));
        _icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "ClipFlow.ico"), 1, iconSize, iconSize, 0x10);
        if (_icon == IntPtr.Zero) _icon = CreateClipFlowIcon();
        _subclassed = SetWindowSubclass(_window, _callback, 71, 0);
        if (_subclassed && _icon != IntPtr.Zero && _menuWindow != IntPtr.Zero) AddIcon();
    }
    private NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = IconId,
        Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = _icon,
        Tip = VersionLabel, Info = "", InfoTitle = ""
    };
    private void AddIcon()
    {
        if (_disposed || !_subclassed || _icon == IntPtr.Zero || _menuWindow == IntPtr.Zero) return;
        var data = Data();
        IsAvailable = Shell_NotifyIcon(0, ref data);
        if (IsAvailable) { data.Version = 4; _version4 = Shell_NotifyIcon(4, ref data); }
    }
    private IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        // Never allow a managed exception to cross the unmanaged callback boundary.
        try
        {
            if (!_disposed && message == _taskbarCreated) { AddIcon(); return IntPtr.Zero; }
            if (!_disposed && message == CallbackMessage)
            {
                var notification = (uint)lParam.ToInt64() & 0xFFFF;
                if (_version4 && ((ulong)lParam.ToInt64() >> 16 & 0xFFFF) != IconId) return IntPtr.Zero;
                if (notification is 0x400 or 0x401 or 0x203) _open(); // select, keyboard select, double-click
                else if (notification == 0x7B || (!_version4 && notification == 0x205))
                    QueueMenu(wParam);
                return IntPtr.Zero;
            }
            if (!_disposed && message == ShowMenuMessage)
            {
                _menuQueued = false;
                ShowMenu();
                return IntPtr.Zero;
            }
            if (!_disposed && message == 0x111)
            {
                var command = (uint)wParam.ToUInt64() & 0xFFFF;
                if (command == OpenCommand) { _open(); return IntPtr.Zero; }
                if (command == ExitCommand) { _exit(); return IntPtr.Zero; }
                if (command == ToggleRecordingCommand) { if (_recordingState().Ready) _toggleRecording(); return IntPtr.Zero; }
            }
        }
        catch { /* The app remains reachable and future callbacks continue. */ }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private IntPtr CreateMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return menu;
        AppendMenu(menu, 0x3, 0, VersionLabel); // Disabled information row.
        AppendMenu(menu, 0x800, 0, null);
        AppendMenu(menu, 0, OpenCommand, "打开 ClipFlow");
        var state = _recordingState();
        AppendMenu(menu, state.Ready ? 0u : 0x3u, ToggleRecordingCommand,
            !state.Ready ? "记录不可用" : state.Recording ? "暂停记录" : "继续记录");
        AppendMenu(menu, 0, ExitCommand, "退出");
        return menu;
    }
    private void QueueMenu(UIntPtr coordinates)
    {
        if (_menuQueued || _menuOpen) return;
        var packed = coordinates.ToUInt64();
        _menuPoint = new Point { X = (short)(packed & 0xFFFF), Y = (short)(packed >> 16 & 0xFFFF) };
        if (!_version4 || (_menuPoint.X == -1 && _menuPoint.Y == -1)) GetCursorPos(out _menuPoint);
        // Finish the Shell's notification before entering the native menu message loop.
        _menuQueued = PostMessage(_window, ShowMenuMessage, UIntPtr.Zero, IntPtr.Zero);
    }
    private void ShowMenu()
    {
        if (_menuOpen || _disposed) return;
        var menu = CreateMenu();
        if (menu == IntPtr.Zero) return;
        _menuOpen = true;
#if DEBUG
        ActiveMenuForTest = menu;
        LastMenuCommandForTest = 0;
#endif
        uint command;
        try
        {
            ShowWindow(_menuWindow, 4); // SW_SHOWNOACTIVATE; no taskbar/Alt+Tab entry.
            SetForegroundWindow(_menuWindow);
            // Dispatch after the menu loop ends, so Exit cannot destroy an active menu owner.
            command = TrackPopupMenuEx(menu, 0x2 | 0x80 | 0x100, _menuPoint.X, _menuPoint.Y, _menuWindow, IntPtr.Zero);
#if DEBUG
            LastMenuCommandForTest = command;
#endif
            PostMessage(_menuWindow, 0, UIntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            ShowWindow(_menuWindow, 0);
            DestroyMenu(menu);
            _menuOpen = false;
#if DEBUG
            ActiveMenuForTest = IntPtr.Zero;
#endif
        }
        if (_disposed) return;
        if (command is OpenCommand or ExitCommand or ToggleRecordingCommand)
            PostMessage(_window, 0x111, new UIntPtr(command), IntPtr.Zero);
        else { var data = Data(); Shell_NotifyIcon(3, ref data); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = Data(); Shell_NotifyIcon(2, ref data);
        IsAvailable = false;
        if (_subclassed) { RemoveWindowSubclass(_window, _callback, 71); _subclassed = false; }
        if (_menuWindow != IntPtr.Zero) DestroyWindow(_menuWindow);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
    }
    private static IntPtr CreateClipFlowIcon()
    {
        // A small antialiased, transparent version of the two overlapping clipboard outlines.
        const int size = 32, samples = 4;
        var bytes = new byte[40 + size * size * 4 + size * 4];
        void Write(int offset, int value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        Write(0, 40); Write(4, size); Write(8, size * 2);
        bytes[12] = 1; bytes[14] = 32; Write(20, size * size * 4);
        static double Distance(double x, double y, double cx, double cy, double w, double h)
        {
            var dx = Math.Abs(x - cx) - (w / 2 - 3);
            var dy = Math.Abs(y - cy) - (h / 2 - 3);
            return Math.Sqrt(Math.Max(dx, 0) * Math.Max(dx, 0) + Math.Max(dy, 0) * Math.Max(dy, 0)) + Math.Min(Math.Max(dx, dy), 0) - 3;
        }
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var coverage = 0;
            for (var sy = 0; sy < samples; sy++) for (var sx = 0; sx < samples; sx++)
            {
                var px = x + (sx + .5) / samples; var py = y + (sy + .5) / samples;
                var front = Distance(px, py, 21, 13, 16, 22);
                var back = Distance(px, py, 12, 21, 16, 20);
                if (Math.Abs(front) < 1 || (front > 1 && Math.Abs(back) < 1)) coverage++;
            }
            var alpha = coverage * 255 / (samples * samples);
            var offset = 40 + ((size - y - 1) * size + x) * 4;
            bytes[offset] = (byte)(235 * alpha / 255);
            bytes[offset + 1] = (byte)(158 * alpha / 255);
            bytes[offset + 2] = (byte)(24 * alpha / 255);
            bytes[offset + 3] = (byte)alpha;
        }
        return CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, size, size, 0);
    }
#if DEBUG
    private IntPtr ActiveMenuForTest { get; set; }
    internal uint LastMenuCommandForTest { get; private set; }
    internal (int X, int Y) MenuItemCenterForTest(uint position)
    {
        if (ActiveMenuForTest == IntPtr.Zero || !GetMenuItemRect(IntPtr.Zero, ActiveMenuForTest, position, out var rect))
            throw new InvalidOperationException("原生菜单项尚未显示。");
        return ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
    }
    internal IntPtr MenuOwnerForTest => _menuWindow;
    internal bool MenuOpenForTest => _menuOpen;
    internal void RequestMenuForTest()
    {
        GetCursorPos(out var point);
        var packed = (uint)(ushort)point.X | ((uint)(ushort)point.Y << 16);
        PostMessage(_window, CallbackMessage, new UIntPtr(packed), new IntPtr(0x7B | (IconId << 16)));
    }
    internal void RequestLegacyRightButtonForTest() => PostMessage(_window, CallbackMessage, UIntPtr.Zero, new IntPtr(0x205 | (IconId << 16)));
    internal bool HasShellIcon()
    {
        var identifier = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = _window, Id = IconId };
        return Shell_NotifyIconGetRect(ref identifier, out _) == 0;
    }
    internal void RequestRestoreForTest() => PostMessage(_window, CallbackMessage, UIntPtr.Zero, new IntPtr(0x400 | (IconId << 16)));
    internal void RequestExitForTest() => PostMessage(_window, 0x111, new UIntPtr(ExitCommand), IntPtr.Zero);
    internal void RecreateForTest()
    {
        var data = Data(); Shell_NotifyIcon(2, ref data);
        PostMessage(_window, _taskbarCreated, UIntPtr.Zero, IntPtr.Zero);
    }
    internal string[] MenuLabelsForTest()
    {
        var menu = CreateMenu();
        try
        {
            return new[] { 0u, 2u, 3u, 4u }.Select(position =>
            {
                var text = new System.Text.StringBuilder(128);
                GetMenuString(menu, position, text, text.Capacity, 0x400);
                return text.ToString();
            }).ToArray();
        }
        finally { DestroyMenu(menu); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [DllImport("shell32")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Rect rect);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")] private static extern int GetMenuString(IntPtr menu, uint item, System.Text.StringBuilder text, int count, uint flags);
    [DllImport("user32")] private static extern bool GetMenuItemRect(IntPtr window, IntPtr menu, uint position, out Rect rect);
#endif
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("shell32", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint command, ref NotifyIconData data);
    [DllImport("comctl32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32")] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint length, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")] private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
