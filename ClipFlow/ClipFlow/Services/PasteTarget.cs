using System.Runtime.InteropServices;

namespace ClipFlow.Services;

// Snapshot before activating the picker. Never paste into whichever unrelated
// window happens to be foreground after an asynchronous clipboard operation.
internal sealed class PasteTarget
{
    private readonly IntPtr _window, _focus;
    private readonly uint _process;
    private PasteTarget(IntPtr window, IntPtr focus, uint process) { _window = window; _focus = focus; _process = process; }
    internal bool IsForeground => GetForegroundWindow() == _window;
    internal static PasteTarget Capture()
    {
        var window = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(window, out var process);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(thread, ref info);
        return new(window, info.Focus, process);
    }
    internal async Task<bool> PasteAsync(IntPtr picker, Func<bool> requested)
    {
        for (var i = 0; i < 40 && ModifiersHeld(); i++)
        {
            if (!requested() || GetForegroundWindow() != picker) return false;
            await Task.Delay(25);
        }
        if (!requested() || ModifiersHeld() || GetForegroundWindow() != picker || !IsWindow(_window) || _focus == IntPtr.Zero || !IsWindow(_focus)) return false;
        var thread = GetWindowThreadProcessId(_window, out var process);
        if (process != _process || !SetForegroundWindow(_window)) return false;
        await Task.Delay(50);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!requested() || GetForegroundWindow() != _window || !GetGUIThreadInfo(thread, ref info) || info.Focus != _focus || ModifiersHeld()) return false;
        var inputs = new[] { Key(0x11), Key(0x56), Key(0x56, 2), Key(0x11, 2) };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            // Release only keys introduced by our incomplete input batch.
            if (sent > 0) SendInput(2, [Key(0x56, 2), Key(0x11, 2)], Marshal.SizeOf<Input>());
            return false;
        }
        return true;
    }
    private static bool ModifiersHeld() => new[] { 0x11, 0x12, 0x10, 0x5B, 0x5C, 0x0D }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
    private static Input Key(ushort key, uint flags = 0) => new() { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = flags } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo { public uint Size, Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public int Left, Top, Right, Bottom; }
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
