using System.Diagnostics;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Animation;

/// <summary>
/// Drives all active <see cref="AnimationClock"/> instances synchronized with the platform render pulse.
/// <see cref="RenderLoopSettings.AnimationActive"/> reads <see cref="HasUnpausedClocks"/> each frame, so the host
/// keeps pulsing while any clock runs. Per-window demand lets the host render only the surfaces that consumed that
/// pulse. It never touches the user's <see cref="RenderLoopSettings.Continuous"/> flag.
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
    private bool _pulseHasApplicationDemand;
    private bool _isUpdating;

    // Read by the render loop every frame, so it is cached rather than recomputed under _sync. Every
    // mutation of the clock set or of a pause state refreshes it.
    private int _hasUnpausedClock;

    /// <summary>
    /// Whether any registered clock is running and unpaused. The render loop pulls this each frame, so a
    /// clock registered before the application ran still starts pulsing once the loop begins.
    /// </summary>
    internal bool HasUnpausedClocks => Volatile.Read(ref _hasUnpausedClock) != 0;

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

    /// <summary>
    /// Advances all clocks once and returns the render-demand snapshot for this platform pulse.
    /// Disposing the lease releases any window references captured by the final animation frame.
    /// </summary>
    internal AnimationPulse BeginPulse(RenderLoopSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Update();
            return new AnimationPulse(this, settings);
        }
        catch
        {
            CompletePulse();
            throw;
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

            RefreshActiveState();
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
                RefreshActiveState();
            }
        }
    }

    internal void OnPauseStateChanged()
    {
        lock (_sync)
        {
            RefreshActiveState();
        }
    }

    /// <summary>
    /// Updates all active animation clocks. Called once by the platform host before selecting and
    /// rendering the windows that consumed the pulse.
    /// </summary>
    public void Update() => UpdateAt(Stopwatch.GetTimestamp());

    /// <summary>
    /// Ticks every active clock against <paramref name="timestamp"/>, in <see cref="Stopwatch"/> ticks.
    /// A caller that supplies the timestamp can step a transition frame by frame, which reading the
    /// wall clock cannot: a test would only ever sample progress zero.
    /// </summary>
    internal void UpdateAt(long timestamp)
    {
        lock (_sync)
        {
            _pulseHasApplicationDemand = false;

            if (_active.Count == 0 && _pendingAdd.Count == 0)
            {
                return;
            }

            long now = timestamp;

            // Capture demand before ticking. A clock may complete and unregister during Update, but
            // its final callback still needs one frame on the surface it updated. An attached clock
            // needs no entry here: its tick invalidates what it animates, and that invalidation is what
            // selects the window.
            for (int i = 0; i < _active.Count; i++)
            {
                var clock = _active[i];
                if (!clock.IsRunning || clock.IsPaused)
                {
                    continue;
                }

                if (!clock.HasOwner)
                {
                    _pulseHasApplicationDemand = true;
                }
            }

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

            RefreshActiveState();
        }
    }

    private bool PulseHasApplicationRenderDemand
    {
        get
        {
            lock (_sync)
            {
                return _pulseHasApplicationDemand;
            }
        }
    }

    private void CompletePulse()
    {
        lock (_sync)
        {
            _pulseHasApplicationDemand = false;
        }
    }

    // Callers hold _sync: every clock-set and pause-state mutation ends here.
    private void RefreshActiveState()
        => Volatile.Write(ref _hasUnpausedClock, HasUnpausedClock() ? 1 : 0);


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
    /// Provides the render policy and demand snapshot for one animation pulse. This scope-bound usage
    /// pattern keeps clock advancement paired with snapshot cleanup in every platform loop.
    /// </summary>
    internal readonly struct AnimationPulse : IDisposable
    {
        private readonly AnimationManager _manager;
        private readonly bool _hasApplicationRenderDemand;
        private readonly bool _rendersApplicationWide;

        internal AnimationPulse(AnimationManager manager, RenderLoopSettings settings)
        {
            _manager = manager;
            _hasApplicationRenderDemand = manager.PulseHasApplicationRenderDemand;
            _rendersApplicationWide =
                settings.Continuous ||
                !settings.VSyncEnabled ||
                _hasApplicationRenderDemand;
        }

        internal bool HasApplicationRenderDemand => _hasApplicationRenderDemand;

        /// <summary>
        /// Whether this window has to be rendered for the frame the pulse just advanced.
        /// </summary>
        /// <param name="window">The window being considered for this frame.</param>
        /// <param name="needsRender">Whether something asked this window to repaint.</param>
        // A clock attached to an element does not select its window here: its tick invalidates what it
        // animates, and that invalidation is the render's only reason. An element the render pass culled
        // invalidates nothing, so its window sleeps.
        internal bool ShouldRender(Window window, bool needsRender) =>
            needsRender || _rendersApplicationWide;

        public void Dispose() => _manager.CompletePulse();
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
                instance._pulseHasApplicationDemand = false;
                instance.RefreshActiveState();
            }
        }
    }
}
