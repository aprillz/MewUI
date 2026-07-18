namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxDispatcher : ManagedUiDispatcher
{
    // The X11 host re-pumps at the loop level too, but keeping a bounded internal re-pump lets a
    // cascade of self-scheduling work settle within one ProcessWorkItems call.
    protected override int MaxPumpIterations => 32;

    // Cap the wait so best-effort DPI polling still wakes even when the desktop sends no notification.
    protected override int NoTimerPollTimeout(int maxMs) => maxMs;

    protected override void DispatchDueTimer(Action action) => action();
}
