using ClipFlow.Core;
using ClipFlow.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace ClipFlow.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    public HistoryStore Store { get; }
    private readonly ClipboardService _clipboard;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private List<RecordViewModel> _all = [];
    public ObservableCollection<RecordViewModel> Records { get; } = [];
    public AppSettings Settings { get; private set; } = new();
    public event Action<ThemeMode>? ThemeChanged;
    public event Action<string, bool>? Notice;
    public Func<RecordViewModel, Task>? DetailRequested { get; set; }
    public Func<bool, Task<bool>>? ConfirmClear { get; set; }
    private string _page = "recent", _search = "";
    private int _typeFilter;
    private bool _ready;
    public bool IsReady
    {
        get => _ready;
        private set
        {
            Set(ref _ready, value); Raise(nameof(RetryVisibility)); Raise(nameof(RecordingLabel));
            Raise(nameof(RecordingButton)); Raise(nameof(RecordingEnabled)); Raise(nameof(RecordingSeverity)); Raise(nameof(CanClearHistory));
        }
    }
    public Visibility RetryVisibility => IsReady ? Visibility.Collapsed : Visibility.Visible;
    public string PageTitle => _page switch { "favorites" => "收藏夹", "settings" => "设置", _ => "最近记录" };
    public string PageSubtitle => _page switch
    {
        "favorites" => "留住灵感，让重要内容随手可得。",
        _ => "复制过的内容，都在这里。"
    };
    public Visibility HistoryVisibility => _page == "settings" ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SettingsVisibility => _page == "settings" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => Records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string EmptyTitle => !IsReady ? "正在准备本地记录" : Search.Length > 0 || _typeFilter != 0 ? "没有找到匹配的内容" : _page == "favorites" ? "收藏你的第一条记录" : "从一次复制开始";
    public string EmptyDescription => !IsReady ? "如果初始化失败，可点击重试。已有数据会被保留。" : Search.Length > 0 || _typeFilter != 0 ? "尝试其他关键词，或切换内容类型。" : _page == "favorites" ? "点击记录上的星标，把常用内容收藏到这里。" : "在其他应用中复制文本或图片，ClipFlow 会在运行期间为你保存。";
    public string CountLabel => $"{Records.Count} 条记录";
    public string SearchPlaceholder => _page == "favorites" ? "搜索收藏内容" : "搜索历史记录";
    public string RecordingLabel => !IsReady ? "记录不可用，请检查本地存储。" : Settings.RecordingEnabled ? "ClipFlow 正在记录" : "记录已暂停，复制的内容将不会被保存。点击「继续记录」恢复。";
    public string RecordingButton => !IsReady ? "记录不可用" : Settings.RecordingEnabled ? "暂停记录" : "继续记录";
    public InfoBarSeverity RecordingSeverity => !IsReady ? InfoBarSeverity.Error : Settings.RecordingEnabled ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
    public bool CanClearHistory => IsReady && _all.Count > 0;
    public Visibility ClearHistoryVisibility => _page == "recent" ? Visibility.Visible : Visibility.Collapsed;
    public string Search { get => _search; set { if (Set(ref _search, value)) Filter(); } }
    public int TypeFilter { get => _typeFilter; set { if (Set(ref _typeFilter, value)) Filter(); } }
    public int ThemeIndex => (int)Settings.Theme;
    public bool RecordingEnabled => IsReady && Settings.RecordingEnabled;
    public int HistoryLimit => Settings.HistoryLimit;
    public AsyncCommand ToggleRecordingCommand { get; }
    public AsyncCommand ClearHistoryCommand { get; }
    public AsyncCommand ClearDataCommand { get; }
    public AsyncCommand RetryCommand { get; }

    public MainViewModel(DispatcherQueue dispatcher, string? dataRoot = null)
    {
        Store = new HistoryStore(dataRoot ?? LocalDataPath.GetRoot());
        _clipboard = new ClipboardService(dispatcher, SaveCaptureAsync);
        _clipboard.Notice += message => Notice?.Invoke(message, false);
        ToggleRecordingCommand = new AsyncCommand(() => UpdateSettingsAsync(Settings with { RecordingEnabled = !Settings.RecordingEnabled }), ReportError);
        ClearHistoryCommand = new AsyncCommand(() => ClearAsync(false), ReportError);
        ClearDataCommand = new AsyncCommand(() => ClearAsync(true), ReportError);
        RetryCommand = new AsyncCommand(InitializeAsync, ReportError);
    }
    private async Task ExclusiveAsync(Func<Task> operation)
    {
        await _operations.WaitAsync();
        try { await operation(); } finally { _operations.Release(); }
    }
    public async Task InitializeAsync()
    {
        try
        {
            await ExclusiveAsync(async () =>
            {
                await Store.InitializeAsync();
                Settings = await Store.LoadSettingsAsync();
                await Store.SaveSettingsAsync(Settings);
                IsReady = true; SettingsChanged();
                await ReloadAsync();
                _clipboard.Start(Settings.RecordingEnabled);
            });
        }
        catch (Exception ex) { IsReady = false; _clipboard.SetEnabled(false); Filter(); ReportError(ex); }
    }
    public void Navigate(string page)
    {
        _page = page is "favorites" or "settings" ? page : "recent"; _typeFilter = 0; _search = "";
        foreach (var property in new[] { nameof(PageTitle), nameof(PageSubtitle), nameof(HistoryVisibility), nameof(SettingsVisibility), nameof(ClearHistoryVisibility), nameof(SearchPlaceholder), nameof(Search), nameof(TypeFilter) }) Raise(property);
        Filter();
    }
    private async Task SaveCaptureAsync(CapturedContent content) => await ExclusiveAsync(async () =>
    {
        if (!Settings.RecordingEnabled || !IsReady) return;
        try { await Store.SaveAsync(content, Settings.HistoryLimit); await ReloadAsync(); }
        catch (Exception ex) { ReportError(ex); }
    });
    private async Task ReloadAsync()
    {
        var records = await Store.LoadAsync();
        var existing = _all.ToDictionary(r => r.Id);
        _all = records.Select(r =>
        {
            if (existing.TryGetValue(r.Id, out var vm)) { vm.Update(r); return vm; }
            return new RecordViewModel(r, this);
        }).ToList();
        Filter();
    }
    private void Filter()
    {
        var filtered = _all.Where(r => (_page != "favorites" || r.Record.IsFavorite)
            && (_typeFilter == 0 || r.Record.Kind == (_typeFilter == 1 ? ContentKind.Text : ContentKind.Image))
            && (string.IsNullOrEmpty(Search) || r.Record.Text?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true)).ToList();
        var ids = filtered.Select(r => r.Id).ToHashSet();
        for (var i = Records.Count - 1; i >= 0; i--) if (!ids.Contains(Records[i].Id)) Records.RemoveAt(i);
        for (var i = 0; i < filtered.Count; i++)
        {
            if (i < Records.Count && Records[i].Id == filtered[i].Id) continue;
            var from = Records.IndexOf(filtered[i]);
            if (from >= 0) Records.Move(from, i); else Records.Insert(i, filtered[i]);
        }
        Raise(nameof(EmptyVisibility)); Raise(nameof(EmptyTitle)); Raise(nameof(EmptyDescription)); Raise(nameof(CountLabel));
        Raise(nameof(CanClearHistory));
    }
    public Task CopyAsync(RecordViewModel item) => ExclusiveAsync(async () =>
    {
        await _clipboard.CopyAsync(item.Record, Store);
        await Store.TouchAsync(item.Id); await ReloadAsync();
        Notice?.Invoke("已复制到剪贴板", false);
    });
    public Task FavoriteAsync(RecordViewModel item) => ExclusiveAsync(async () =>
    {
        await Store.SetFavoriteAsync(item.Id, !item.Record.IsFavorite); await ReloadAsync();
    });
    public Task DeleteAsync(RecordViewModel item) => ExclusiveAsync(async () =>
    {
        await Store.DeleteAsync(item.Id); await ReloadAsync(); Notice?.Invoke("记录已删除", false);
    });
    public Task ShowDetailAsync(RecordViewModel item) => DetailRequested?.Invoke(item) ?? Task.CompletedTask;
    public async Task UpdateSettingsAsync(AppSettings settings)
    {
        if (!IsReady) return;
        Settings = settings with { HistoryLimit = Math.Clamp(settings.HistoryLimit, 10, 10000) };
        _clipboard.SetEnabled(Settings.RecordingEnabled); SettingsChanged();
        await ExclusiveAsync(async () => { await Store.SaveSettingsAsync(Settings); await ReloadAsync(); });
    }
    private void SettingsChanged()
    {
        foreach (var name in new[] { nameof(ThemeIndex), nameof(RecordingEnabled), nameof(HistoryLimit), nameof(RecordingLabel), nameof(RecordingButton), nameof(RecordingSeverity) }) Raise(name);
        ThemeChanged?.Invoke(Settings.Theme);
    }
    private async Task ClearAsync(bool allData)
    {
        if ((!allData && !CanClearHistory) || ConfirmClear is null || !await ConfirmClear(allData)) return;
        await _clipboard.SuspendAndDrainAsync();
        try
        {
            await ExclusiveAsync(async () =>
            {
                if (IsReady) await Store.ClearAsync(allData);
                else { await Store.ResetBrokenDataAsync(); IsReady = true; _clipboard.Start(true); }
                if (allData) { Settings = new AppSettings(); _clipboard.SetEnabled(true); SettingsChanged(); }
                await ReloadAsync();
            });
            Notice?.Invoke(allData ? "本地记录、图片和设置已清空" : "历史记录和图片已清空", false);
        }
        finally { _clipboard.Resume(); }
    }
    public void ReportError(Exception ex) => Notice?.Invoke(ex is InvalidDataException ? ex.Message : "操作未完成，请重试。已有数据库会被保留；请检查本地目录的访问权限和可用空间。", true);
    public void Dispose() => _clipboard.Dispose();
    public async Task ShutdownAsync()
    {
        await _clipboard.SuspendAndDrainAsync();
        // Wait for in-flight database writes before terminating the process.
        await ExclusiveAsync(() => Task.CompletedTask);
        Dispose();
    }
}
