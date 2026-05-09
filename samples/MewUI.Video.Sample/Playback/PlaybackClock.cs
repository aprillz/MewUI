using System.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Playback;

public sealed class PlaybackClock
{
    private readonly object _gate = new();
    private TimeSpan _baseMediaTime;
    private long _baseStopwatchTicks;
    private bool _running;

    public TimeSpan Now
    {
        get
        {
            lock (_gate)
            {
                return _running
                    ? _baseMediaTime + Stopwatch.GetElapsedTime(_baseStopwatchTicks)
                    : _baseMediaTime;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _baseStopwatchTicks = Stopwatch.GetTimestamp();
            _running = true;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _baseMediaTime += Stopwatch.GetElapsedTime(_baseStopwatchTicks);
            _running = false;
        }
    }

    public void SeekTo(TimeSpan mediaTime)
    {
        lock (_gate)
        {
            _baseMediaTime = mediaTime < TimeSpan.Zero ? TimeSpan.Zero : mediaTime;
            _baseStopwatchTicks = Stopwatch.GetTimestamp();
        }
    }
}