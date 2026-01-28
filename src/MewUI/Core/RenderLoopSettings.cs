using System.Threading;

namespace Aprillz.MewUI;

public sealed class RenderLoopSettings
{
    private int _mode;
    private int _targetFps;
    private int _vsyncEnabled = 1;

    internal RenderLoopSettings()
    {
    }

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

    public void SetContinuous(bool enabled)
        => Mode = enabled ? RenderLoopMode.Continuous : RenderLoopMode.OnRequest;
}
