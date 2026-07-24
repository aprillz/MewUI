using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Managed dispatcher for the headless preview loop: no OS event source, the host loop blocks on a
/// wake event and drains the queue/timers each pass.
/// </summary>
internal sealed class PreviewDispatcher : ManagedUiDispatcher
{
    protected override int MaxPumpIterations => 32;

    protected override int NoTimerPollTimeout(int maxMs) => maxMs;

    protected override void DispatchDueTimer(Action action) => action();
}
