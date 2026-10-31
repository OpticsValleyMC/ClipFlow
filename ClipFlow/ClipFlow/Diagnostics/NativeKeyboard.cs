using System.Runtime.InteropServices;

namespace ClipFlow.Diagnostics;

internal static class NativeKeyboard
{
    internal static async Task ChordAsync(params ushort[] keys)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var owner);
        if (owner != Environment.ProcessId) throw new InvalidOperationException("测试窗口失去焦点，停止键盘输入");
        var inputs = keys.Select(key => new Input { Type = 1, Data = new Data { Keyboard = new Keyboard { Key = key } } })
            .Concat(keys.AsEnumerable().Reverse().Select(key => new Input { Type = 1, Data = new Data { Keyboard = new Keyboard { Key = key, Flags = 2 } } })).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new IOException("快捷键输入未送达");
        await Task.Delay(100);
    }
    internal static async Task PressAsync(ushort key)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var owner);
        if (owner != Environment.ProcessId) throw new InvalidOperationException("测试窗口失去焦点，停止键盘输入。");
        var inputs = new[]
        {
            new Input { Type = 1, Data = new Data { Keyboard = new Keyboard { Key = key } } },
            new Input { Type = 1, Data = new Data { Keyboard = new Keyboard { Key = key, Flags = 2 } } }
        };
        if (SendInput(2, inputs, Marshal.SizeOf<Input>()) != 2) throw new IOException("键盘输入未送达测试窗口。");
        await Task.Delay(60);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Data Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Data { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
