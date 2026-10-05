using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A circular progress control: an arc that fills clockwise from the top for a completion percentage.
/// </summary>
public sealed partial class ProgressRing : RangeBase
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ProgressRing>(DefaultStyles.CreateProgressRingStyle);

    public static readonly MewProperty<bool> IsIndeterminateProperty =
        MewProperty<bool>.Register<ProgressRing>(
            nameof(IsIndeterminate),
            false,
            MewPropertyOptions.AffectsRender,
            static (self, _, isIndeterminate) => self.OnIsIndeterminateChanged(isIndeterminate));

    // Stroke thickness relative to the ring's side length.
    private const double THICKNESS_RATIO = 0.125;
    private const double MIN_THICKNESS = 2;

    // One indeterminate cycle: the arc grows from a dot to half the ring while it turns 450 degrees,
    // then shrinks back to a dot over another 630 degrees, so it lands where it started.
    private const double CYCLE_DURATION_MS = 2000;
    private const double GROW_TURN_DEGREES = 450;
    private const double SHRINK_TURN_DEGREES = 630;
    private const double LONGEST_ARC_DEGREES = 180;

    // Short enough to read as a dot, long enough for every backend to draw its round caps.
    private const double SHORTEST_ARC_DEGREES = 0.1;

    private static readonly StrokeStyle _roundedStroke = new() { LineCap = StrokeLineCap.Round };

    private AnimationClock? _clock;

    static ProgressRing()
    {
        MaximumProperty.OverrideDefaultValue<ProgressRing>(100.0);
    }

    /// <summary>
    /// Gets or sets whether the ring shows a repeating spin animation instead of <see cref="RangeBase.Value"/>.
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
        _clock = new AnimationClock(TimeSpan.FromMilliseconds(CYCLE_DURATION_MS), Easing.Linear)
            .AttachTo(this);
        _clock.RepeatCount = -1;
        _clock.TickCallback = _ => InvalidateVisual();
        _clock.Start();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double side = Theme.Metrics.BaseControlHeight;
        return new Size(side, side).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var contentBounds = Bounds.Deflate(Padding);
        double side = Math.Min(contentBounds.Width, contentBounds.Height);
        if (side <= 0)
        {
            return;
        }

        double thickness = Math.Min(side / 2, Math.Max(MIN_THICKNESS, side * THICKNESS_RATIO));
        double radius = (side - thickness) / 2;
        double centerX = contentBounds.X + contentBounds.Width / 2;
        double centerY = contentBounds.Y + contentBounds.Height / 2;

        var track = GetValue(BackgroundProperty);
        if (track.A > 0)
        {
            context.DrawEllipse(new Rect(centerX - radius, centerY - radius, radius * 2, radius * 2), track, thickness);
        }

        double startDegrees;
        double sweepDegrees;
        if (IsIndeterminate && _clock != null)
        {
            (startDegrees, sweepDegrees) = GetIndeterminateArc(_clock.RawProgress);
        }
        else
        {
            startDegrees = -90;
            sweepDegrees = 360 * GetNormalizedValue();
        }

        if (sweepDegrees <= 0)
        {
            return;
        }

        var indicator = IsEffectivelyEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
        if (sweepDegrees >= 360)
        {
            context.DrawEllipse(new Rect(centerX - radius, centerY - radius, radius * 2, radius * 2), indicator, thickness);
        }
        else
        {
            double startRadians = startDegrees * (Math.PI / 180.0);
            double endRadians = (startDegrees + sweepDegrees) * (Math.PI / 180.0);
            var arc = new PathGeometry();
            arc.MoveTo(centerX + radius * Math.Cos(startRadians), centerY + radius * Math.Sin(startRadians));
            arc.Arc(centerX, centerY, radius, radius, startRadians, endRadians);
            context.DrawPath(arc, new Pen(indicator, thickness, _roundedStroke));
        }
    }

    internal static (double StartDegrees, double SweepDegrees) GetIndeterminateArc(double progress)
    {
        double start;
        double sweep;
        if (progress < 0.5)
        {
            double phase = progress / 0.5;
            start = GROW_TURN_DEGREES * phase;
            sweep = LONGEST_ARC_DEGREES * phase;
        }
        else
        {
            double phase = (progress - 0.5) / 0.5;
            start = GROW_TURN_DEGREES + SHRINK_TURN_DEGREES * phase;
            sweep = LONGEST_ARC_DEGREES * (1 - phase);
        }

        return (start, Math.Max(SHORTEST_ARC_DEGREES, sweep));
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
