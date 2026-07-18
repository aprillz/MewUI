namespace Aprillz.MewUI;

/// <summary>
/// Controls when the application's run loop ends automatically as windows close.
/// </summary>
public enum ShutdownMode
{
    /// <summary>Shut down when the last window closes. This is the default.</summary>
    OnLastWindowClose,

    /// <summary>Shut down when the main window (the one passed to <see cref="Application.Run"/>) closes.</summary>
    OnMainWindowClose,

    /// <summary>Never shut down automatically; the loop ends only via <see cref="Application.Quit"/>.</summary>
    OnExplicitShutdown,
}
