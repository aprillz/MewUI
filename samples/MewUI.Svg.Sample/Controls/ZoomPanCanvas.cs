using System.Numerics;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Svg.Sample.Controls;

/// <summary>
/// Zoom/pan host for a single child. Designed to sit inside a ScrollViewer.
/// </summary>
public sealed class ZoomPanCanvas : FrameworkElement, IVisualTreeHost
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
                if (!self._isAnimatingZoom)
                {
                    self.ScrollToKeepViewCenter(oldZoom, newZoom);
                }
            });

    public static readonly MewProperty<bool> CenterContentProperty =
        MewProperty<bool>.Register<ZoomPanCanvas>(nameof(CenterContent), true, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> ShowCheckerboardBackgroundProperty =
        MewProperty<bool>.Register<ZoomPanCanvas>(nameof(ShowCheckerboardBackground), false, MewPropertyOptions.AffectsRender);

    private bool _isPanning;
    private bool _isAnimatingZoom;
    private Point _panStart;
    private double _panStartScrollX;
    private double _panStartScrollY;
    private AnimationClock? _zoomClock;
    private Tween<double>? _zoomTween;
    private Action<double>? _scrollOnZoomTick;
    private ImageScaleQuality? _savedImageQuality;

    public ZoomPanCanvas()
    {
        SkipViewportCull = true;
    }

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

    public bool CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public bool ShowCheckerboardBackground
    {
        get => GetValue(ShowCheckerboardBackgroundProperty);
        set => SetValue(ShowCheckerboardBackgroundProperty, value);
    }

    public void ResetView(ScrollViewer? scrollViewer = null)
    {
        _zoomClock?.Stop();
        _zoomClock = null;
        _zoomTween = null;
        _scrollOnZoomTick = null;
        _isAnimatingZoom = false;
        Zoom = 1.0;
        (scrollViewer ?? FindParentScrollViewer())?.SetScrollOffsets(0, 0);
    }

    public void FitToView(ScrollViewer? scrollViewer = null)
        => FitToViewInternal(scrollViewer, retriesLeft: 16);

    private void FitToViewInternal(ScrollViewer? scrollViewer, int retriesLeft)
    {
        _zoomClock?.Stop();
        _zoomClock = null;
        _zoomTween = null;
        _scrollOnZoomTick = null;
        _isAnimatingZoom = false;

        var sv = scrollViewer ?? FindParentScrollViewer();
        var child = Child;
        if (sv == null || child == null)
        {
            Zoom = 1.0;
            return;
        }

        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = child.DesiredSize;
        double vpW = sv.ViewportWidth;
        double vpH = sv.ViewportHeight;
        if (natural.Width <= 0 || natural.Height <= 0 || vpW <= 0 || vpH <= 0)
        {
            // Bounded retry: if the child or scrollviewer never report a measurable size
            // (e.g. an SVG document with width/height that don't resolve via the host's
            // measurement context), an unbounded BeginInvoke chain pegs the dispatcher and
            // freezes the UI. Cap the retries and fall through to Zoom=1.0 so the user at
            // least sees the document at a sane default scale.
            if (retriesLeft > 0)
            {
                Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render,
                    () => FitToViewInternal(sv, retriesLeft - 1));
            }
            else
            {
                Zoom = 1.0;
            }
            return;
        }

        double fitZoom = Math.Min(vpW / natural.Width, vpH / natural.Height);
        Zoom = Math.Clamp(fitZoom, MinZoom, MaxZoom);
        sv.SetScrollOffsets(0, 0);
        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () => sv.SetScrollOffsets(0, 0));
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
        double dpiScale = GetDpi() / 96.0;
        double w = Math.Floor(natural.Width * Zoom * dpiScale) / dpiScale;
        double h = Math.Floor(natural.Height * Zoom * dpiScale) / dpiScale;

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
        child.Arrange(new Rect(0, 0, natural.Width, natural.Height));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (ShowCheckerboardBackground && Bounds.Width > 0 && Bounds.Height > 0)
        {
            AlphaCheckerboard.Fill(context, Bounds, Theme.IsDark);
        }

        var child = Child;
        if (child == null)
        {
            return;
        }

        var bounds = Bounds;
        var scrollViewer = FindParentScrollViewer();
        var viewportSize = scrollViewer is null
            ? bounds.Size
            : new Size(
                scrollViewer.ViewportWidth > 0 ? scrollViewer.ViewportWidth : bounds.Width,
                scrollViewer.ViewportHeight > 0 ? scrollViewer.ViewportHeight : bounds.Height);
        float zoom = (float)Zoom;

        context.Save();

        var current = context.GetTransform();
        var natural = child.DesiredSize;
        var centerOffset = GetCenterOffset(viewportSize, natural, Zoom);
        float cx = (float)centerOffset.X;
        float cy = (float)centerOffset.Y;

        var transform = Matrix3x2.CreateScale(zoom, zoom)
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
        if (sv == null || Child == null)
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
        double viewportX = contentX - sv.HorizontalOffset;
        double viewportY = contentY - sv.VerticalOffset;

        var natural = Child.DesiredSize;
        var oldCenterOffset = GetCenterOffset(Bounds.Size, natural, oldZoom);
        double ratio = newZoom / oldZoom;
        var newCenterOffset = GetCenterOffset(new Size(sv.ViewportWidth, sv.ViewportHeight), natural, newZoom);
        double newScrollX = (contentX - oldCenterOffset.X) * ratio + newCenterOffset.X - viewportX;
        double newScrollY = (contentY - oldCenterOffset.Y) * ratio + newCenterOffset.Y - viewportY;

        _isAnimatingZoom = true;
        Zoom = newZoom;
        _isAnimatingZoom = false;

        double sx = Math.Max(0, newScrollX);
        double sy = Math.Max(0, newScrollY);
        sv.SetScrollOffsets(sx, sy);
        e.Handled = true;

        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () => sv.SetScrollOffsets(sx, sy));
    }

    private Point GetCenterOffset(Size viewport, Size natural, double zoom)
    {
        if (!CenterContent)
        {
            return default;
        }

        return new Point(
            Math.Max(0, (viewport.Width - natural.Width * zoom) * 0.5),
            Math.Max(0, (viewport.Height - natural.Height * zoom) * 0.5));
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
        sv.SetScrollOffsets(Math.Max(0, _panStartScrollX - dx), Math.Max(0, _panStartScrollY - dy));
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
            Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () => scrollAction?.Invoke(v));
        };
        _zoomTween.Bind(_zoomClock);
        _zoomClock.Start();
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
        double worldCenterX = (sv.HorizontalOffset + vpW * 0.5 - oldCx) / oldZoom;
        double worldCenterY = (sv.VerticalOffset + vpH * 0.5 - oldCy) / oldZoom;
        double newCx = Math.Max(0, (vpW - natural.Width * newZoom) * 0.5);
        double newCy = Math.Max(0, (vpH - natural.Height * newZoom) * 0.5);
        double sx = Math.Max(0, worldCenterX * newZoom + newCx - vpW * 0.5);
        double sy = Math.Max(0, worldCenterY * newZoom + newCy - vpH * 0.5);
        sv.SetScrollOffsets(sx, sy);
        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () => sv.SetScrollOffsets(sx, sy));
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
