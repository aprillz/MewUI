using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// An indeterminate progress ring that displays orbiting dots,
/// matching the WPF/WinUI ProgressRing animation style.
/// </summary>
public class ProgressRing : Control
{
    private AnimationClock? _clock;
    private bool _isActive;

    private const int DotCount = 6;
    private const double DotRadiusRatio = 0.06; // dot radius relative to ring side length
    private const double DotAnimationDurationMs = 3470; // per-dot animation duration
    private const double BeginTimeStepMs = 167;
    // Total storyboard cycle = last dot's BeginTime + animation duration
    private const double StoryboardDurationMs = BeginTimeStepMs * (DotCount - 1) + DotAnimationDurationMs; // 4305
    private const double AngleStepPerDot = -6; // degrees offset per dot index

    // WPF SplineDoubleKeyFrame: KeySpline is on the DESTINATION keyframe (easing INTO it).
    // Our Keyframe<T>.Easing is on the SOURCE keyframe (easing OUT of it).
    // So WPF KF[i+1].KeySpline → Our KF[i].Easing.
    private static readonly KeyframeTrack<double> AngleTrack = new(Lerp.Double,
        new Keyframe<double> { Time = 0, Value = -110, Easing = Easing.CubicBezier(0.02, 0.33, 0.38, 0.77) },
        new Keyframe<double> { Time = 433, Value = 10 },
        new Keyframe<double> { Time = 1200, Value = 93, Easing = Easing.CubicBezier(0.57, 0.17, 0.95, 0.75) },
        new Keyframe<double> { Time = 1617, Value = 205, Easing = Easing.CubicBezier(0, 0.19, 0.07, 0.72) },
        new Keyframe<double> { Time = 2017, Value = 357 },
        new Keyframe<double> { Time = 2783, Value = 439, Easing = Easing.CubicBezier(0, 0, 0.95, 0.37) },
        new Keyframe<double> { Time = 3217, Value = 585 }
    );

    private static readonly DiscreteKeyframeTrack<double> OpacityTrack = new(
        (0, 1.0),
        (3210, 1.0),
        (3220, 0.0),
        (3470, 0.0)
    );

    /// <summary>
    /// Gets or sets whether the ring animation is active.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;

            if (value)
            {
                _clock = new AnimationClock(TimeSpan.FromMilliseconds(StoryboardDurationMs), Easing.Linear)
                {
                    RepeatCount = -1,
                };
                _clock.Tick += OnAnimationTick;
                _clock.Start();
            }
            else
            {
                _clock?.Stop();
                _clock = null;
                InvalidateVisual();
            }
        }
    }

    private void OnAnimationTick(double _)
    {
        InvalidateVisual();
    }

    public override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        base.Render(context);

        if (!_isActive || _clock == null)
        {
            return;
        }

        // Global storyboard elapsed time within current cycle
        double globalMs = _clock.RawProgress * StoryboardDurationMs;
        var bounds = Bounds;
        double side = Math.Min(bounds.Width, bounds.Height);
        if (side <= 0)
        {
            return;
        }

        double dotRadius = side * DotRadiusRatio;
        double cx = bounds.X + bounds.Width * 0.5;
        double cy = bounds.Y + bounds.Height * 0.5;
        double ringRadius = side * 0.5 - dotRadius * 2;
        if (ringRadius <= 0)
        {
            return;
        }

        var dotColor = Foreground;
        double dotDiameter = dotRadius * 2;

        for (int i = 0; i < DotCount; i++)
        {
            // Each dot's local time relative to its BeginTime
            double localMs = globalMs - i * BeginTimeStepMs;

            // Before this dot's BeginTime or after its animation ends:
            // WPF base opacity=0 (from style), so dot is invisible.
            if (localMs < 0 || localMs >= DotAnimationDurationMs)
            {
                continue;
            }

            double opacity = OpacityTrack.Evaluate(localMs);
            if (opacity <= 0)
            {
                continue;
            }

            double angleTimeMs = Math.Min(localMs, AngleTrack.TotalDuration);
            double angleDeg = AngleTrack.Evaluate(angleTimeMs) + i * AngleStepPerDot;
            // WPF RotateTransform: 0° = top (12 o'clock), clockwise.
            // cos/sin: 0° = right (3 o'clock). Subtract 90° to align.
            double angleRad = (angleDeg - 90.0) * (Math.PI / 180.0);

            double x = cx + ringRadius * Math.Cos(angleRad);
            double y = cy + ringRadius * Math.Sin(angleRad);
            var ellipseRect = new Rect(x - dotRadius, y - dotRadius, dotDiameter, dotDiameter);

            if (opacity < 1.0)
            {
                context.Save();
                context.GlobalAlpha *= (float)opacity;
                context.FillEllipse(ellipseRect, dotColor);
                context.Restore();
            }
            else
            {
                context.FillEllipse(ellipseRect, dotColor);
            }
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        if (newRoot == null)
        {
            // Detached from visual tree — stop clock to prevent AnimationManager leak.
            _clock?.Stop();
            _clock = null;
        }
        else if (_isActive && _clock == null)
        {
            // Re-attached while still active — restart.
            _clock = new AnimationClock(TimeSpan.FromMilliseconds(StoryboardDurationMs), Easing.Linear)
            {
                RepeatCount = -1,
            };
            _clock.Tick += OnAnimationTick;
            _clock.Start();
        }
    }

    protected override void OnDispose()
    {
        _clock?.Stop();
        _clock = null;
        base.OnDispose();
    }
}
