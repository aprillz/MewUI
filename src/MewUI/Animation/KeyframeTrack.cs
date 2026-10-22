namespace Aprillz.MewUI.Animation;

/// <summary>
/// A single keyframe with a time, value, and optional per-segment easing.
/// </summary>
public readonly struct Keyframe<T>
{
    /// <summary>
    /// Gets or sets the time of this keyframe in milliseconds from the start of the track.
    /// </summary>
    public required double Time { get; init; }

    /// <summary>
    /// Gets or sets the value at this keyframe.
    /// </summary>
    public required T Value { get; init; }

    /// <summary>
    /// Gets or sets the easing applied from this keyframe to the next; null means linear.
    /// </summary>
    public Func<double, double>? Easing { get; init; }
}

/// <summary>
/// Evaluates interpolated values across a sorted sequence of keyframes, each segment with its own optional easing.
/// </summary>
public sealed class KeyframeTrack<T>
{
    private readonly Keyframe<T>[] _keyframes;
    private readonly LerpFunc<T> _lerp;

    /// <summary>
    /// Initializes a track from at least two keyframes, sorted by time.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer than two keyframes were given.</exception>
    public KeyframeTrack(LerpFunc<T> lerp, params Keyframe<T>[] keyframes)
    {
        ArgumentNullException.ThrowIfNull(lerp);

        if (keyframes.Length < 2)
        {
            throw new ArgumentException("At least 2 keyframes are required.", nameof(keyframes));
        }

        _lerp = lerp;
        _keyframes = new Keyframe<T>[keyframes.Length];
        Array.Copy(keyframes, _keyframes, keyframes.Length);
        Array.Sort(_keyframes, (a, b) => a.Time.CompareTo(b.Time));

        TotalDuration = _keyframes[^1].Time;
    }

    /// <summary>
    /// Gets the time of the last keyframe in milliseconds.
    /// </summary>
    public double TotalDuration { get; }

    /// <summary>
    /// Evaluates the interpolated value at the given time in milliseconds; times before the first or after the last keyframe return that keyframe's value.
    /// </summary>
    public T Evaluate(double timeMs)
    {
        var kf = _keyframes;

        // Before first keyframe
        if (timeMs <= kf[0].Time)
        {
            return kf[0].Value;
        }

        // After last keyframe
        if (timeMs >= kf[^1].Time)
        {
            return kf[^1].Value;
        }

        // Find segment: binary search for the last keyframe with Time <= timeMs
        int lo = 0, hi = kf.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (kf[mid].Time <= timeMs)
                lo = mid;
            else
                hi = mid - 1;
        }

        ref readonly var from = ref kf[lo];
        ref readonly var to = ref kf[lo + 1];

        double segmentDuration = to.Time - from.Time;
        if (segmentDuration <= 0)
        {
            return to.Value;
        }

        double localT = (timeMs - from.Time) / segmentDuration;
        double easedT = from.Easing != null ? from.Easing(localT) : localT;

        return _lerp(from.Value, to.Value, easedT);
    }
}

/// <summary>
/// A keyframe track that holds the most recent keyframe value without interpolation, for discrete transitions such as on/off opacity.
/// </summary>
public sealed class DiscreteKeyframeTrack<T>
{
    private readonly (double Time, T Value)[] _keyframes;

    /// <summary>
    /// Initializes a track from at least one (time, value) pair, sorted by time.
    /// </summary>
    /// <exception cref="ArgumentException">No keyframes were given.</exception>
    public DiscreteKeyframeTrack(params (double Time, T Value)[] keyframes)
    {
        if (keyframes.Length < 1)
        {
            throw new ArgumentException("At least 1 keyframe is required.", nameof(keyframes));
        }

        _keyframes = new (double, T)[keyframes.Length];
        Array.Copy(keyframes, _keyframes, keyframes.Length);
        Array.Sort(_keyframes, (a, b) => a.Time.CompareTo(b.Time));

        TotalDuration = _keyframes[^1].Time;
    }

    /// <summary>
    /// Gets the time of the last keyframe in milliseconds.
    /// </summary>
    public double TotalDuration { get; }

    /// <summary>
    /// Returns the value of the most recent keyframe at or before the given time in milliseconds; times before the first keyframe return its value.
    /// </summary>
    public T Evaluate(double timeMs)
    {
        var kf = _keyframes;

        if (timeMs <= kf[0].Time)
        {
            return kf[0].Value;
        }

        if (timeMs >= kf[^1].Time)
        {
            return kf[^1].Value;
        }

        // Binary search for the last keyframe with Time <= timeMs
        int lo = 0, hi = kf.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (kf[mid].Time <= timeMs)
                lo = mid;
            else
                hi = mid - 1;
        }

        return kf[lo].Value;
    }
}
