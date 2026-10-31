using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace ClipFlow.Services;

public sealed record StartupSnapshot(bool Enabled, bool CanChange, string Description);

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
            if (task is null) return new(false, false, "开机自启动需要Microsoft Store版本软件支持");
            var state = task.State;
            if (enabled == true && state == StartupTaskState.Disabled) state = await task.EnableAsync();
            else if (enabled == false && state == StartupTaskState.Enabled) { task.Disable(); state = task.State; }
            return state switch
            {
                StartupTaskState.Enabled => new(true, true, "登录 Windows 后在托盘启动，可在任务管理器的“启动应用”中管理"),
                StartupTaskState.Disabled => new(false, true, "登录 Windows 后在托盘启动，不打扰当前工作"),
                StartupTaskState.DisabledByUser => new(false, false, "已在 Windows 中禁用，请到任务管理器的“启动应用”中重新启用 ClipFlow"),
                StartupTaskState.DisabledByPolicy => new(false, false, "开机启动已被系统策略禁用"),
                StartupTaskState.EnabledByPolicy => new(true, false, "开机启动由系统策略启用，无法在应用中更改"),
                _ => new(false, false, "当前 Windows 环境不支持更改开机启动")
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        { return new(false, false, "暂时无法读取开机启动状态，请在 Windows 设置的“应用 → 启动”中检查"); }
        finally { _gate.Release(); }
    }
    private static async Task<IStartupRegistration?> GetNativeAsync()
    {
        try { _ = Package.Current.Id; }
        catch (InvalidOperationException) { return null; }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80073D54)) { return null; }
#if DEBUG
        // Unpackaged fixtures never change the real user's startup preference.
        if (Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null
            && Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") != "startup-check") return null;
#endif
        return new NativeRegistration(await StartupTask.GetAsync(TaskId));
    }
    private sealed class NativeRegistration(StartupTask task) : IStartupRegistration
    {
        public StartupTaskState State => task.State;
        public async Task<StartupTaskState> EnableAsync() => await task.RequestEnableAsync();
        public void Disable() => task.Disable();
    }
}
