using System.Diagnostics;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Animation;

/// <summary>
/// Manages normalized animation progress over time with easing.
/// </summary>
public sealed class AnimationClock
{
    private WeakReference<Element>? _owner;
    private long _startTimestamp;

    // Set by Start, cleared by the first Update that ticks this clock.
    private bool _awaitingFirstUpdate;
    private long _pauseTimestamp;
    private long _pauseAccumulated;
    private bool _isRunning;
    private bool _isPaused;
    private bool _isReversing;      // current direction in AutoReverse
    private int _currentIteration;
    private double _progress;
    private double _rawProgress;

    public AnimationClock(TimeSpan duration, Func<double, double>? easing = null)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be greater than zero.");
        }

        Duration = duration;
        EasingFunction = easing ?? Easing.Default;
    }

    /// <summary>
    /// Gets or sets the animation duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the easing function applied to the raw progress.
    /// </summary>
    public Func<double, double> EasingFunction { get; set; }

    /// <summary>
    /// Gets or sets the repeat count.
    /// 0 = play once, 1 = play twice, -1 = infinite.
    /// </summary>
    public int RepeatCount { get; set; }

    /// <summary>
    /// Gets or sets whether the animation reverses direction on each repeat.
    /// </summary>
    public bool AutoReverse { get; set; }

    /// <summary>
    /// Gets whether the clock is currently running (not stopped).
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets whether the clock is paused.
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Gets the current eased progress value in [0, 1].
    /// </summary>
    public double Progress => _progress;

    /// <summary>
    /// Gets the current raw (linear) progress value in [0, 1].
    /// </summary>
    public double RawProgress => _rawProgress;

    /// <summary>
    /// Called each frame with the eased progress value.
    /// Single-owner callback - no multicast, no cleanup needed.
    /// </summary>
    public Action<double>? TickCallback { get; set; }

    /// <summary>
    /// Called when the animation completes (after all repeats).
    /// Single-owner callback - no multicast, no cleanup needed.
    /// </summary>
    public Action? CompletedCallback { get; set; }

    /// <summary>
    /// Associates this clock with the element whose visual surface consumes its frames. Framework-owned
    /// clocks use this to keep animation render demand local to one window. Public clocks that are not
    /// attached retain the legacy application-wide render demand.
    /// </summary>
    internal AnimationClock AttachTo(Element owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = new WeakReference<Element>(owner);
        return this;
    }

    internal bool HasOwner => _owner != null;

    internal Window? ResolveOwnerWindow()
    {
        if (_owner == null || !_owner.TryGetTarget(out var owner))
        {
            return null;
        }

        // Native popups use a portal: their subtree remains rooted in the owner window, while a
        // PopupWindow owns the actual render surface. ResolveInputHostWindow already follows that
        // hosted-surface boundary and falls back to the ordinary visual root.
        return owner.ResolveInputHostWindow();
    }

    /// <summary>
    /// Starts the animation from the beginning.
    /// </summary>
    public void Start()
    {
        _startTimestamp = Stopwatch.GetTimestamp();
        _awaitingFirstUpdate = true;
        _pauseAccumulated = 0;
        _isReversing = false;
        _currentIteration = 0;
        bool wasRunning = _isRunning;
        _isRunning = true;
        _isPaused = false;
        _rawProgress = 0;
        _progress = 0;

        // Restarting a still-running clock only resets its timing - re-registering would add a second entry to the
        // manager's active list (a List, not a set), so a later single Unregister would leave it registered and pin
        // the render loop in Continuous mode forever.
        if (!wasRunning)
        {
            AnimationManager.Instance.Register(this);
        }
    }

    /// <summary>
    /// Stops the animation and resets progress.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _isPaused = false;
        AnimationManager.Instance.Unregister(this);
    }

    /// <summary>
    /// Pauses the animation at its current progress.
    /// </summary>
    public void Pause()
    {
        if (!_isRunning || _isPaused)
        {
            return;
        }

        _isPaused = true;
        _pauseTimestamp = Stopwatch.GetTimestamp();
        AnimationManager.Instance.OnPauseStateChanged();
    }

    /// <summary>
    /// Resumes a paused animation.
    /// </summary>
    public void Resume()
    {
        if (!_isRunning || !_isPaused)
        {
            return;
        }

        _isPaused = false;
        _pauseAccumulated += Stopwatch.GetTimestamp() - _pauseTimestamp;
        AnimationManager.Instance.OnPauseStateChanged();
    }

    /// <summary>
    /// Called by <see cref="AnimationManager"/> each frame.
    /// </summary>
    internal void Update(long currentTimestamp)
    {
        if (!_isRunning || _isPaused)
        {
            return;
        }

        if (_awaitingFirstUpdate)
        {
            // Start can precede the first pulse by an unbounded gap (a clock started before the
            // application ran, or while its window was hidden). Timing from Start would report that gap
            // as elapsed and skip a short animation outright, so the run begins at the first pulse.
            _awaitingFirstUpdate = false;
            _startTimestamp = currentTimestamp;
            _pauseAccumulated = 0;
        }

        double durationMs = Duration.TotalMilliseconds;
        double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp + _pauseAccumulated, currentTimestamp).TotalMilliseconds;

        // Compute iteration and linear progress within current iteration
        double iterationProgress = elapsedMs / durationMs;

        if (iterationProgress >= 1.0)
        {
            int completedIterations = (int)iterationProgress;

            if (RepeatCount >= 0 && _currentIteration + completedIterations > RepeatCount)
            {
                // Animation complete
                _rawProgress = 1.0;
                _isReversing = AutoReverse && (RepeatCount % 2 != 0);
                double directed = _isReversing ? 1.0 - _rawProgress : _rawProgress;
                _progress = EasingFunction(directed);

                _isRunning = false;
                AnimationManager.Instance.Unregister(this);

                TickCallback?.Invoke(_progress);
                CompletedCallback?.Invoke();
                return;
            }

            // Advance iterations
            _currentIteration += completedIterations;
            iterationProgress -= completedIterations;

            // Reset start time for next iteration
            long iterationTicks = (long)(completedIterations * durationMs * Stopwatch.Frequency / 1000.0);
            _startTimestamp += iterationTicks;

            if (AutoReverse)
            {
                _isReversing = (_currentIteration % 2) != 0;
            }
        }

        _rawProgress = Math.Clamp(iterationProgress, 0, 1);
        double directedProgress = _isReversing ? 1.0 - _rawProgress : _rawProgress;
        _progress = EasingFunction(directedProgress);

        TickCallback?.Invoke(_progress);
    }
}
