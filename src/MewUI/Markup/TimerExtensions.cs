namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extension methods for <see cref="DispatcherTimer"/>.
/// </summary>
public static class TimerExtensions
{
    /// <summary>
    /// Sets the timer interval.
    /// </summary>
    /// <param name="timer">Target timer.</param>
    /// <param name="interval">Interval timespan.</param>
    /// <returns>The timer for chaining.</returns>
    public static DispatcherTimer Interval(this DispatcherTimer timer, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Interval = interval;
        return timer;
    }

    /// <summary>
    /// Sets the timer interval in milliseconds.
    /// </summary>
    /// <param name="timer">Target timer.</param>
    /// <param name="milliseconds">Interval in milliseconds.</param>
    /// <returns>The timer for chaining.</returns>
    public static DispatcherTimer IntervalMs(this DispatcherTimer timer, int milliseconds)
        => Interval(timer, TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>
    /// Adds a tick event handler.
    /// </summary>
    /// <param name="timer">Target timer.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The timer for chaining.</returns>
    public static DispatcherTimer OnTick(this DispatcherTimer timer, Action handler)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(handler);

        timer.Tick += () => handler();
        return timer;
    }

    /// <summary>
    /// Starts the timer.
    /// </summary>
    /// <param name="timer">Target timer.</param>
    /// <returns>The timer for chaining.</returns>
    public static DispatcherTimer Start(this DispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Start();
        return timer;
    }

    /// <summary>
    /// Stops the timer.
    /// </summary>
    /// <param name="timer">Target timer.</param>
    /// <returns>The timer for chaining.</returns>
    public static DispatcherTimer Stop(this DispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Stop();
        return timer;
    }
}

