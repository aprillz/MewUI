using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// Managed dispatcher of a headless session: no OS event source, the host loop blocks on a wake
/// event, and timer expiry follows the session's clock.
/// </summary>
internal sealed class HeadlessDispatcher : ManagedUiDispatcher
{
    private readonly HeadlessTimeSource _clock;

    public HeadlessDispatcher(HeadlessTimeSource clock)
    {
        _clock = clock;
    }

    protected override int MaxPumpIterations => 32;

    protected override long GetTimestamp() => _clock.GetTimestamp();

    protected override int NoTimerPollTimeout(int maxMs) => maxMs;

    protected override void DispatchDueTimer(Action action) => action();
}
