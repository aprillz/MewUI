using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A content control that animates transitions when its <see cref="Content"/> changes.
/// Supports fade and slide transitions with optional delay.
/// </summary>
public class TransitionContentControl : Control, IVisualTreeHost
{
    private Element? _currentContent;
    private Element? _oldContent;
    private AnimationClock? _delayClock;
    private AnimationClock? _clock;
    private double _progress = 1.0;

    /// <summary>
    /// Gets or sets the transition applied when content changes.
    /// </summary>
    public ContentTransition Transition { get; set; } = ContentTransition.CreateFade();

    /// <summary>
    /// Gets the effective opacity of the visible content during transition.
    /// Enter: 0→1, Exit: 1→0, Idle: 0 or 1.
    /// </summary>
    public double ContentOpacity =>
        _progress >= 1.0
            ? (_currentContent != null ? 1.0 : 0.0)
            : (_currentContent != null ? _progress : 1.0 - _progress);

    /// <summary>
    /// Gets or sets the content element. Changing this triggers the configured transition.
    /// </summary>
    public Element? Content
    {
        get => _currentContent;
        set
        {
            if (ReferenceEquals(_currentContent, value))
            {
                return;
            }

            // Finish any in-progress transition immediately.
            FinishTransition();

            var oldContent = _currentContent;
            _currentContent = value;

            if (_currentContent != null)
            {
                _currentContent.Parent = this;
            }

            if (oldContent != null || _currentContent != null)
            {
                // Animate: exit old (if any), enter new (if any).
                // Content→null = exit-only fade-out.
                // null→Content = enter-only fade-in.
                _oldContent = oldContent;
                StartTransition();
            }

            InvalidateMeasure();
        }
    }

    private void StartTransition()
    {
        var transition = Transition;
        if (transition.Kind == ContentTransitionKind.None || transition.Duration <= TimeSpan.Zero)
        {
            FinishTransition();
            return;
        }

        _progress = 0.0;

        if (transition.Delay > TimeSpan.Zero)
        {
            _delayClock = new AnimationClock(transition.Delay, Easing.Linear);
            _delayClock.Completed += OnDelayCompleted;
            _delayClock.Start();
        }
        else
        {
            StartAnimationClock(transition);
        }
    }

    private void OnDelayCompleted()
    {
        _delayClock = null;
        StartAnimationClock(Transition);
    }

    private void StartAnimationClock(ContentTransition transition)
    {
        _clock = new AnimationClock(transition.Duration, transition.Easing);
        _clock.Tick += OnTransitionTick;
        _clock.Completed += OnTransitionCompleted;
        _clock.Start();
    }

    private void OnTransitionTick(double progress)
    {
        _progress = progress;
        InvalidateVisual();
    }

    private void OnTransitionCompleted()
    {
        _clock = null;
        _progress = 1.0;

        if (_oldContent != null)
        {
            _oldContent.Parent = null;
            _oldContent = null;
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private void FinishTransition()
    {
        _delayClock?.Stop();
        _delayClock = null;
        _clock?.Stop();
        _clock = null;
        _progress = 1.0;

        if (_oldContent != null)
        {
            _oldContent.Parent = null;
            _oldContent = null;
        }
    }

    #region Layout

    protected override Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(Padding);
        var desired = Size.Empty;

        if (_currentContent != null)
        {
            _currentContent.Measure(inner);
            desired = _currentContent.DesiredSize;
        }

        // Old content needs measuring for rendering during transition.
        if (_oldContent != null)
        {
            _oldContent.Measure(inner);

            // Use the larger of old/new so neither gets clipped during transition.
            var oldSize = _oldContent.DesiredSize;
            desired = new Size(
                Math.Max(desired.Width, oldSize.Width),
                Math.Max(desired.Height, oldSize.Height));
        }

        return desired.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        _currentContent?.Arrange(inner);
        _oldContent?.Arrange(inner);
    }

    #endregion

    #region Rendering

    public override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        base.Render(context);

        var transition = Transition;
        double p = _progress;

        // No transition or completed — render current content only.
        if (p >= 1.0 || transition.Kind == ContentTransitionKind.None)
        {
            _currentContent?.Render(context);
            return;
        }

        bool needsClip = transition.Kind is ContentTransitionKind.Slide
                                           or ContentTransitionKind.Scale
                                           or ContentTransitionKind.Rotate;
        needsClip = false;
        if (needsClip)  
        {
            context.Save();
            context.SetClip(Bounds);
        }

        // Old content — exit animation
        if (_oldContent != null)
        {
            context.Save();
            context.TextPixelSnap = false;
            ApplyExitTransform(context, transition, p, Bounds);
            context.GlobalAlpha *= (float)(1.0 - p);
            _oldContent.Render(context);
            context.Restore();
        }

        // New content — enter animation
        if (_currentContent != null)
        {
            context.Save();
            context.TextPixelSnap = false;
            ApplyEnterTransform(context, transition, p, Bounds);
            context.GlobalAlpha = (float)p;
            _currentContent.Render(context);
            context.Restore();
        }

        if (needsClip)
        {
            context.Restore();
        }
    }

    private static void ApplyExitTransform(IGraphicsContext context, ContentTransition transition, double progress, Rect bounds)
    {
        switch (transition.Kind)
        {
            case ContentTransitionKind.Slide:
            {
                double offset = progress;
                switch (transition.Direction)
                {
                    case SlideDirection.Left:
                        context.Translate(-offset * bounds.Width, 0);
                        break;
                    case SlideDirection.Right:
                        context.Translate(offset * bounds.Width, 0);
                        break;
                    case SlideDirection.Up:
                        context.Translate(0, -offset * bounds.Height);
                        break;
                    case SlideDirection.Down:
                        context.Translate(0, offset * bounds.Height);
                        break;
                }
                break;
            }

            case ContentTransitionKind.Scale:
            {
                double scale = 1.0 - progress; // 1→0
                double cx = bounds.X + bounds.Width * 0.5;
                double cy = bounds.Y + bounds.Height * 0.5;
                context.Translate(cx, cy);
                context.Scale(scale, scale);
                context.Translate(-cx, -cy);
                break;
            }

            case ContentTransitionKind.Rotate:
            {
                double angle = progress * Math.PI * 0.5; // 0→90°
                double cx = bounds.X + bounds.Width * 0.5;
                double cy = bounds.Y + bounds.Height * 0.5;
                context.Translate(cx, cy);
                context.Rotate(angle);
                context.Scale(1.0 - progress, 1.0 - progress);
                context.Translate(-cx, -cy);
                break;
            }
        }
    }

    private static void ApplyEnterTransform(IGraphicsContext context, ContentTransition transition, double progress, Rect bounds)
    {
        switch (transition.Kind)
        {
            case ContentTransitionKind.Slide:
            {
                double offset = 1.0 - progress;
                switch (transition.Direction)
                {
                    case SlideDirection.Left:
                        context.Translate(offset * bounds.Width, 0);
                        break;
                    case SlideDirection.Right:
                        context.Translate(-offset * bounds.Width, 0);
                        break;
                    case SlideDirection.Up:
                        context.Translate(0, offset * bounds.Height);
                        break;
                    case SlideDirection.Down:
                        context.Translate(0, -offset * bounds.Height);
                        break;
                }
                break;
            }

            case ContentTransitionKind.Scale:
            {
                double scale = progress; // 0→1
                double cx = bounds.X + bounds.Width * 0.5;
                double cy = bounds.Y + bounds.Height * 0.5;
                context.Translate(cx, cy);
                context.Scale(scale, scale);
                context.Translate(-cx, -cy);
                break;
            }

            case ContentTransitionKind.Rotate:
            {
                double angle = -(1.0 - progress) * Math.PI * 0.5; // -90°→0
                double cx = bounds.X + bounds.Width * 0.5;
                double cy = bounds.Y + bounds.Height * 0.5;
                context.Translate(cx, cy);
                context.Rotate(angle);
                context.Scale(progress, progress);
                context.Translate(-cx, -cy);
                break;
            }
        }
    }

    #endregion

    #region HitTest

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        // Only current content is interactive; old content is fading out.
        if (_currentContent is UIElement uiContent)
        {
            var result = uiContent.HitTest(point);
            if (result != null)
            {
                return result;
            }
        }

        return Bounds.Contains(point) ? this : null;
    }

    #endregion

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (_oldContent != null && !visitor(_oldContent))
        {
            return false;
        }

        if (_currentContent != null && !visitor(_currentContent))
        {
            return false;
        }

        return true;
    }

    protected override void OnDispose()
    {
        FinishTransition();

        if (_currentContent != null)
        {
            _currentContent.Parent = null;
            _currentContent = null;
        }

        base.OnDispose();
    }
}
