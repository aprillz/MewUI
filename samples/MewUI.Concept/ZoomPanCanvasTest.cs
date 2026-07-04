using System.Numerics;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Concept;

internal static class ZoomPanCanvasTest
{
    private const string ImageUrl = "https://raw.githubusercontent.com/aprillz/MewUI/main/assets/images/soonduk.jpg";

    internal static Window Create()
    {
        var image = new Image()
            .StretchMode(Stretch.None)
            .ImageScaleQuality(ImageScaleQuality.HighQuality);

        var canvas = new ZoomPanCanvas { Child = image, CenterContent = true };

        // Download image from web asynchronously.
        DownloadAndSetImage(image);

        var scrollViewer = new ScrollViewer()
            .Padding(0)
            .HorizontalScroll(ScrollMode.Auto)
            .VerticalScroll(ScrollMode.Auto);
        scrollViewer.Content = canvas;

        // Log-scale slider: t ∈ [0,1] → zoom = MinZoom * (MaxZoom/MinZoom)^t
        double logRatio = Math.Log(ZoomPanCanvas.MaxZoom / ZoomPanCanvas.MinZoom);
        var slider = new Slider().Width(150)
            .CenterVertical()
            .Minimum(0)
            .Maximum(1)
            .SmallChange(0.01)
            .Value(Math.Log(1.0 / ZoomPanCanvas.MinZoom) / logRatio)
            .Bind(Slider.ValueProperty, canvas, ZoomPanCanvas.ZoomProperty,
                zoom => Math.Log(zoom / ZoomPanCanvas.MinZoom) / logRatio,
                t => ZoomPanCanvas.MinZoom * Math.Exp(t * logRatio));

        var zoomLabel = new TextBlock()
            .Text("100%")
            .CenterVertical()
            .Width(70);

        canvas.ZoomChanged += zoom => zoomLabel.Text = $"{zoom * 100:0}%";

        var resetButton = new Button()
            .Content("Reset")
            .Width(70)
            .OnClick(() =>
            {
                canvas.AnimateZoomWithViewCenter(scrollViewer, 1.0);
            });

        var fitButton = new Button()
            .Content("Fit")
            .Width(70)
            .OnClick(() =>
            {
                var viewportW = scrollViewer.ViewportWidth;
                var viewportH = scrollViewer.ViewportHeight;
                if (viewportW <= 0 || viewportH <= 0)
                {
                    return;
                }

                var childSize = canvas.ChildNaturalSize;
                if (childSize.Width <= 0 || childSize.Height <= 0)
                {
                    return;
                }

                var fitZoom = Math.Min(viewportW / childSize.Width, viewportH / childSize.Height);
                canvas.AnimateZoomWithViewCenter(scrollViewer, Math.Clamp(fitZoom, ZoomPanCanvas.MinZoom, ZoomPanCanvas.MaxZoom));
            });

        var toolbar = new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Margin(new Thickness(8))
            .Children(
                new TextBlock().Text("Zoom:").CenterVertical(),
                slider,
                zoomLabel,
                resetButton,
                fitButton,
                new CheckBox()
                    .Content("Center")
                    .Check()
                    .CenterVertical()
                    .OnCheckedChanged(isChecked => canvas.CenterContent = isChecked),
                new TextBlock()
                    .Text("Wheel to zoom, Drag or Scrollbar to pan")
                    .Foreground(Color.FromRgb(120, 120, 120))
                    .CenterVertical()
            );

        var window = new Window()
            .Resizable(900, 650)
            .Title("Zoom & Pan Canvas");

        window.Content = new DockPanel()
            .Children(
                toolbar.DockTop(),
                scrollViewer
            );

        return window;
    }

    private static async void DownloadAndSetImage(Image image)
    {
        try
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(ImageUrl);
            var source = ImageSource.FromBytes(bytes);
            source.EnsureDecode();
            image.Source = source;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image download failed: {ex.Message}");
        }
    }
}

/// <summary>
/// A container that applies a zoom (scale) transform to a single child.
/// Designed to be placed inside a ScrollViewer - measures at childSize * zoom
/// so the ScrollViewer provides scrollbars automatically.
/// Wheel zooms anchored to the cursor position.
/// </summary>
public class ZoomPanCanvas : FrameworkElement, IVisualTreeHost
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 20.0;

    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<ZoomPanCanvas>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) =>
            {
                if (oldValue != null)
                {
                    oldValue.SkipViewportCull = false;
                    self.DetachChild(oldValue);
                }
                if (newValue != null)
                {
                    self.AttachChild(newValue);
                    newValue.SkipViewportCull = true;
                }
            });

    public static readonly MewProperty<double> ZoomProperty =
        MewProperty<double>.Register<ZoomPanCanvas>(nameof(Zoom), 1.0,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, oldZoom, newZoom) =>
            {
                self.ZoomChanged?.Invoke(self.Zoom);
                if (!self._isAnimatingZoom)
                {
                    self.ScrollToKeepViewCenter(oldZoom, newZoom);
                }
            });

    public static readonly MewProperty<bool> CenterContentProperty =
        MewProperty<bool>.Register<ZoomPanCanvas>(nameof(CenterContent), false,
            MewPropertyOptions.AffectsRender);

    private bool _isPanning;
    private bool _isAnimatingZoom;
    private Point _panStart;
    private double _panStartScrollX;
    private double _panStartScrollY;

    private AnimationClock? _zoomClock;
    private Tween<double>? _zoomTween;
    private Action<double>? _scrollOnZoomTick;
    private ImageScaleQuality? _savedImageQuality;

    public event Action<double>? ZoomChanged;

    public UIElement? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, MinZoom, MaxZoom));
    }

    /// <summary>
    /// When true, content is centered within the viewport when zoomed content is smaller than the viewport.
    /// When false, content is aligned to the top-left corner.
    /// </summary>
    public bool CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public void AnimateZoomWithViewCenter(ScrollViewer sv, double targetZoom, int durationMs = 250)
    {
        if (!CenterContent || sv.ViewportWidth <= 0)
        {
            AnimateZoomTo(targetZoom, durationMs);
            return;
        }

        double vpW = sv.ViewportWidth;
        double vpH = sv.ViewportHeight;
        double scrollX = sv.HorizontalOffset;
        double scrollY = sv.VerticalOffset;
        double startZoom = Zoom;
        var natural = Child?.DesiredSize ?? Size.Empty;

        double oldCx = Math.Max(0, (vpW - natural.Width * startZoom) * 0.5);
        double oldCy = Math.Max(0, (vpH - natural.Height * startZoom) * 0.5);
        double worldCenterX = (scrollX + vpW * 0.5 - oldCx) / startZoom;
        double worldCenterY = (scrollY + vpH * 0.5 - oldCy) / startZoom;

        _scrollOnZoomTick = z =>
        {
            double cx = Math.Max(0, (vpW - natural.Width * z) * 0.5);
            double cy = Math.Max(0, (vpH - natural.Height * z) * 0.5);
            double sx = Math.Max(0, worldCenterX * z + cx - vpW * 0.5);
            double sy = Math.Max(0, worldCenterY * z + cy - vpH * 0.5);
            sv.SetScrollOffsets(sx, sy);
        };
        AnimateZoomTo(targetZoom, durationMs);
    }

    public void AnimateZoomTo(double targetZoom, int durationMs = 250)
    {
        targetZoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);
        _zoomClock?.Stop();
        _isAnimatingZoom = true;

        // Save original quality only on first animation; skip if already Fast.
        var image = Child as Image;
        if (image != null && _savedImageQuality == null)
        {
            _savedImageQuality = image.ImageScaleQuality;
            image.ImageScaleQuality = ImageScaleQuality.Fast;
        }

        var scrollAction = _scrollOnZoomTick;
        _zoomClock = new AnimationClock(TimeSpan.FromMilliseconds(durationMs), Easing.EaseOutCubic);
        _zoomClock.CompletedCallback = () =>
        {
            _isAnimatingZoom = false;
            _scrollOnZoomTick = null;
            if (image != null && _savedImageQuality.HasValue)
            {
                image.ImageScaleQuality = _savedImageQuality.Value;
                _savedImageQuality = null;
            }
            // Final scroll after layout settles.
            scrollAction?.Invoke(targetZoom);
        };
        _zoomTween = new Tween<double>(Zoom, targetZoom, Lerp.Double);
        _zoomTween.ValueChanged += v =>
        {
            Zoom = v;
            // Defer scroll to after layout so extent is up-to-date.
            Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () =>
                scrollAction?.Invoke(v));
        };
        _zoomTween.Bind(_zoomClock);
        _zoomClock.Start();
    }

    public Size ChildNaturalSize
    {
        get
        {
            var child = Child;
            if (child == null)
            {
                return Size.Empty;
            }

            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return child.DesiredSize;
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var child = Child;
        if (child == null)
        {
            return Size.Empty;
        }

        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = child.DesiredSize;
        var zoom = Zoom;

        // Floor to pixel boundary so that RoundSizeToPixels never inflates
        // the extent beyond the true childSize * zoom value.
        double dpiScale = GetDpi() / 96.0;
        double w = Math.Floor(natural.Width * zoom * dpiScale) / dpiScale;
        double h = Math.Floor(natural.Height * zoom * dpiScale) / dpiScale;
        return new Size(w, h);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var child = Child;
        if (child == null)
        {
            return;
        }

        var natural = child.DesiredSize;
        child.Arrange(new Rect(bounds.X, bounds.Y, natural.Width, natural.Height));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var child = Child;
        if (child == null)
        {
            return;
        }

        var bounds = Bounds;
        var zoom = (float)Zoom;

        context.Save();
        context.SetClip(bounds);

        var current = context.GetTransform();
        // Centering offset when zoomed content is smaller than bounds.
        var natural = child.DesiredSize;
        float cx = CenterContent ? (float)Math.Max(0, (bounds.Width - natural.Width * zoom) * 0.5) : 0f;
        float cy = CenterContent ? (float)Math.Max(0, (bounds.Height - natural.Height * zoom) * 0.5) : 0f;

        // Scale around (bounds.X, bounds.Y), then offset for centering.
        var transform = Matrix3x2.CreateTranslation(-(float)bounds.X, -(float)bounds.Y)
            * Matrix3x2.CreateScale(zoom, zoom)
            * Matrix3x2.CreateTranslation((float)bounds.X + cx, (float)bounds.Y + cy)
            * current;

        context.SetTransform(transform);
        child.Render(context);
        context.Restore();
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        return Bounds.Contains(point) ? this : null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.Delta.Y == 0 || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
        {
            return;
        }

        var sv = FindParentScrollViewer();
        if (sv == null)
        {
            return;
        }

        double oldZoom = Zoom;
        double factor = Math.Pow(1.15, e.Delta.Y);
        double newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 1e-9)
        {
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(this);
        double contentX = pos.X;
        double contentY = pos.Y;
        double scrollX = sv.HorizontalOffset;
        double scrollY = sv.VerticalOffset;
        double viewportX = contentX - scrollX;
        double viewportY = contentY - scrollY;

        var natural = Child!.DesiredSize;
        var bounds = Bounds;
        double cx = CenterContent ? Math.Max(0, (bounds.Width - natural.Width * oldZoom) * 0.5) : 0;
        double cy = CenterContent ? Math.Max(0, (bounds.Height - natural.Height * oldZoom) * 0.5) : 0;

        double ratio = newZoom / oldZoom;
        double newCx = CenterContent ? Math.Max(0, (sv.ViewportWidth - natural.Width * newZoom) * 0.5) : 0;
        double newCy = CenterContent ? Math.Max(0, (sv.ViewportHeight - natural.Height * newZoom) * 0.5) : 0;
        double newScrollX = (contentX - cx) * ratio + newCx - viewportX;
        double newScrollY = (contentY - cy) * ratio + newCy - viewportY;

        double worldX = (contentX - cx) / oldZoom;
        double worldY = (contentY - cy) / oldZoom;

        _isAnimatingZoom = true;
        Zoom = newZoom;
        _isAnimatingZoom = false;

        double sx = Math.Max(0, newScrollX);
        double sy = Math.Max(0, newScrollY);
        sv.SetScrollOffsets(sx, sy);

        e.Handled = true;
        // Layout hasn't run yet - extent is stale, so the offset may be clamped to 0.
        // Re-apply after layout when extent reflects the new zoom.
        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () =>
            sv.SetScrollOffsets(sx, sy));

    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left)
        {
            return;
        }

        var sv = FindParentScrollViewer();
        if (sv == null)
        {
            return;
        }

        _isPanning = true;
        // Use window-relative position (DIPs) for stable delta - it doesn't
        // shift as the scroll offset changes during the drag, and the Y axis
        // is consistent across platforms (unlike ScreenPosition on macOS).
        _panStart = e.GetPosition((UIElement)FindVisualRoot()!);
        _panStartScrollX = sv.HorizontalOffset;
        _panStartScrollY = sv.VerticalOffset;

        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPanning)
        {
            return;
        }

        var sv = FindParentScrollViewer();
        if (sv == null)
        {
            return;
        }

        var windowPos = e.GetPosition((UIElement)FindVisualRoot()!);
        double dx = windowPos.X - _panStart.X;
        double dy = windowPos.Y - _panStart.Y;

        sv.SetScrollOffsets(
            Math.Max(0, _panStartScrollX - dx),
            Math.Max(0, _panStartScrollY - dy));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;

        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }
    }

    private void ScrollToKeepViewCenter(double oldZoom, double newZoom)
    {
        if (!CenterContent)
        {
            return;
        }

        var sv = FindParentScrollViewer();
        if (sv == null || Child == null)
        {
            return;
        }

        double vpW = sv.ViewportWidth;
        double vpH = sv.ViewportHeight;
        if (vpW <= 0 || vpH <= 0)
        {
            return;
        }

        var natural = Child.DesiredSize;

        // Account for centering offset when content is smaller than viewport.
        double oldCx = Math.Max(0, (vpW - natural.Width * oldZoom) * 0.5);
        double oldCy = Math.Max(0, (vpH - natural.Height * oldZoom) * 0.5);

        double scrollX = sv.HorizontalOffset;
        double scrollY = sv.VerticalOffset;
        double worldCenterX = (scrollX + vpW * 0.5 - oldCx) / oldZoom;
        double worldCenterY = (scrollY + vpH * 0.5 - oldCy) / oldZoom;

        double newCx = Math.Max(0, (vpW - natural.Width * newZoom) * 0.5);
        double newCy = Math.Max(0, (vpH - natural.Height * newZoom) * 0.5);
        double sx = Math.Max(0, worldCenterX * newZoom + newCx - vpW * 0.5);
        double sy = Math.Max(0, worldCenterY * newZoom + newCy - vpH * 0.5);
        sv.SetScrollOffsets(sx, sy);

        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () =>
            sv.SetScrollOffsets(sx, sy));
    }

    private ScrollViewer? FindParentScrollViewer()
    {
        var current = Parent;
        while (current != null)
        {
            if (current is ScrollViewer sv)
            {
                return sv;
            }

            current = current.Parent;
        }

        return null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);
}
