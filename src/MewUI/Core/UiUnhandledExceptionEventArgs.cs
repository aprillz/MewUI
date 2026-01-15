namespace Aprillz.MewUI;

public sealed class UiUnhandledExceptionEventArgs : EventArgs
{
    public UiUnhandledExceptionEventArgs(Exception exception) => Exception = exception;

    public Exception Exception { get; }

    /// <summary>
    /// Set to true to continue the UI loop instead of terminating.
    /// </summary>
    public bool Handled { get; set; }
}

