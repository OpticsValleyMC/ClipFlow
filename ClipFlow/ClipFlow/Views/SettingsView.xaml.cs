using ClipFlow.Core;
using ClipFlow.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipFlow.Views;
public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AboutVersion.Text = AppInfo.AboutVersion; AboutAuthor.Text = AppInfo.AboutAuthor;
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
    private async void Limit_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsReady || double.IsNaN(e.NewValue) || (int)e.NewValue == vm.HistoryLimit) return;
        try { await vm.UpdateSettingsAsync(vm.Settings with { HistoryLimit = (int)e.NewValue }); } catch (Exception ex) { vm.ReportError(ex); }
    }
}
