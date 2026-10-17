using ClipFlow.Core;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ClipFlow;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThemeService _themes;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _feedbackTimer;
    private readonly TrayService _tray;
    private bool _exiting;
    private bool _dialogOpen;
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ClipFlow.ico"));
        var customTitleBar = WindowCompatibility.UseCustomTitleBar;
        ExtendsContentIntoTitleBar = customTitleBar;
        if (customTitleBar)
        {
            if (WindowCompatibility.UseLegacyCaptionButtons)
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
                LegacyCaptionButtons.Visibility = Visibility.Visible;
            }
            SetTitleBar(AppTitleBar);
        }
        else
        {
            AppTitleBar.Visibility = Visibility.Collapsed;
            Root.RowDefinitions[0].Height = new GridLength(0);
        }
        if (WindowCompatibility.UseLegacyCaptionButtons)
        {
            Application.Current.Resources["ContentControlThemeFontFamily"] = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI");
            Application.Current.Resources["SymbolThemeFontFamily"] = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets");
        }
        AppWindow.Changed += (_, _) => UpdateCaptionState();
        ResizeInDips(1100, 760);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var scale = WindowScale;
            presenter.PreferredMinimumWidth = (int)(760 * scale);
            presenter.PreferredMinimumHeight = (int)(560 * scale);
        }
        _themes = new ThemeService(this, Root);
        _feedbackTimer = DispatcherQueue.CreateTimer();
        _feedbackTimer.Interval = TimeSpan.FromSeconds(3); _feedbackTimer.IsRepeating = false;
        _feedbackTimer.Tick += (_, _) => Feedback.IsOpen = false;
        Feedback.Closed += (_, _) => Feedback.Visibility = Visibility.Collapsed;
#if DEBUG
        _viewModel = new MainViewModel(DispatcherQueue, Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT"));
#else
        _viewModel = new MainViewModel(DispatcherQueue);
#endif
        _viewModel.ThemeChanged += _themes.Apply;
        _viewModel.Notice += ShowNotice;
        _viewModel.ConfirmClear = ConfirmClearAsync;
        _viewModel.DetailRequested = ShowDetailAsync;
        Root.DataContext = _viewModel;
        Navigation.SelectedItem = RecentItem;
        Navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) =>
            PaneFooterText.Visibility = Navigation.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed);
        Root.Loaded += OnLoaded;
        _tray = new TrayService(WinRT.Interop.WindowNative.GetWindowHandle(this), RestoreWindow, () => _ = ExitAsync(),
            () => _viewModel.ToggleRecordingCommand.Execute(null), () => (_viewModel.IsReady, _viewModel.RecordingEnabled));
        AppWindow.Closing += (_, args) =>
        {
            if (!_exiting && _tray.IsAvailable) { args.Cancel = true; AppWindow.Hide(); }
        };
        Closed += (_, _) => { _tray.Dispose(); _feedbackTimer.Stop(); _viewModel.Dispose(); };
        Activated += (_, args) => AppTitleBar.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? .7 : 1;
    }
    private void CaptionMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
    }
    private void CaptionMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        else presenter.Maximize();
    }
    private void CaptionClose_Click(object sender, RoutedEventArgs e) =>
        PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(this), 0x10, UIntPtr.Zero, IntPtr.Zero);
    private void UpdateCaptionState()
    {
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        CaptionMaximizeGlyph.Glyph = maximized ? "\uE923" : "\uE922";
        var label = maximized ? "还原窗口" : "最大化窗口";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CaptionMaximize, label);
        ToolTipService.SetToolTip(CaptionMaximize, maximized ? "还原" : "最大化");
    }
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnLoaded;
        PaneFooterText.Visibility = Navigation.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        await _viewModel.InitializeAsync();
        if (!_tray.IsAvailable) ShowNotice("系统托盘暂不可用，关闭窗口将退出应用。", true);
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") == "chrome-check" && Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null)
        {
            try
            {
                RestoreWindow(); await Task.Delay(500);
                await Diagnostics.BackdropScenario.RunAsync(this, _themes, Root, _viewModel.Store.RootPath);
            }
            finally { await ExitAsync(); }
        }
        else if (Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") == "1" && Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null)
        {
            await Diagnostics.IntegrationScenario.RunAsync(_viewModel, Root, NavigateForTest,
                ResizeInDips, CreateDetailDialog,
                () => Diagnostics.BackdropScenario.RunAsync(this, _themes, Root, _viewModel.Store.RootPath), RestoreWindow);
            await ExitAsync();
        }
        else if (Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") == "restart-check" && Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null)
        {
            await Diagnostics.IntegrationScenario.CheckRestartAsync(_viewModel, Root, NavigateForTest);
            await ExitAsync();
        }
        else if (Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") == "tray-check" && Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null)
        {
            await Diagnostics.TrayScenario.RunAsync(_viewModel, this, _tray);
            _tray.RequestExitForTest();
        }
        else if (Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") == "pan-check" && Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null)
        {
            await Diagnostics.PanScenario.RunAsync(_viewModel, CreateDetailDialog);
            await ExitAsync();
        }
#endif
    }
    internal void RestoreWindow()
    {
        if (_exiting) return;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }
    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try { await _viewModel.ShutdownAsync(); }
        catch (Exception ex) { _viewModel.ReportError(ex); }
        finally
        {
            _tray.Dispose();
            Close();
            Application.Current.Exit();
        }
    }
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr hwnd);
#if DEBUG
    private void NavigateForTest(string page)
    {
        Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>().First(item => item.Tag as string == page);
        _viewModel.Navigate(page);
    }
#endif
    private double WindowScale => Math.Max(1d, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d);
    private void ResizeInDips(int width, int height) => AppWindow.Resize(new SizeInt32((int)(width * WindowScale), (int)(height * WindowScale)));
    [System.Runtime.InteropServices.DllImport("user32")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag) _viewModel?.Navigate(tag);
    }
    private void ShowNotice(string message, bool error)
    {
        Feedback.Message = message;
        Feedback.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        Feedback.Visibility = Visibility.Visible;
        Feedback.IsOpen = true;
        _feedbackTimer.Stop();
        if (!error) _feedbackTimer.Start();
    }
    private async Task<bool> ConfirmClearAsync(bool allData)
    {
        if (_dialogOpen) return false;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
                Title = allData ? "清空全部本地数据？" : "清空历史记录？",
                Content = allData ? "所有记录、收藏和图片将被删除，设置将恢复默认。这项操作无法撤销。" : "所有记录、收藏和图片将被删除。这项操作无法撤销。",
                PrimaryButtonText = "清空", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { _dialogOpen = false; }
    }
    private async Task ShowDetailAsync(RecordViewModel item)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = CreateDetailDialog(item);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await _viewModel.CopyAsync(item);
        }
        finally { _dialogOpen = false; }
    }
    private ContentDialog CreateDetailDialog(RecordViewModel item)
    {
        FrameworkElement content = item.Record.Kind == ContentKind.Text
            ? new Views.TextDetailView(item.Record.Text ?? "")
            : new Views.ImageDetailView(_viewModel.Store.ResolveFile(item.Record.ImagePath ?? ""));
        content.Width = Math.Max(240, Math.Min(800, Root.ActualWidth - 112));
        content.Height = Math.Clamp(Root.ActualHeight - 230, 180, 480);
        if (item.Record.Kind == ContentKind.Image && item.Record.Width > 0 && item.Record.Height > 0)
        {
            var availableHeight = content.Height - 44;
            var ratio = (double)item.Record.Width / item.Record.Height;
            var fitWidth = Math.Min(content.Width, availableHeight * ratio);
            content.Width = Math.Min(content.Width, Math.Max(360, fitWidth));
            content.Height = Math.Min(content.Height, fitWidth / ratio + 44);
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
            Title = item.Record.Kind == ContentKind.Text ? "完整文本" : $"图片预览 · {item.Dimensions}",
            Content = content, CloseButtonText = "关闭", PrimaryButtonText = "复制", DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Max(320, Math.Min(900, Root.ActualWidth - 64));
        return dialog;
    }
}
