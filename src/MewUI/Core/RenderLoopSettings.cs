namespace Aprillz.MewUI;

/// <summary>
/// Controls the application's render loop scheduling. Continuous rendering has exactly two inputs: the user
/// <see cref="Continuous"/> flag, and active animations (driven internally by the animation system). The platform
/// host reads <see cref="IsContinuous"/> - the OR of those (plus VSync-off) - each frame; nothing sets the loop mode
/// directly.
/// </summary>
public sealed class RenderLoopSettings
{
    private int _continuous;
    private int _animationActive;
    private int _targetFps;
    private int _vsyncEnabled = 1;

    internal RenderLoopSettings()
    {
    }

    /// <summary>
    /// User flag: force the render loop to run continuously. This is the only direct way to request continuous
    /// rendering; the animation system requests it on its own (see <see cref="IsContinuous"/>) while clocks run.
    /// </summary>
    public bool Continuous
    {
        get => Volatile.Read(ref _continuous) != 0;
        set => Interlocked.Exchange(ref _continuous, value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets the target FPS. A value of 0 indicates no cap.
    /// </summary>
    public int TargetFps
    {
        get => Volatile.Read(ref _targetFps);
        set => Interlocked.Exchange(ref _targetFps, value);
    }

    /// <summary>
    /// Gets or sets whether VSync is enabled (when supported by the backend/platform).
    /// </summary>
    public bool VSyncEnabled
    {
        get => Volatile.Read(ref _vsyncEnabled) != 0;
        set => Interlocked.Exchange(ref _vsyncEnabled, value ? 1 : 0);
    }

    /// <summary>
    /// Driven by the animation system: true while one or more animation clocks are active. Not a user knob - set the
    /// <see cref="Continuous"/> flag to force continuous rendering yourself.
    /// </summary>
    internal bool AnimationActive
    {
        get => Volatile.Read(ref _animationActive) != 0;
        set => Interlocked.Exchange(ref _animationActive, value ? 1 : 0);
    }

    /// <summary>
    /// True when the render loop should run continuously: VSync is off, the user <see cref="Continuous"/> flag is set,
    /// or animations are active. The single value the platform host reads each frame.
    /// </summary>
    public bool IsContinuous => !VSyncEnabled || Continuous || AnimationActive;

    /// <summary>Convenience toggle for the user <see cref="Continuous"/> flag.</summary>
    public void SetContinuous(bool enabled) => Continuous = enabled;
}
