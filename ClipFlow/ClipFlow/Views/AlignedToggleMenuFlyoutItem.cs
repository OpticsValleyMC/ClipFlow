using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClipFlow.Views;

// Keep the framework's Toggle control, keyboard behavior and automation peer.
// Display its mark in the same icon slot used by the other menu commands.
internal sealed class AlignedToggleMenuFlyoutItem : ToggleMenuFlyoutItem
{
    private readonly FontIcon _mark = new() { Glyph = "\uE73E" };
    internal AlignedToggleMenuFlyoutItem()
    {
        DefaultStyleKey = typeof(ToggleMenuFlyoutItem);
        Icon = _mark;
        RegisterPropertyChangedCallback(IsCheckedProperty, (_, _) => UpdateMark());
        UpdateMark();
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("CheckGlyph") is FontIcon check && VisualTreeHelper.GetParent(check) is Grid grid)
        {
            // Reserve the native check gutter so text stays aligned with the
            // other commands, then let the ordinary Icon presenter draw it.
            grid.ColumnDefinitions[Grid.GetColumn(check)].Width = new GridLength(check.FontSize + check.Margin.Left + check.Margin.Right);
            check.Visibility = Visibility.Collapsed;
        }
        UpdateMark();
    }
    private void UpdateMark() => _mark.Opacity = IsChecked ? 1 : 0;
}
