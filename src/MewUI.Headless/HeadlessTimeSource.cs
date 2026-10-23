using System.Diagnostics;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// The clock a session's dispatcher timers and animation pulses read, in <see cref="Stopwatch"/> ticks:
/// the wall clock, or a counter that only <see cref="Advance"/> moves.
/// </summary>
internal sealed class HeadlessTimeSource
{
    private long _virtualTicks;

    public HeadlessTimeSource(bool isVirtual)
    {
        IsVirtual = isVirtual;
        // Starting at the wall clock keeps the virtual time comparable with timestamps taken before
        // the session started, such as an animation clock's start.
        _virtualTicks = Stopwatch.GetTimestamp();
    }

    public bool IsVirtual { get; }

    public long GetTimestamp() => IsVirtual ? Volatile.Read(ref _virtualTicks) : Stopwatch.GetTimestamp();

    public void Advance(long ticks) => Interlocked.Add(ref _virtualTicks, ticks);

    public static long ToTicks(TimeSpan duration)
        => (long)Math.Round(duration.TotalSeconds * Stopwatch.Frequency);
}
