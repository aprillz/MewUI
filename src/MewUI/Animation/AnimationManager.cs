using System.Diagnostics;

namespace Aprillz.MewUI.Animation;

/// <summary>
/// Drives all active <see cref="AnimationClock"/> instances synchronized with the render loop. While any clock is
/// active it sets <see cref="RenderLoopSettings.AnimationActive"/> so the platform host renders every frame, and
/// clears it when idle. It never touches the user's <see cref="RenderLoopSettings.Continuous"/> flag.
/// </summary>
public sealed class AnimationManager
{
    private static AnimationManager? _instance;

    // Clocks start/stop from any UI thread (per-window render loops, tests), so every
    // list mutation and the update sweep run under _sync. Same-thread reentrancy from
    // tick callbacks (a tick starting/stopping another clock) is handled by the
    // _isUpdating deferral, and the monitor is reentrant for those nested calls.
    private readonly object _sync = new();
    private readonly List<AnimationClock> _active = new();
    private readonly List<AnimationClock> _pendingAdd = new();
    private readonly List<AnimationClock> _pendingRemove = new();
    private bool _isUpdating;

    internal AnimationManager() { }

    /// <summary>
    /// Gets the singleton animation manager instance.
    /// </summary>
    internal static AnimationManager Instance
    {
        get
        {
            var existing = Volatile.Read(ref _instance);
            if (existing != null)
            {
                return existing;
            }

            var created = new AnimationManager();
            return Interlocked.CompareExchange(ref _instance, created, null) ?? created;
        }
    }

    /// <summary>
    /// Gets the number of currently active animations.
    /// </summary>
    public int ActiveCount
    {
        get { lock (_sync) { return _active.Count; } }
    }

    internal bool HasRenderDemand
    {
        get
        {
            lock (_sync)
            {
                return HasUnpausedClock();
            }
        }
    }

    internal void Register(AnimationClock clock)
    {
        lock (_sync)
        {
            // Only ever called for a not-yet-running clock (AnimationClock.Start guards on its running state), so a clock
            // appears at most once and a single Unregister fully removes it.
            if (_isUpdating)
            {
                _pendingAdd.Add(clock);
            }
            else
            {
                _active.Add(clock);
            }

            EnableContinuousMode();
        }
    }

    internal void Unregister(AnimationClock clock)
    {
        lock (_sync)
        {
            if (_isUpdating)
            {
                _pendingRemove.Add(clock);
            }
            else
            {
                _active.Remove(clock);
                DisableContinuousModeIfIdle();
            }
        }
    }

    internal void OnPauseStateChanged()
    {
        lock (_sync)
        {
            if (HasUnpausedClock())
            {
                EnableContinuousMode();
            }
            else
            {
                DisableContinuousModeIfIdle();
            }
        }
    }

    /// <summary>
    /// Updates all active animation clocks. Called by the rendering pipeline
    /// before each frame (e.g. from <c>Window.RenderFrameCore</c>).
    /// </summary>
    public void Update()
    {
        lock (_sync)
        {
            if (_active.Count == 0 && _pendingAdd.Count == 0)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();

            _isUpdating = true;
            try
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    _active[i].Update(now);
                }
            }
            finally
            {
                _isUpdating = false;
            }

            // Apply deferred additions/removals
            if (_pendingAdd.Count > 0)
            {
                _active.AddRange(_pendingAdd);
                _pendingAdd.Clear();
            }

            if (_pendingRemove.Count > 0)
            {
                for (int i = 0; i < _pendingRemove.Count; i++)
                {
                    _active.Remove(_pendingRemove[i]);
                }

                _pendingRemove.Clear();
            }

            DisableContinuousModeIfIdle();
        }
    }

    private void EnableContinuousMode()
    {
        if (!Application.IsRunning)
        {
            return;
        }

        Application.Current.RenderLoopSettings.AnimationActive = true;
    }

    private void DisableContinuousModeIfIdle()
    {
        if (HasUnpausedClock())
        {
            return;
        }

        if (!Application.IsRunning)
        {
            return;
        }

        Application.Current.RenderLoopSettings.AnimationActive = false;
    }

    private bool HasUnpausedClock()
    {
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].IsRunning && !_active[i].IsPaused)
            {
                return true;
            }
        }

        for (int i = 0; i < _pendingAdd.Count; i++)
        {
            if (_pendingAdd[i].IsRunning && !_pendingAdd[i].IsPaused)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resets the singleton instance. For testing purposes only.
    /// </summary>
    internal static void Reset()
    {
        var instance = Interlocked.Exchange(ref _instance, null);
        if (instance != null)
        {
            lock (instance._sync)
            {
                instance._active.Clear();
                instance._pendingAdd.Clear();
                instance._pendingRemove.Clear();
                instance.DisableContinuousModeIfIdle();
            }
        }
    }
}
