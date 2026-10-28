using System.Numerics;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A content control that animates transitions when its <see cref="Content"/> changes.
/// Supports fade and slide transitions with optional delay.
/// </summary>
public class TransitionContentControl : Control, IVisualTreeHost
{
    private readonly TransitionSlot _exitSlot;
    private readonly TransitionSlot _enterSlot;
    private AnimationClock? _delayClock;
    private AnimationClock? _clock;
    private double _progress = 1.0;
    // Starting alpha of the outgoing layer: 1.0 normally, but lower when a crossfade was interrupted so
    // the carried-out content continues from its current (partial) alpha instead of snapping to opaque.
    private double _oldContentAlpha = 1.0;

    public TransitionContentControl()
    {
        _exitSlot = new TransitionSlot();
        _enterSlot = new TransitionSlot();
        AttachChild(_exitSlot);
        AttachChild(_enterSlot);
    }

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
            ? (_enterSlot.Child != null ? 1.0 : 0.0)
            : (_enterSlot.Child != null ? _progress : 1.0 - _progress);

    /// <summary>Gets or sets the content element; changing it plays the configured transition.</summary>
    public Element? Content
    {
        get => _enterSlot.Child;
        set
        {
            if (ReferenceEquals(_enterSlot.Child, value))
            {
                return;
            }

            // If a crossfade is still running, the current content has only faded part-way in. Carry it out
            // from its current alpha rather than snapping it to fully opaque first - otherwise an interrupting
            // change (e.g. clicking to the next slide mid-fade) makes it flash fully visible and then fade
            // out. Any older outgoing layer is dropped (it was already on its way out).
            double carryAlpha = _progress < 1.0 ? _progress : 1.0;

            _delayClock?.Stop();
            _delayClock = null;
            _clock?.Stop();
            _clock = null;

            // At least one of old/new is non-null here (an identical no-op set returned early above), so
            // there is always something to animate: exit old (if any) from carryAlpha, enter new (if any).
            // Content→null = exit-only fade-out; null→Content = enter-only fade-in.
            var outgoing = _enterSlot.Child;
            _enterSlot.Child = null;
            _exitSlot.Child = outgoing;
            _enterSlot.Child = value;
            _oldContentAlpha = carryAlpha;
            StartTransition();

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
        ApplyProgress();

        if (transition.Delay > TimeSpan.Zero)
        {
            _delayClock = new AnimationClock(transition.Delay, Easing.Linear)
                .AttachTo(this);
            _delayClock.CompletedCallback = OnDelayCompleted;
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
        _clock = new AnimationClock(transition.Duration, transition.Easing)
            .AttachTo(this);
        _clock.TickCallback = OnTransitionTick;
        _clock.CompletedCallback = OnTransitionCompleted;
        _clock.Start();
    }

    private void OnTransitionTick(double progress)
    {
        _progress = progress;
        ApplyProgress();
    }

    /// <summary>
    /// Raised once a content change has finished playing, including the run out to nothing.
    /// </summary>
    /// <remarks>
    /// A host that tears itself down when it holds no content waits for this, or the exit is never
    /// seen.
    /// </remarks>
    public event Action? TransitionCompleted;

    private void OnTransitionCompleted()
    {
        _clock = null;
        _progress = 1.0;
        _oldContentAlpha = 1.0;

        bool hadOldContent = _exitSlot.Child != null;
        ApplyProgress();
        if (hadOldContent)
        {
            InvalidateMeasure();
        }

        TransitionCompleted?.Invoke();
    }

    private void FinishTransition()
    {
        _delayClock?.Stop();
        _delayClock = null;
        _clock?.Stop();
        _clock = null;
        _progress = 1.0;
        _oldContentAlpha = 1.0;
        ApplyProgress();
    }

    /// <summary>Puts the two slots where the progress has them: at rest only the entering content, upright and opaque.</summary>
    private void ApplyProgress()
    {
        var transition = Transition;
        if (_progress >= 1.0 || transition.Kind == ContentTransitionKind.None)
        {
            _exitSlot.Child = null;
            _exitSlot.Set(Matrix3x2.Identity, 1.0);
            _enterSlot.Set(Matrix3x2.Identity, 1.0);
            return;
        }

        var bounds = Bounds;
        _exitSlot.Set(ExitTransform(transition, _progress, bounds), _oldContentAlpha * (1.0 - _progress));
        _enterSlot.Set(EnterTransform(transition, _progress, bounds), _progress);
    }

    #region Layout

    protected override Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(Padding);
        _enterSlot.Measure(inner);
        var desired = _enterSlot.DesiredSize;

        // Old content needs measuring for rendering during transition.
        if (_exitSlot.Child != null)
        {
            _exitSlot.Measure(inner);

            // Use the larger of old/new so neither gets clipped during transition.
            var oldSize = _exitSlot.DesiredSize;
            desired = new Size(
                Math.Max(desired.Width, oldSize.Width),
                Math.Max(desired.Height, oldSize.Height));
        }

        return desired.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        _enterSlot.Arrange(inner);
        // An empty slot is laid out without area, so it is neither drawn nor hit.
        _exitSlot.Arrange(_exitSlot.Child != null ? inner : Rect.Empty);
        if (_progress < 1.0)
        {
            // The slide and scale transforms are taken about the bounds, which the arrange may have moved.
            ApplyProgress();
        }
    }

    #endregion

    #region Transforms

    private static Matrix3x2 ExitTransform(ContentTransition transition, double progress, Rect bounds)
    {
        switch (transition.Kind)
        {
            case ContentTransitionKind.Slide:
                return SlideTransform(transition.Direction, progress, bounds);
            case ContentTransitionKind.Scale:
                return AboutCenter(Matrix3x2.CreateScale((float)(1.0 - progress)), bounds);
            case ContentTransitionKind.Rotate:
            {
                float angle = (float)(progress * Math.PI * 0.5);
                float scale = (float)(1.0 - progress);
                return AboutCenter(Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateScale(scale), bounds);
            }
            default:
                return Matrix3x2.Identity;
        }
    }

    private static Matrix3x2 EnterTransform(ContentTransition transition, double progress, Rect bounds)
    {
        switch (transition.Kind)
        {
            case ContentTransitionKind.Slide:
                return SlideTransform(transition.Direction, -(1.0 - progress), bounds);
            case ContentTransitionKind.Scale:
                return AboutCenter(Matrix3x2.CreateScale((float)progress), bounds);
            case ContentTransitionKind.Rotate:
            {
                float angle = (float)(-(1.0 - progress) * Math.PI * 0.5);
                return AboutCenter(Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateScale((float)progress), bounds);
            }
            default:
                return Matrix3x2.Identity;
        }
    }

    /// <summary>The slide offset for <paramref name="fraction"/> of the bounds along the direction; negative moves against it.</summary>
    private static Matrix3x2 SlideTransform(SlideDirection direction, double fraction, Rect bounds)
    {
        return direction switch
        {
            SlideDirection.Left => Matrix3x2.CreateTranslation((float)(-fraction * bounds.Width), 0),
            SlideDirection.Right => Matrix3x2.CreateTranslation((float)(fraction * bounds.Width), 0),
            SlideDirection.Up => Matrix3x2.CreateTranslation(0, (float)(-fraction * bounds.Height)),
            SlideDirection.Down => Matrix3x2.CreateTranslation(0, (float)(fraction * bounds.Height)),
            _ => Matrix3x2.Identity,
        };
    }

    private static Matrix3x2 AboutCenter(Matrix3x2 transform, Rect bounds)
        => TransformHost.About(transform, bounds.X + bounds.Width * 0.5, bounds.Y + bounds.Height * 0.5);

    #endregion

    #region HitTest

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // Only current content is interactive; old content is fading out.
        if (_enterSlot.Child is UIElement uiContent)
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
        => visitor(_exitSlot) && visitor(_enterSlot);

    protected override void OnDispose()
    {
        FinishTransition();
        _enterSlot.Child = null;
        base.OnDispose();
    }

    /// <summary>
    /// Holds one content under the transform and alpha of a running transition. Both are framework data, so
    /// the content stays a scene node of its own while it animates.
    /// </summary>
    private sealed class TransitionSlot : FrameworkElement, IVisualTreeHost
    {
        private Element? _child;
        private Matrix3x2 _transform = Matrix3x2.Identity;
        private double _alpha = 1.0;

        internal Element? Child
        {
            get => _child;
            set
            {
                if (ReferenceEquals(_child, value))
                {
                    return;
                }

                if (_child != null)
                {
                    DetachChild(_child);
                }

                _child = value;
                if (_child != null)
                {
                    // Content handed over from another host moves here, as it did before the slots existed.
                    _child.Parent = this;
                }

                InvalidateMeasure();
            }
        }

        internal void Set(Matrix3x2 transform, double alpha)
        {
            alpha = Math.Clamp(alpha, 0, 1);
            if (_transform != transform || _alpha != alpha)
            {
                _transform = transform;
                _alpha = alpha;
                InvalidateVisual();
            }
        }

        protected override Size MeasureContent(Size availableSize)
        {
            if (_child == null)
            {
                return Size.Empty;
            }

            _child.Measure(availableSize);
            return _child.DesiredSize;
        }

        protected override void ArrangeContent(Rect bounds) => _child?.Arrange(bounds);

        protected override bool TryGetChildTransform(out Matrix3x2 transform)
        {
            transform = _transform;
            return !_transform.IsIdentity;
        }

        // Each call fades on its own, as the transition drew before it had slots; a group would blend the content once.
        internal override double ChildAlpha => _alpha;

        // The content moves between pixels every tick; snapping its glyphs would make them jitter.
        internal override bool KeepsTextPixelSnapUnderChildTransform(Matrix3x2 transform) => false;

        protected override UIElement? OnHitTest(Point point)
            => TransformHost.HitTest(this, _child as UIElement, _transform, point);

        bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
            => _child == null || visitor(_child);
    }
}
