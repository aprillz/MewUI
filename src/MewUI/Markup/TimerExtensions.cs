namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extension methods for <see cref="DispatcherTimer"/>.
/// </summary>
public static class TimerExtensions
{
    public static DispatcherTimer Interval(this DispatcherTimer timer, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Interval = interval;
        return timer;
    }

    public static DispatcherTimer IntervalMs(this DispatcherTimer timer, int milliseconds)
        => Interval(timer, TimeSpan.FromMilliseconds(milliseconds));

    public static DispatcherTimer OnTick(this DispatcherTimer timer, Action handler)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(handler);

        timer.Tick += (_, _) => handler();
        return timer;
    }

    public static DispatcherTimer Start(this DispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Start();
        return timer;
    }

    public static DispatcherTimer Stop(this DispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Stop();
        return timer;
    }
}

