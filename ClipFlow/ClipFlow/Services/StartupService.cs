using static ClipFlow.Core.LocalizedStrings;
using System.Runtime.InteropServices;
using System.Security;
using Windows.ApplicationModel;

namespace ClipFlow.Services;

public sealed record StartupSnapshot(bool Enabled, bool CanChange, string DescriptionKey)
{
    public string Description => L(DescriptionKey);
}

internal interface IStartupRegistration
{
    StartupTaskState State { get; }
    Task<StartupTaskState> EnableAsync();
    void Disable();
}

// Windows owns this setting. Do not mirror it in SQLite or override Task Manager decisions.
public sealed class StartupService
{
    internal const string TaskId = "ClipFlowStartup";
    internal const string BackgroundArgument = "--startup";
    private readonly Func<Task<IStartupRegistration?>> _registration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public StartupService() : this(GetNativeAsync) { }
    internal StartupService(Func<Task<IStartupRegistration?>> registration) => _registration = registration;

    public Task<StartupSnapshot> RefreshAsync() => RunAsync(null);
    public Task<StartupSnapshot> SetEnabledAsync(bool enabled) => RunAsync(enabled);
    private async Task<StartupSnapshot> RunAsync(bool? enabled)
    {
        await _gate.WaitAsync();
        try
        {
            var task = await _registration();
            if (task is null) return new(false, false, "StartupUnavailable");
            var state = task.State;
            if (enabled == true && state == StartupTaskState.Disabled) state = await task.EnableAsync();
            else if (enabled == false && state == StartupTaskState.Enabled) { task.Disable(); state = task.State; }
            return state switch
            {
                StartupTaskState.Enabled => new(true, true, "StartupEnabledHelp"),
                StartupTaskState.Disabled => new(false, true, "StartupDisabledHelp"),
                StartupTaskState.DisabledByUser => new(false, false, "StartupDisabledByUser"),
                StartupTaskState.DisabledByPolicy => new(false, false, "StartupDisabledByPolicy"),
                StartupTaskState.EnabledByPolicy => new(true, false, "StartupEnabledByPolicy"),
                _ => new(false, false, "StartupUnavailable")
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or SecurityException or IOException)
        { return new(false, false, "StartupReadFailed"); }
        finally { _gate.Release(); }
    }
    private static async Task<IStartupRegistration?> GetNativeAsync()
    {
        var packaged = LocalDataPath.IsPackaged();
#if DEBUG
        // Only the isolated packaged acceptance fixture exercises native startup registration.
        if (Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null
            && (!packaged || Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") != "startup-check")) return null;
#endif
        if (packaged) return new NativeRegistration(await StartupTask.GetAsync(TaskId));
        return Environment.ProcessPath is { } path ? new PortableStartupRegistration(path) : null;
    }
    private sealed class NativeRegistration(StartupTask task) : IStartupRegistration
    {
        public StartupTaskState State => task.State;
        public async Task<StartupTaskState> EnableAsync() => await task.RequestEnableAsync();
        public void Disable() => task.Disable();
    }
}
