namespace Aprillz.MewUI.Headless;

/// <summary>
/// The clock dispatcher timers and animations of a headless session run against.
/// </summary>
public enum HeadlessClock
{
    /// <summary>Time stands still until <see cref="HeadlessSession.AdvanceTime"/> moves it.</summary>
    Virtual,

    /// <summary>Timers expire on the wall clock, as in a running application.</summary>
    RealTime,
}
