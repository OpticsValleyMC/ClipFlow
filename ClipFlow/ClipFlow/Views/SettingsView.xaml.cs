using ClipFlow.Core;
using ClipFlow.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace ClipFlow.Views;
public sealed partial class SettingsView : UserControl
{
    private MainViewModel? _shortcutOwner;
    public SettingsView()
    {
        InitializeComponent();
        AboutVersion.Text = AppInfo.AboutVersion; AboutAuthor.Text = AppInfo.AboutAuthor;
        foreach (var key in Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x70, 11)))
            ShortcutKey.Items.Add(new ComboBoxItem { Content = key >= 0x70 ? $"F{key - 0x6F}" : ((char)key).ToString(), Tag = (uint)key });
        Loaded += (_, _) => SubscribeShortcut();
        DataContextChanged += (_, _) => SubscribeShortcut();
        Unloaded += (_, _) => { if (_shortcutOwner is not null) _shortcutOwner.PropertyChanged -= Shortcut_Changed; _shortcutOwner = null; };
    }
    private void SubscribeShortcut()
    {
        if (_shortcutOwner is not null) _shortcutOwner.PropertyChanged -= Shortcut_Changed;
        _shortcutOwner = DataContext as MainViewModel;
        if (_shortcutOwner is not null) _shortcutOwner.PropertyChanged += Shortcut_Changed;
        RefreshShortcut();
    }
    private void Shortcut_Changed(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(MainViewModel.ShortcutLabel)) RefreshShortcut(); }
    private void RefreshShortcut()
    {
        if (DataContext is not MainViewModel vm) return;
        ShortcutModifiers.SelectedItem = ShortcutModifiers.Items.OfType<ComboBoxItem>().FirstOrDefault(item => uint.Parse((string)item.Tag) == vm.Settings.HotkeyModifiers);
        ShortcutKey.SelectedItem = ShortcutKey.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (uint)item.Tag == vm.Settings.HotkeyKey);
    }
    private async void Shortcut_Save(object sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel vm || !vm.IsReady || ShortcutModifiers.SelectedItem is not ComboBoxItem modifiers || ShortcutKey.SelectedItem is not ComboBoxItem key) return;
        try { await vm.ChangeShortcutAsync(uint.Parse((string)modifiers.Tag), (uint)key.Tag); vm.SetShortcutStatus("快捷键已更新"); }
        catch (Exception ex) { vm.SetShortcutStatus(ex.Message); }
        RefreshShortcut();
    }
    private async void Shortcut_Reset(object sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel vm || !vm.IsReady) return;
        try { await vm.ChangeShortcutAsync(1, 0x56); vm.SetShortcutStatus("已恢复 Alt + V"); }
        catch (Exception ex) { vm.SetShortcutStatus(ex.Message); }
        RefreshShortcut();
    }
    private async void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsReady || sender is not ComboBox box || box.SelectedIndex < 0 || box.SelectedIndex == vm.ThemeIndex) return;
        try { await vm.UpdateSettingsAsync(vm.Settings with { Theme = (ThemeMode)box.SelectedIndex }); } catch (Exception ex) { vm.ReportError(ex); }
    }
    private async void Recording_Toggled(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsReady || sender is not ToggleSwitch toggle || toggle.IsOn == vm.RecordingEnabled) return;
        try { await vm.UpdateSettingsAsync(vm.Settings with { RecordingEnabled = toggle.IsOn }); } catch (Exception ex) { vm.ReportError(ex); }
    }
    private async void Backdrop_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsReady || sender is not ComboBox box || box.SelectedIndex < 0 || box.SelectedIndex == vm.BackdropIndex) return;
        try { await vm.UpdateSettingsAsync(vm.Settings with { Backdrop = (BackdropLevel)box.SelectedIndex }); } catch (Exception ex) { vm.ReportError(ex); }
    }
    private async void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanChangeStartup || sender is not ToggleSwitch toggle || toggle.IsOn == vm.StartupEnabled) return;
        try { await vm.SetStartupAsync(toggle.IsOn); } catch (Exception ex) { vm.ReportError(ex); }
    }
    private async void Limit_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsReady || double.IsNaN(e.NewValue) || (int)e.NewValue == vm.HistoryLimit) return;
        try { await vm.UpdateSettingsAsync(vm.Settings with { HistoryLimit = (int)e.NewValue }); } catch (Exception ex) { vm.ReportError(ex); }
    }
}
