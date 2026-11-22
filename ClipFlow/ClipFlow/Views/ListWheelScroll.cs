using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ClipFlow.Views;

internal sealed class ListWheelScroll
{
    private const double WheelDeltaPerNotch = 120;
    private readonly double _distancePerNotch;
    private ScrollViewer? _viewer;
    private double? _destination;

    internal ListWheelScroll(ListView list, double distancePerNotch)
    {
        _distancePerNotch = distancePerNotch;
        list.Loaded += (_, _) => Attach(list);
        list.PreviewKeyDown += (_, _) => Reset();
    }

    internal void Reset() => _destination = null;

    private void Attach(ListView list)
    {
        if (_viewer is not null || FindScrollViewer(list) is not { } viewer) return;
        _viewer = viewer;

        // Handle wheel input before it reaches the ScrollViewer's own animation.
        if (viewer.Content is UIElement content) content.PointerWheelChanged += WheelChanged;
        viewer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Reset()), true);
        viewer.ViewChanged += (_, _) =>
        {
            if (_destination is { } destination && Math.Abs(viewer.VerticalOffset - destination) < .5) Reset();
        };
    }

    private void WheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (_viewer is not { } viewer) return;
        var properties = args.GetCurrentPoint(viewer).Properties;
        if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0) return;

        var distance = properties.MouseWheelDelta / WheelDeltaPerNotch * _distancePerNotch;
        var offset = Math.Clamp((_destination ?? viewer.VerticalOffset) - distance, 0, viewer.ScrollableHeight);
        // Accumulate new deltas while the preceding offset is still pending.
        _destination = offset;
        viewer.ChangeView(null, offset, null, true);
        args.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }
}
