using static ClipFlow.Core.LocalizedStrings;
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
    public event Action<AppSettings>? AppearanceChanged;
    public Func<uint, uint, bool>? RegisterShortcut { get; set; }
    public string ShortcutLabel => HotkeyService.Label(Settings.HotkeyModifiers, Settings.HotkeyKey);
    private string? _shortcutStatusKey;
    public string ShortcutStatus => _shortcutStatusKey is { } key ? L(key) : "";
    internal void SetShortcutStatus(string? key) { _shortcutStatusKey = key; Raise(nameof(ShortcutStatus)); }
    internal void SetShortcutError(Exception error) => SetShortcutStatus(error is ShortcutException rejected ? rejected.ResourceKey : "OperationFailedStorage");

    private sealed class ShortcutException(string resourceKey) : InvalidOperationException(L(resourceKey))
    {
        internal string ResourceKey { get; } = resourceKey;
    }
    public async Task ChangeShortcutAsync(uint modifiers, uint key)
    {
        if (modifiers == 2 && key == 0x56) throw new ShortcutException("StandardPasteShortcut");
        if (RegisterShortcut?.Invoke(modifiers, key) != true) throw new ShortcutException("ShortcutUnavailable");
        var previous = Settings;
        try { await UpdateSettingsAsync(Settings with { HotkeyModifiers = modifiers, HotkeyKey = key }); }
        catch { RegisterShortcut(previous.HotkeyModifiers, previous.HotkeyKey); Settings = previous; SettingsChanged(); throw; }
    }
    public StartupService Startup { get; } = new();
    private StartupSnapshot _startup = new(false, false, "ReadingStartup");
    private int _startupOperations;
    private Task? _startupRefresh;
    public bool StartupEnabled
    {
        get => _startup.Enabled;
        set { if (value != _startup.Enabled && CanChangeStartup) _ = ApplyStartupFromBindingAsync(value); }
    }
    public bool CanChangeStartup => _startupOperations == 0 && _startup.CanChange;
    public string StartupDescription => _startup.Description;
    internal StartupSnapshot StartupStatus => _startup;
    internal event Action? LanguageChanged;
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
            Raise(nameof(CanChangeLanguage));
        }
    }
    public Visibility RetryVisibility => IsReady ? Visibility.Collapsed : Visibility.Visible;
    public string PageTitle => _page switch { "favorites" => L("Favorites"), "settings" => L("Settings"), _ => L("Recent") };
    public string PageSubtitle => _page switch
    {
        "favorites" => L("FavoritesSubtitle"),
        _ => L("RecentSubtitle")
    };
    public Visibility HistoryVisibility => _page == "settings" ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SettingsVisibility => _page == "settings" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => Records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string EmptyTitle => !IsReady ? L("PreparingHistory") : Search.Length > 0 || _typeFilter != 0 ? L("NoMatches") : _page == "favorites" ? L("FirstFavorite") : L("StartByCopying");
    public string EmptyDescription => !IsReady ? L("RetryHelp") : Search.Length > 0 || _typeFilter != 0 ? L("SearchHelp") : _page == "favorites" ? L("FavoriteHelp") : L("FirstCopyHelp");
    public string CountLabel => L("CountFormat", Records.Count);
    public string AboutVersion => AppInfo.AboutVersion;
    public string AboutAuthor => AppInfo.AboutAuthor;
    public string SearchPlaceholder => _page == "favorites" ? L("SearchFavorites") : L("SearchHistory");
    public string RecordingLabel => !IsReady ? L("RecordingStorageUnavailable") : Settings.RecordingEnabled ? L("RecordingActive") : L("RecordingPausedHelp");
    public string RecordingButton => !IsReady ? L("RecordingUnavailable") : Settings.RecordingEnabled ? L("PauseRecording") : L("ResumeRecording");
    public InfoBarSeverity RecordingSeverity => !IsReady ? InfoBarSeverity.Error : Settings.RecordingEnabled ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
    public bool CanClearHistory => IsReady && _all.Count > 0;
    public Visibility ClearHistoryVisibility => _page == "recent" ? Visibility.Visible : Visibility.Collapsed;
    public string Search { get => _search; set { if (Set(ref _search, value)) Filter(); } }
    public int TypeFilter { get => _typeFilter; set { if (Set(ref _typeFilter, value)) Filter(); } }
    public int ThemeIndex { get => (int)Settings.Theme; set { if (value is >= 0 and <= 2 && value != ThemeIndex) _ = ApplySettingsFromBindingAsync(Settings with { Theme = (ThemeMode)value }); } }
    public int BackdropIndex { get => (int)Settings.Backdrop; set { if (value is >= 0 and <= 3 && value != BackdropIndex) _ = ApplySettingsFromBindingAsync(Settings with { Backdrop = (BackdropLevel)value }); } }
    public int FavoriteSortIndex { get => (int)Settings.FavoriteSort; set { if (value is >= 0 and <= 2 && value != FavoriteSortIndex) _ = ChangeSortAsync((FavoriteSortMode)value); } }
    private bool _changingLanguage;
    public int LanguageIndex => (int)Settings.Language;
    public bool CanChangeLanguage => IsReady && !_changingLanguage;
    public Visibility FavoritesVisibility => _page == "favorites" ? Visibility.Visible : Visibility.Collapsed;
    public bool IsFavoritesPage => _page == "favorites";
    public bool RecordingEnabled { get => IsReady && Settings.RecordingEnabled; set { if (IsReady && value != Settings.RecordingEnabled) _ = ApplySettingsFromBindingAsync(Settings with { RecordingEnabled = value }); } }
    public int HistoryLimit => Settings.HistoryLimit;
    public double HistoryLimitValue { get => Settings.HistoryLimit; set { if (double.IsFinite(value) && value != Settings.HistoryLimit) _ = ApplySettingsFromBindingAsync(Settings with { HistoryLimit = (int)Math.Clamp(value, 10, 10000) }); } }
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
        foreach (var property in new[] { nameof(PageTitle), nameof(PageSubtitle), nameof(HistoryVisibility), nameof(SettingsVisibility), nameof(ClearHistoryVisibility), nameof(SearchPlaceholder), nameof(Search), nameof(TypeFilter), nameof(FavoritesVisibility) }) Raise(property);
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
        foreach (var item in _all) item.RefreshPage();
        var filtered = _all.Where(r => (_page != "favorites" || r.Record.IsFavorite)
            && (_typeFilter == 0 || r.Record.Kind == (_typeFilter == 1 ? ContentKind.Text : ContentKind.Image))
            && (string.IsNullOrEmpty(Search) || r.Record.Text?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true)).ToList();
        if (IsFavoritesPage)
        {
            var byId = filtered.ToDictionary(r => r.Id);
            filtered = FavoriteOrdering.Apply(filtered.Select(r => r.Record), Settings.FavoriteSort).Select(r => byId[r.Id]).ToList();
        }
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
        _ = item.MarkCopiedAsync();
        await Store.TouchAsync(item.Id); await ReloadAsync();
    });
    public Task FavoriteAsync(RecordViewModel item) => ExclusiveAsync(async () =>
    {
        await Store.SetFavoriteAsync(item.Id, !item.Record.IsFavorite); await ReloadAsync();
    });
    public Task PinAsync(RecordViewModel item) => ExclusiveAsync(async () =>
    {
        await Store.SetPinnedAsync(item.Id, !item.Record.IsPinned); await ReloadAsync();
    });
    private async Task ChangeSortAsync(FavoriteSortMode sort)
    {
        try { await UpdateSettingsAsync(Settings with { FavoriteSort = sort }); }
        catch (Exception ex) { ReportError(ex); }
    }
    public Task RefreshStartupAsync() => _startupRefresh is { IsCompleted: false } pending ? pending : _startupRefresh = RefreshStartupCoreAsync();
    private async Task RefreshStartupCoreAsync()
    {
        _startupOperations++; Raise(nameof(CanChangeStartup));
        try { _startup = await Startup.RefreshAsync(); }
        finally { _startupOperations--; RaiseStartup(); }
    }
    public async Task SetStartupAsync(bool enabled)
    {
        if (!CanChangeStartup) return;
        _startupOperations++; Raise(nameof(CanChangeStartup));
        try { _startup = await Startup.SetEnabledAsync(enabled); }
        finally { _startupOperations--; RaiseStartup(); }
    }
    private void RaiseStartup()
    { Raise(nameof(StartupEnabled)); Raise(nameof(CanChangeStartup)); Raise(nameof(StartupDescription)); }
    private async Task ApplyStartupFromBindingAsync(bool enabled)
    {
        try { await SetStartupAsync(enabled); } catch (Exception ex) { ReportError(ex); }
    }
    private async Task ApplySettingsFromBindingAsync(AppSettings settings)
    {
        try { await UpdateSettingsAsync(settings); } catch (Exception ex) { ReportError(ex); }
    }
    public Task DeleteAsync(RecordViewModel item) => ExclusiveAsync(async () =>
    {
        await Store.DeleteAsync(item.Id); await ReloadAsync(); Notice?.Invoke(L("ItemDeleted"), false);
    });
    public Task ShowDetailAsync(RecordViewModel item) => DetailRequested?.Invoke(item) ?? Task.CompletedTask;
    public async Task ChangeLanguageAsync(AppLanguage language)
    {
        if (!CanChangeLanguage || language == Settings.Language) return;
        _changingLanguage = true;
        Raise(nameof(CanChangeLanguage));
        try
        {
            await ExclusiveAsync(async () =>
            {
                if (!IsReady) return;
                await Store.SaveLanguageAsync(language);
                Settings = Settings with { Language = language };
                ApplyLanguage();
                Raise(nameof(LanguageIndex));
            });
        }
        finally { _changingLanguage = false; Raise(nameof(CanChangeLanguage)); }
    }

    private void ApplyLanguage()
    {
        UiLanguage.Apply(Settings.Language);
        foreach (var record in _all) record.RefreshLanguage();
        foreach (var property in new[] { nameof(PageTitle), nameof(PageSubtitle), nameof(EmptyTitle), nameof(EmptyDescription),
            nameof(CountLabel), nameof(SearchPlaceholder), nameof(RecordingLabel), nameof(RecordingButton),
            nameof(StartupDescription), nameof(AboutVersion), nameof(AboutAuthor), nameof(ShortcutStatus) }) Raise(property);
        LanguageChanged?.Invoke();
    }

    public async Task UpdateSettingsAsync(AppSettings settings)
    {
        if (!IsReady) return;
        Settings = settings with { Language = Settings.Language, HistoryLimit = Math.Clamp(settings.HistoryLimit, 10, 10000) };
        _clipboard.SetEnabled(Settings.RecordingEnabled); SettingsChanged();
        await ExclusiveAsync(async () => { await Store.SaveSettingsAsync(Settings); await ReloadAsync(); });
    }
    private void SettingsChanged()
    {
        Raise(nameof(ShortcutLabel));
        foreach (var name in new[] { nameof(ThemeIndex), nameof(BackdropIndex), nameof(FavoriteSortIndex), nameof(LanguageIndex), nameof(RecordingEnabled), nameof(HistoryLimit), nameof(HistoryLimitValue), nameof(RecordingLabel), nameof(RecordingButton), nameof(RecordingSeverity) }) Raise(name);
        ThemeChanged?.Invoke(Settings.Theme);
        AppearanceChanged?.Invoke(Settings);
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
                if (allData) { Settings = new AppSettings(); _clipboard.SetEnabled(true); ApplyLanguage(); SettingsChanged(); }
                await ReloadAsync();
            });
            Notice?.Invoke(allData ? L("AllDataCleared") : L("HistoryCleared"), false);
        }
        finally { _clipboard.Resume(); }
    }
    public void ReportError(Exception ex) => Notice?.Invoke(ex is InvalidDataException ? ex.Message : L("OperationFailedStorage"), true);
    public void Dispose() => _clipboard.Dispose();
    public async Task ShutdownAsync()
    {
        await _clipboard.SuspendAndDrainAsync();
        // Wait for in-flight database writes before terminating the process.
        await ExclusiveAsync(() => Task.CompletedTask);
        Dispose();
    }
}
