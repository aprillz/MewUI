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

    /// <summary>
    /// The optional track part of a template; its arranged width is the full length of the bar.
    /// </summary>
    public const string PART_TRACK = "PART_Track";

    /// <summary>
    /// The optional indicator part of a template; after the template arranges it, the control arranges it
    /// again to the horizontal span of the value or of the indeterminate sweep, keeping its vertical placement.
    /// </summary>
    public const string PART_INDICATOR = "PART_Indicator";

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
    private Element? _lengthPart;
    private Element? _indicatorPart;

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
        _clock.TickCallback = _ => OnClockTick();
        _clock.Start();
    }

    private void OnClockTick()
    {
        if (_indicatorPart != null)
        {
            InvalidateArrange();
        }
        else
        {
            InvalidateVisual();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

        return new Size(120, Height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        ArrangeIndicatorPart();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // A template owns the control's entire visuals; the built-in bar would double-render.
        if (HasTemplateInstance)
        {
            return;
        }

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
            var fillColor = Foreground;
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

    private protected override void OnTemplateInstanceAttached()
    {
        base.OnTemplateInstanceAttached();

        _indicatorPart = GetTemplateChild<Element>(PART_INDICATOR);
        // The bar length is the track part, or the whole template when there is none.
        _lengthPart = GetTemplateChild<Element>(PART_TRACK) ?? TemplateVisualRoot;
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        _indicatorPart = null;
        _lengthPart = null;
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        if (_indicatorPart != null
            && (property == ValueProperty || property == MinimumProperty || property == MaximumProperty || property == IsIndeterminateProperty))
        {
            InvalidateArrange();
        }
    }

    private void ArrangeIndicatorPart()
    {
        var indicator = _indicatorPart;
        var lengthPart = _lengthPart;
        if (indicator == null || lengthPart == null)
        {
            return;
        }

        // The template's own arrange settled the vertical placement; only the horizontal span is the value's.
        var track = lengthPart.Bounds;
        var slot = indicator.Bounds;
        Rect span;
        if (IsIndeterminate && _clock != null)
        {
            span = GetIndeterminateFillRect(track);
        }
        else
        {
            span = new Rect(track.X, track.Y, track.Width * GetNormalizedValue(), track.Height);
        }

        indicator.Arrange(new Rect(span.X, slot.Y, span.Width, slot.Height));
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
