namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// macOS UI dispatcher. The host loop drives <see cref="ManagedUiDispatcher.ProcessWorkItems"/> and
/// re-pumps at its own level, so the internal pump runs a single pass.
/// </summary>
internal sealed class MacOSDispatcher : ManagedUiDispatcher
{
    protected override int MaxPumpIterations => 1;

    // No timers: let the host wait until an OS event or an explicit wake.
    protected override int NoTimerPollTimeout(int maxMs) => -1;

    // Re-queue at Background so a due timer flows through the queue's priority ordering and the
    // dispatcher's exception routing instead of running inline on the pump.
    protected override void DispatchDueTimer(Action action)
        => EnqueueOnQueue(DispatcherPriority.Background, action);
}
