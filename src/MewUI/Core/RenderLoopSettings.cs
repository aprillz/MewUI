using System.Threading;

namespace Aprillz.MewUI;

/// <summary>
/// Controls the application's render loop scheduling.
/// </summary>
public sealed class RenderLoopSettings
{
    private int _mode;
    private int _targetFps;
    private int _vsyncEnabled = 1;

    internal RenderLoopSettings()
    {
    }

    /// <summary>
    /// Gets or sets the render loop mode.
    /// </summary>
    public RenderLoopMode Mode
    {
        get => (RenderLoopMode)Volatile.Read(ref _mode);
        set
        {
            int next = (int)value;
            if (Interlocked.Exchange(ref _mode, next) == next)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Gets or sets the target FPS. A value of 0 indicates no cap.
    /// </summary>
    public int TargetFps
    {
        get => Volatile.Read(ref _targetFps);
        set
        {
            if (Interlocked.Exchange(ref _targetFps, value) == value)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether VSync is enabled (when supported by the backend/platform).
    /// </summary>
    public bool VSyncEnabled
    {
        get => Volatile.Read(ref _vsyncEnabled) != 0;
        set
        {
            int next = value ? 1 : 0;
            if (Interlocked.Exchange(ref _vsyncEnabled, next) == next)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Convenience helper to toggle <see cref="RenderLoopMode.Continuous"/>.
    /// </summary>
    public void SetContinuous(bool enabled)
        => Mode = enabled ? RenderLoopMode.Continuous : RenderLoopMode.OnRequest;
}
