using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ClipFlow.Services;

internal sealed class PortableStartupRegistration(string executablePath, string runKey, string approvalKey) : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName = "ClipFlow Portable";
    private const int MaxCommandLength = 260;
    private const string CommandSuffix = "\" " + StartupService.BackgroundArgument;
    private readonly string _command = $"\"{executablePath}{CommandSuffix}";

    internal PortableStartupRegistration(string executablePath) : this(executablePath, RunKey, ApprovalKey) { }

    public StartupTaskState State
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(runKey);
            var command = run?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (command is null || command.Length <= CommandSuffix.Length + 1
                || !command.StartsWith('"') || !command.EndsWith(CommandSuffix, StringComparison.Ordinal)
                || !File.Exists(command[1..^CommandSuffix.Length])) return StartupTaskState.Disabled;

            // Keep Windows' Startup Apps decision intact. Only read known disabled states.
            using var approval = Registry.CurrentUser.OpenSubKey(approvalKey);
            if (approval?.GetValue(ValueName) is byte[] { Length: >= 4 } state
                && BitConverter.ToUInt32(state) is 3 or 7) return StartupTaskState.DisabledByUser;

            return StartupTaskState.Enabled;
        }
    }

    public Task<StartupTaskState> EnableAsync()
    {
        if (_command.Length > MaxCommandLength) throw new IOException("The portable startup command exceeds the Windows Run key limit.");
        using var run = Registry.CurrentUser.CreateSubKey(runKey);
        run.SetValue(ValueName, _command, RegistryValueKind.String);
        return Task.FromResult(State);
    }

    public void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
