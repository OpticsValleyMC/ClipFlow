using static ClipFlow.Core.LocalizedStrings;
using System.Runtime.InteropServices;
using ClipFlow.Core;
using ClipFlow.ViewModels;
using ClipFlow.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace ClipFlow.Views;

public sealed partial class QuickHistoryWindow : Window
{
    private readonly MainViewModel _owner;
    private readonly PasteTarget _pasteTarget;
    private List<ClipboardRecord> _records = [];
    private bool _open, _ready, _copying, _changing, _shutdown;
    private int _generation;
    private readonly ListWheelScroll _wheelScroll;
    internal bool IsOpen => _open && _ready;
    internal IntPtr NativeHandle => WinRT.Interop.WindowNative.GetWindowHandle(this);
    internal QuickHistoryWindow(MainViewModel owner, PasteTarget pasteTarget)
    {
        InitializeComponent(); _owner = owner; _pasteTarget = pasteTarget;
        UiLanguage.BindLanguage(Root);
        _wheelScroll = new ListWheelScroll(RecordsList, distancePerNotch: 88);
        AppWindow.SetPresenter(OverlappedPresenter.CreateForContextMenu());
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, true); presenter.IsAlwaysOnTop = true;
        }
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            SetTitleBar(DragRegion);
        }
        else
        {
            if (AppWindow.Presenter is OverlappedPresenter fallback) fallback.SetBorderAndTitleBar(false, false);
            DragRegion.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(DragRegion).Properties.IsLeftButtonPressed)
                { ReleaseCapture(); SendMessage(NativeHandle, 0xA1, new UIntPtr(2), IntPtr.Zero); args.Handled = true; }
            };
        }
        if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
            Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        Activated += (_, args) => { if (_open && !_copying && args.WindowActivationState == WindowActivationState.Deactivated) Dismiss(); };
        AppWindow.Closing += (_, args) => { if (!_shutdown) { args.Cancel = true; Dismiss(); } };
        // TextBox consumes Escape to undo text edits. Closing the floating
        // history must also work while its search field owns keyboard focus.
        Root.PreviewKeyDown += (_, args) =>
        {
            if (_open && args.Key == VirtualKey.Escape) { args.Handled = true; Dismiss(); }
        };
        Root.KeyDown += async (_, args) =>
        {
            if (args.Key == VirtualKey.Escape) { args.Handled = true; Dismiss(); }
            else if (args.Key == VirtualKey.Down && SearchBox.FocusState != FocusState.Unfocused && RecordsList.Items.Count > 0)
            { args.Handled = true; RecordsList.SelectedIndex = 0; RecordsList.Focus(FocusState.Keyboard); }
            else if (args.Key == VirtualKey.Enter && CanPasteFromKeyboard())
            {
                args.Handled = true;
                if (RecordsList.SelectedItem is RecordViewModel selected) await CopyAsync(selected);
                else if (RecordsList.Items.FirstOrDefault() is RecordViewModel first) await CopyAsync(first);
            }
        };
    }
    internal async Task ToggleAsync()
    {
        if (_open) { Dismiss(); return; }
        var generation = ++_generation;
        Root.RequestedTheme = _owner.Settings.Theme switch { ThemeMode.Light => ElementTheme.Light, ThemeMode.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        ToolTipService.SetToolTip(DragRegion, L("DragPanelShortcutFormat", _owner.ShortcutLabel));
        SearchBox.Text = ""; FavoritesOnly.IsChecked = false;
        _records.Clear(); Filter(); SetStatus("LoadingHistory");
        GetCursorPos(out var cursor);
        var work = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(cursor.X, cursor.Y));
        var scale = GetDpiForWindow(NativeHandle) / 96d;
        var width = Math.Min(work.Width, (int)(360 * scale));
        var height = Math.Min(work.Height, (int)(400 * scale));
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(cursor.X, work.X, work.X + work.Width - width), Math.Clamp(cursor.Y, work.Y, work.Y + work.Height - height), width, height));
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
        Root.Loaded += OnLoaded;
        _open = true; AppWindow.Show(); Activate(); SetForegroundWindow(NativeHandle);
        try
        {
            if (!Root.IsLoaded) await loaded.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (!_open || generation != _generation) return;
            SearchBox.Focus(FocusState.Programmatic); _ready = true;
            var records = await _owner.Store.LoadAsync();
            if (!_open || generation != _generation) return;
            _records = records.ToList(); Filter(); SetStatus("PastePanelHint");
        }
        catch { if (_open && generation == _generation) SetStatus("HistoryLoadFailed"); }
        finally { Root.Loaded -= OnLoaded; }
    }
    private void Filter()
    {
        if (RecordsList is null) return;
        _wheelScroll?.Reset();
        var query = SearchBox.Text;
        var records = _records.Where(record => (FavoritesOnly.IsChecked != true || record.IsFavorite)
            && (query.Length == 0 || record.Text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true));
        RecordsList.ItemsSource = records.Select(record => new RecordViewModel(record, _owner, () => SearchBox.Text)).ToList();
        UiLanguage.Bind(EmptyLabel, TextBlock.TextProperty, query.Length > 0 ? "NoMatches" : FavoritesOnly.IsChecked == true ? "NoFavorites" : "NoClipboardItems");
        EmptyText.Visibility = RecordsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task CopyAsync(RecordViewModel record)
    {
        if (_copying || _changing) return;
        _copying = true;
        var generation = _generation;
        try
        {
            await _owner.CopyAsync(record);
            if (!_open || generation != _generation) return;
            if (await _pasteTarget.PasteAsync(NativeHandle, () => _open && generation == _generation)) Dismiss();
            else if (_open && (GetForegroundWindow() == NativeHandle || _pasteTarget.IsForeground))
            {
                SetStatus("AutoPasteFailed");
                Activate(); SetForegroundWindow(NativeHandle);
            }
            else Dismiss();
        }
        catch { if (_open) SetStatus("CopyFailed"); }
        finally { _copying = false; }
    }
    private bool CanPasteFromKeyboard()
    {
        var element = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        while (element is not null)
        {
            if (element is Button) return false; // Buttons handle their own Enter action.
            if (element is TextBox) return ReferenceEquals(element, SearchBox);
            if (element is ListViewItem or ListView) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }
    private async Task ChangeRecordAsync(RecordViewModel item, bool delete)
    {
        if (_copying || _changing) return;
        _changing = true;
        var generation = _generation;
        try
        {
            if (delete) await _owner.DeleteAsync(item); else await _owner.FavoriteAsync(item);
            var records = await _owner.Store.LoadAsync();
            if (!_open || generation != _generation) return;
            _records = records.ToList(); Filter();
            SetStatus(delete ? "Deleted" : item.Record.IsFavorite ? "Unfavorited" : "Favorited");
        }
        catch { if (_open) SetStatus("ActionFailed"); }
        finally { _changing = false; }
    }
    private void SetStatus(string key) => UiLanguage.Bind(StatusText, TextBlock.TextProperty, key);

    internal void RefreshLanguage()
    {
        ToolTipService.SetToolTip(DragRegion, L("DragPanelShortcutFormat", _owner.ShortcutLabel));
        foreach (var item in RecordsList.Items.OfType<RecordViewModel>()) item.RefreshLanguage();
    }
    private static RecordViewModel? Item(object sender) => (sender as FrameworkElement)?.Tag as RecordViewModel;
    private async void Favorite_Click(object sender, RoutedEventArgs args) { if (Item(sender) is { } item) await ChangeRecordAsync(item, false); }
    private async void Delete_Click(object sender, RoutedEventArgs args) { if (Item(sender) is { } item) await ChangeRecordAsync(item, true); }
    private async void Paste_Click(object sender, RoutedEventArgs args) { if (Item(sender) is { } item) await CopyAsync(item); }
    private void ClipboardTab_Click(object sender, RoutedEventArgs args) => FavoritesOnly.IsChecked = false;
    private void Search_Changed(object sender, TextChangedEventArgs args) => Filter();
    private void Favorites_Changed(object sender, RoutedEventArgs args)
    {
        if (AllIndicator is not null) AllIndicator.Visibility = FavoritesOnly.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        if (FavoriteIndicator is not null) FavoriteIndicator.Visibility = FavoritesOnly.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Filter();
    }
    private void Close_Click(object sender, RoutedEventArgs args) => Dismiss();
    internal void Dismiss() { _open = false; _ready = false; _generation++; AppWindow.Hide(); }
    internal void Shutdown() { _shutdown = true; Dismiss(); Close(); }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] private static extern bool ReleaseCapture();
    [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
}
