using System.Diagnostics;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Maps X server event times (32-bit milliseconds) onto <see cref="Stopwatch"/> ticks for one display.
/// </summary>
internal sealed class X11EventClock
{
    private static readonly double _ticksPerMillisecond = Stopwatch.Frequency / 1000.0;

    private bool _hasLast;
    private uint _lastServerTime;
    private long _serverMilliseconds;
    private long _offsetTicks = long.MaxValue;

    /// <summary>Returns the Stopwatch tick at which the server stamped <paramref name="serverTime"/>.</summary>
    public long ToTimestamp(nuint serverTime)
    {
        uint time = unchecked((uint)serverTime);
        if (_hasLast)
        {
            // Signed difference: unwraps the 49.7-day rollover and tolerates an event stamped slightly earlier.
            _serverMilliseconds += unchecked((int)(time - _lastServerTime));
        }
        else
        {
            _serverMilliseconds = time;
            _hasLast = true;
        }

        _lastServerTime = time;

        // A remote display runs its own clock, so the offset is learned rather than assumed. The smallest
        // observed gap is the transport floor; keeping it fixed preserves the server's sample spacing.
        long serverTicks = (long)(_serverMilliseconds * _ticksPerMillisecond);
        long offset = Stopwatch.GetTimestamp() - serverTicks;
        if (offset < _offsetTicks)
        {
            _offsetTicks = offset;
        }

        return serverTicks + _offsetTicks;
    }
}
