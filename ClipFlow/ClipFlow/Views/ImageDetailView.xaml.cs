using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Windows.Foundation;

namespace ClipFlow.Views;

public sealed partial class ImageDetailView : UserControl
{
    private readonly string _path;
    private bool _started;
    private double _pixelScale = 1;
    private double _nativeWidth, _nativeHeight, _baseScale = 1;
    private uint? _dragPointer;
    private Point _dragStart;
    private double _dragHorizontal, _dragVertical;
    private bool _pointerOver;
    private PointerRoutedEventArgs? _lastWheelEvent;
    private readonly InputSystemCursor _handCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    private readonly InputSystemCursor _dragCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
    private readonly InputSystemCursor _arrowCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    public Task LoadTask { get; private set; } = Task.CompletedTask;
#if DEBUG
    internal List<(double X, double Y)> CompletedDrags { get; } = [];
    internal bool IsDragging => _dragPointer is not null;
#endif
    public ImageDetailView(string path)
    {
        InitializeComponent();
        _path = path;
        Loaded += (_, _) => { if (!_started) { _started = true; LoadTask = LoadAsync(); } };
        PreviewScroll.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Preview_PointerPressed), true);
        PreviewScroll.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Preview_PointerMoved), true);
        PreviewScroll.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Preview_PointerReleased), true);
        OriginalImage.PointerWheelChanged += Preview_PointerWheelChanged;
        PreviewScroll.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Preview_PointerWheelChanged), true);
        PreviewScroll.PointerCaptureLost += (_, _) => EndDrag();
        PreviewScroll.PointerCanceled += (_, _) => EndDrag();
        PreviewScroll.PointerEntered += (_, _) => { _pointerOver = true; UpdateCursor(); };
        PreviewScroll.PointerExited += (_, _) => { _pointerOver = false; UpdateCursor(); };
        Unloaded += (_, _) => { EndDrag(); _lastWheelEvent = null; OriginalImage.Source = null; };
    }
    private bool CanPan => OriginalImage.Source is not null && (PreviewScroll.ScrollableWidth > .5 || PreviewScroll.ScrollableHeight > .5);
    private void Preview_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // An image event bubbles to ScrollViewer; process it only once. Handled
        // native events over empty margins must still use our zoom behavior.
        if (ReferenceEquals(e, _lastWheelEvent) || OriginalImage.Source is null) return;
        var point = e.GetCurrentPoint(PreviewScroll);
        if (point.Properties.IsHorizontalMouseWheel || point.Properties.MouseWheelDelta == 0) return;
        _lastWheelEvent = e;
        e.Handled = true;
        EndDrag();
        ZoomAt(Math.Pow(1.25, Math.Clamp(point.Properties.MouseWheelDelta / 120d, -8, 8)), point.Position);
    }
    private void Preview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer is not null || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse || !CanPan) return;
        var point = e.GetCurrentPoint(PreviewScroll);
        if (!point.Properties.IsLeftButtonPressed || point.Position.X >= PreviewScroll.ViewportWidth - 14 || point.Position.Y >= PreviewScroll.ViewportHeight - 14) return;
        if (!PreviewScroll.CapturePointer(e.Pointer)) return;
        _dragPointer = e.Pointer.PointerId;
        _dragStart = point.Position;
        _dragHorizontal = PreviewScroll.HorizontalOffset; _dragVertical = PreviewScroll.VerticalOffset;
        e.Handled = true; UpdateCursor();
    }
    private void Preview_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(PreviewScroll);
        if (!point.Properties.IsLeftButtonPressed) { EndDrag(); return; }
        // Coordinates are relative to the stationary viewport, not the moving/zoomed image.
        PreviewScroll.ChangeView(Math.Clamp(_dragHorizontal - (point.Position.X - _dragStart.X), 0, PreviewScroll.ScrollableWidth),
            Math.Clamp(_dragVertical - (point.Position.Y - _dragStart.Y), 0, PreviewScroll.ScrollableHeight), null, true);
        e.Handled = true;
    }
    private void Preview_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer != e.Pointer.PointerId) return;
        EndDrag(); e.Handled = true;
    }
    private void EndDrag()
    {
        if (_dragPointer is null) return;
#if DEBUG
        CompletedDrags.Add((PreviewScroll.HorizontalOffset, PreviewScroll.VerticalOffset));
#endif
        _dragPointer = null; PreviewScroll.ReleasePointerCaptures(); UpdateCursor();
    }
    private void UpdateCursor() => ProtectedCursor = _dragPointer is not null ? _dragCursor : _pointerOver && CanPan ? _handCursor : _arrowCursor;
    private async Task LoadAsync()
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_path);
            using var stream = await file.OpenReadAsync();
            if (stream.Size > 64 * 1024 * 1024) throw new InvalidDataException("图片超过读取上限。");
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 16_000_000)
                throw new InvalidDataException("图片尺寸无效。");
            stream.Seek(0);
            // No decode-size limit: only the open detail view loads the original pixels.
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (!IsLoaded) return;
            _pixelScale = XamlRoot?.RasterizationScale ?? 1;
            _nativeWidth = bitmap.PixelWidth / _pixelScale;
            _nativeHeight = bitmap.PixelHeight / _pixelScale;
            OriginalImage.Width = _nativeWidth;
            OriginalImage.Height = _nativeHeight;
            OriginalImage.Source = bitmap;
            ZoomControls.IsEnabled = true;
            UpdateLayout();
            Fit();
        }
        catch
        {
            Failure.Visibility = Visibility.Visible;
            PreviewScroll.Visibility = Visibility.Collapsed;
        }
        finally { LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed; }
    }
    private void Fit()
    {
        EndDrag();
        if (OriginalImage.Source is null) return;
        // Very tall/wide images can need less than ScrollViewer's native 10% minimum.
        // Scale the visual's base size; the loaded bitmap still retains all original pixels.
        var naturalFit = Math.Min(PreviewScroll.ViewportWidth / _nativeWidth, PreviewScroll.ViewportHeight / _nativeHeight);
        _baseScale = Math.Min(1, naturalFit / .1);
        OriginalImage.Width = _nativeWidth * _baseScale;
        OriginalImage.Height = _nativeHeight * _baseScale;
        UpdateLayout();
        var factor = Math.Min(1, Math.Min(PreviewScroll.ViewportWidth / OriginalImage.Width, PreviewScroll.ViewportHeight / OriginalImage.Height));
        PreviewScroll.ChangeView(0, 0, (float)Math.Clamp(factor, .1, 8), true);
        UpdateZoomLabel();
    }
    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();
    private void Actual_Click(object sender, RoutedEventArgs e)
    {
        EndDrag();
        _baseScale = 1;
        OriginalImage.Width = _nativeWidth;
        OriginalImage.Height = _nativeHeight;
        UpdateLayout();
        PreviewScroll.ChangeView(0, 0, 1, true);
        UpdateZoomLabel();
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(1.25f);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(.8f);
    private void Zoom(float multiplier) => ZoomAt(multiplier, new Point(PreviewScroll.ViewportWidth / 2, PreviewScroll.ViewportHeight / 2));
    private void ZoomAt(double multiplier, Point anchor)
    {
        if (OriginalImage.Source is null || !IsLoaded) return;
        var oldZoom = PreviewScroll.ZoomFactor;
        var newZoom = (float)Math.Clamp(oldZoom * multiplier, .1, 8);
        if (Math.Abs(newZoom - oldZoom) < .00001) return;
        var x = Math.Clamp(anchor.X, 0, PreviewScroll.ViewportWidth);
        var y = Math.Clamp(anchor.Y, 0, PreviewScroll.ViewportHeight);
        // Center-aligned content has a margin while it is smaller than the viewport.
        double Offset(double position, double current, double size, double viewport)
        {
            var oldMargin = Math.Max(0, (viewport - size * oldZoom) / 2);
            var newMargin = Math.Max(0, (viewport - size * newZoom) / 2);
            var pixel = Math.Clamp((current + position - oldMargin) / oldZoom, 0, size);
            return Math.Clamp(pixel * newZoom + newMargin - position, 0, Math.Max(0, size * newZoom - viewport));
        }
        PreviewScroll.ChangeView(Offset(x, PreviewScroll.HorizontalOffset, OriginalImage.Width, PreviewScroll.ViewportWidth),
            Offset(y, PreviewScroll.VerticalOffset, OriginalImage.Height, PreviewScroll.ViewportHeight), newZoom, true);
    }
    private void Scroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { UpdateZoomLabel(); UpdateCursor(); }
    private void UpdateZoomLabel() => ZoomLabel.Text = $"{PreviewScroll.ZoomFactor * _baseScale:P0}";
}
