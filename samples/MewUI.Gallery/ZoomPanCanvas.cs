using System.Numerics;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

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
            scrollAction?.Invoke(targetZoom);
        };
        _zoomTween = new Tween<double>(Zoom, targetZoom, Lerp.Double);
        _zoomTween.ValueChanged += v =>
        {
            Zoom = v;
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
        var natural = child.DesiredSize;
        float cx = CenterContent ? (float)Math.Max(0, (bounds.Width - natural.Width * zoom) * 0.5) : 0f;
        float cy = CenterContent ? (float)Math.Max(0, (bounds.Height - natural.Height * zoom) * 0.5) : 0f;

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
        // Continuous zoom: full notch = 1.15×, sub-notch (trackpad) scales smoothly via the exponent.
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
        double oldCx = CenterContent ? Math.Max(0, (bounds.Width - natural.Width * oldZoom) * 0.5) : 0;
        double oldCy = CenterContent ? Math.Max(0, (bounds.Height - natural.Height * oldZoom) * 0.5) : 0;

        double ratio = newZoom / oldZoom;
        double newCx = CenterContent ? Math.Max(0, (sv.ViewportWidth - natural.Width * newZoom) * 0.5) : 0;
        double newCy = CenterContent ? Math.Max(0, (sv.ViewportHeight - natural.Height * newZoom) * 0.5) : 0;
        double newScrollX = (contentX - oldCx) * ratio + newCx - viewportX;
        double newScrollY = (contentY - oldCy) * ratio + newCy - viewportY;

        _isAnimatingZoom = true;
        Zoom = newZoom;
        _isAnimatingZoom = false;

        double sx = Math.Max(0, newScrollX);
        double sy = Math.Max(0, newScrollY);
        sv.SetScrollOffsets(sx, sy);

        e.Handled = true;
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
