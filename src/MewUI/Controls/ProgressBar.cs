using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A progress bar control for displaying completion percentage.
/// </summary>
public sealed partial class ProgressBar : RangeBase
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ProgressBar>(DefaultStyles.CreateProgressBarStyle);

    public static readonly MewProperty<bool> IsIndeterminateProperty =
        MewProperty<bool>.Register<ProgressBar>(
            nameof(IsIndeterminate),
            false,
            MewPropertyOptions.AffectsRender,
            static (self, _, isIndeterminate) => self.OnIsIndeterminateChanged(isIndeterminate));

    // Indicator block width relative to the track in indeterminate mode.
    private const double INDICATOR_RATIO = 0.3;
    private const double CYCLE_DURATION_MS = 2000;

    private AnimationClock? _clock;

    static ProgressBar()
    {
        MaximumProperty.OverrideDefaultValue<ProgressBar>(100.0);
    }

    /// <summary>
    /// Gets or sets whether the bar shows a repeating sweep animation instead of <see cref="RangeBase.Value"/>.
    /// </summary>
    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    private void OnIsIndeterminateChanged(bool isIndeterminate)
    {
        if (isIndeterminate)
        {
            StartClock();
        }
        else
        {
            _clock?.Stop();
            _clock = null;
        }
    }

    private void StartClock()
    {
        _clock = new AnimationClock(TimeSpan.FromMilliseconds(CYCLE_DURATION_MS), Easing.EaseInOutQuad)
            .AttachTo(this);
        _clock.RepeatCount = -1;
        _clock.TickCallback = _ => InvalidateVisual();
        _clock.Start();
    }

    protected override Size MeasureContent(Size availableSize) => new Size(120, Height);

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(borderInset));
        double radius = Math.Min(bounds.Height / 2, CornerRadius);

        var bg = GetValue(BackgroundProperty);
        var border = GetValue(BorderBrushProperty);
        DrawBackgroundAndBorder(context, bounds, bg, border, BorderThickness, radius);

        Rect fillRect;
        if (IsIndeterminate && _clock != null)
        {
            fillRect = GetIndeterminateFillRect(contentBounds);
        }
        else
        {
            fillRect = new Rect(contentBounds.X, contentBounds.Y, contentBounds.Width * GetNormalizedValue(), contentBounds.Height);
        }

        if (fillRect.Width > 0)
        {
            var fillColor = IsEffectivelyEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
            if (radius - 1 > 0)
            {
                double rx = Math.Min(radius - 1, fillRect.Height / 2.0);
                context.FillRoundedRectangle(fillRect, rx, rx, fillColor);
            }
            else
            {
                context.FillRectangle(fillRect, fillColor);
            }
        }
    }

    private Rect GetIndeterminateFillRect(Rect contentBounds)
    {
        double blockWidth = contentBounds.Width * INDICATOR_RATIO;

        // The block travels from fully off-track left to fully off-track right; clipping to the
        // track makes it appear to grow in at the left edge and shrink out at the right edge.
        double travel = _clock!.Progress * (contentBounds.Width + blockWidth) - blockWidth;
        double left = Math.Max(contentBounds.X, contentBounds.X + travel);
        double right = Math.Min(contentBounds.Right, contentBounds.X + travel + blockWidth);

        return new Rect(left, contentBounds.Y, Math.Max(0, right - left), contentBounds.Height);
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot == null)
        {
            // Detached from visual tree - stop clock to prevent AnimationManager leak.
            _clock?.Stop();
            _clock = null;
        }
        else if (IsIndeterminate && _clock == null)
        {
            StartClock();
        }
    }

    protected override void OnDispose()
    {
        _clock?.Stop();
        _clock = null;
        base.OnDispose();
    }
}
