namespace Aprillz.MewUI;

/// <summary>
/// Provides data for <see cref="Application.DispatcherUnhandledException"/> when an exception escapes UI dispatcher work.
/// </summary>
public sealed class DispatcherUnhandledExceptionEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherUnhandledExceptionEventArgs"/> class.
    /// </summary>
    /// <param name="exception">The unhandled exception.</param>
    public DispatcherUnhandledExceptionEventArgs(Exception exception) => Exception = exception;

    /// <summary>
    /// Gets the unhandled exception.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Set to true to continue the UI loop instead of terminating.
    /// </summary>
    public bool Handled { get; set; }
}
