using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// A semi-transparent accent highlight shown over the candidate drop zone during a drag (port of the golden
/// MewDock DropTargetIndicator). Lives in the window's <see cref="OverlayLayer"/>, hit-test invisible; the rect
/// is in window coordinates (<see cref="Model.DropInfo.Rect"/>). Fades in/out and morphs its bounds when the
/// zone changes, ticked off <see cref="AnimationClock"/> with the render loop.
/// </summary>
internal sealed class FlexDropTargetIndicator : FrameworkElement
{
    private static readonly TimeSpan MorphDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(120);

    private readonly OverlayLayer _overlayLayer;

    private Rect _targetRect;
    private Rect _currentRect;
    private Rect _morphStartRect;

    private readonly AnimationClock _morphClock;
    private readonly AnimationClock _fadeClock;

    private double _opacity;
    private double _fadeFrom;
    private double _fadeTo;
    private bool _visible;
    private bool _hasShown;
    private bool _dismissing;

    public FlexDropTargetIndicator(OverlayLayer overlayLayer)
    {
        _overlayLayer = overlayLayer;
        IsHitTestVisible = false;

        _morphClock = new AnimationClock(MorphDuration, Easing.EaseOutCubic) { TickCallback = OnMorphTick };
        _fadeClock = new AnimationClock(FadeDuration, Easing.EaseOutQuad)
        {
            TickCallback = OnFadeTick,
            CompletedCallback = OnFadeCompleted,
        };
    }

    /// <summary>Shows the indicator over <paramref name="area"/> (window coords), inset by <paramref name="margin"/>.</summary>
    public void HighlightArea(Rect area, double margin)
    {
        var rect = new Rect(
            area.X + margin,
            area.Y + margin,
            Math.Max(0, area.Width - margin * 2),
            Math.Max(0, area.Height - margin * 2));

        if (!_visible)
        {
            _visible = true;
            FadeTo(1.0);
        }

        if (!_hasShown)
        {
            _hasShown = true;
            _morphClock.Stop();
            _targetRect = rect;
            _currentRect = rect;
            InvalidateVisual();
            return;
        }

        if (_targetRect == rect)
        {
            return;
        }

        _targetRect = rect;
        _morphStartRect = _currentRect;
        _morphClock.Start();
    }

    /// <summary>Fades out but keeps the element so a later <see cref="HighlightArea"/> can re-show it.</summary>
    public void Hide()
    {
        if (!_visible)
        {
            return;
        }
        _visible = false;
        _hasShown = false;
        FadeTo(0.0);
    }

    /// <summary>Fades out and removes itself from the overlay when the fade completes (drag end).</summary>
    public void Dismiss()
    {
        _dismissing = true;
        _visible = false;
        FadeTo(0.0);
    }

    protected override Size MeasureOverride(Size availableSize) => Size.Empty;

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override UIElement? OnHitTest(Point point) => null;

    protected override void OnRender(IGraphicsContext context)
    {
        if (_opacity <= 0 || _currentRect.Width <= 0 || _currentRect.Height <= 0)
        {
            return;
        }

        var accent = Theme.Palette.Accent;
        var fill = accent.WithAlpha(80);

        if (_opacity < 1.0)
        {
            context.Save();
            context.GlobalAlpha *= (float)_opacity;
            context.FillRectangle(_currentRect, fill);
            context.DrawRectangle(_currentRect, accent, 2);
            context.Restore();
        }
        else
        {
            context.FillRectangle(_currentRect, fill);
            context.DrawRectangle(_currentRect, accent, 2);
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot is null)
        {
            _morphClock.Stop();
            _fadeClock.Stop();
        }
    }

    private void FadeTo(double targetOpacity)
    {
        _fadeFrom = _opacity;
        _fadeTo = targetOpacity;
        _fadeClock.Start();
    }

    private void OnFadeTick(double progress)
    {
        _opacity = _fadeFrom + (_fadeTo - _fadeFrom) * progress;
        InvalidateVisual();
    }

    private void OnFadeCompleted()
    {
        _opacity = _fadeTo;
        InvalidateVisual();
        if (_dismissing && _opacity <= 0)
        {
            _overlayLayer.Remove(this);
        }
    }

    private void OnMorphTick(double progress)
    {
        _currentRect = new Rect(
            _morphStartRect.X + (_targetRect.X - _morphStartRect.X) * progress,
            _morphStartRect.Y + (_targetRect.Y - _morphStartRect.Y) * progress,
            _morphStartRect.Width + (_targetRect.Width - _morphStartRect.Width) * progress,
            _morphStartRect.Height + (_targetRect.Height - _morphStartRect.Height) * progress);
        InvalidateVisual();
    }
}
