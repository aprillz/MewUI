using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Manages toast notification lifecycle: slide-in/out with shadow.
/// Hosted directly by <see cref="Window"/> as an overlay between Content and Adorners.
/// </summary>
internal sealed class ToastPresenter : Control, IVisualTreeHost
{
    private const double SHADOW_BLUR_RADIUS = 24;
    private const double SHADOW_OFFSET_Y = 4;

    private readonly TransitionContentControl _transition;
    private DispatcherTimer? _timer;

    public ToastPresenter()
    {
        _transition = new TransitionContentControl
        {
            Transition = ContentTransition.CreateSlide(SlideDirection.Down, 300),
        };

        AttachChild(_transition);
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Vertical position as a ratio of the available height (0.0 = top, 1.0 = bottom). Default is 0.8 (4/5).
    /// </summary>
    public double VerticalPosition { get; set; } = 0.80;

    /// <summary>
    /// Raised once the toast has finished dismissing and holds no content. Lets the owning
    /// service remove this presenter from the overlay layer instead of it sitting idle forever.
    /// </summary>
    public event Action? BecameIdle;

    public void Show(string text, TimeSpan duration)
    {
        _transition.Content = CreateDefaultContent(text);

        _timer?.Stop();
        _timer ??= new DispatcherTimer();
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Interval = duration;
        _timer.Start();

        InvalidateMeasure();
    }

    public void Hide()
    {
        _timer?.Stop();
        _transition.Content = null;
        BecameIdle?.Invoke();
    }

    private void OnTimerTick()
    {
        _timer?.Stop();
        _transition.Content = null;
        BecameIdle?.Invoke();
    }

    internal static TimeSpan ComputeDuration(string text)
        => TimeSpan.FromMilliseconds(1000 + text.Length * 35);

    private Element CreateDefaultContent(string text)
    {
        var palette = Theme.Palette;
        var metrics = Theme.Metrics;
        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Child = new TextBlock
            {
                Text = text,
            },
        };
        border.WithTheme((t, c) => c
            .CornerRadius(t.Metrics.ControlCornerRadius)
            .BorderThickness(t.Metrics.ControlBorderThickness)
            .Background(t.Palette.ControlBackground)
            .Foreground(t.Palette.WindowText)
            .BorderBrush(t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5)));
        return new ShadowHost(border);
    }

    /// <summary>
    /// Wrapper that draws a drop shadow behind its child.
    /// Lives inside TransitionContentControl so Transform + GlobalAlpha apply to shadow too.
    /// </summary>
    private sealed class ShadowHost : ContentControl
    {
        internal ShadowHost(UIElement child) => Content = child;

        protected override void OnRender(IGraphicsContext context)
        {
            if (Content != null)
            {
                var cb = Content.Bounds;
                if (cb.Width > 0 && cb.Height > 0)
                {
                    int strength = Theme.IsDark ? 128 : 64;
                    double cornerRadius = Theme.Metrics.ControlCornerRadius;
                    context.DrawBoxShadow(
                        new Rect(cb.X, cb.Y + SHADOW_OFFSET_Y, cb.Width, cb.Height - SHADOW_OFFSET_Y),
                        cornerRadius, SHADOW_BLUR_RADIUS,
                        Color.FromArgb((byte)strength, 0, 0, 0));
                }
            }

            base.OnRender(context);
        }
    }

    #region Layout

    protected override Size MeasureContent(Size availableSize)
    {
        _transition.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var desired = _transition.DesiredSize;
        double x = bounds.X + (bounds.Width - desired.Width) * 0.5;
        double y = bounds.Y + bounds.Height * VerticalPosition - desired.Height * 0.5;
        _transition.Arrange(new Rect(x, y, desired.Width, desired.Height));
    }

    #endregion

    #region Rendering

    protected override void RenderSubtree(IGraphicsContext context)
    {
        _transition.Render(context);
    }

    #endregion

    #region HitTest

    protected override UIElement? OnHitTest(Point point) => null;

    #endregion

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_transition);

    protected override void OnDispose()
    {
        _timer?.Dispose();
        _timer = null;
        base.OnDispose();
    }
}
