namespace Aprillz.MewUI;

/// <summary>
/// Base class for native platform message/event arguments delivered through
/// <see cref="Window.NativeMessage"/>.
/// </summary>
public class NativeMessageEventArgs
{
    /// <summary>
    /// Gets or sets a value indicating whether the message has been handled.
    /// When <see langword="true"/>, the framework skips its default processing for this message.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets or sets the result value returned to the platform for this message; platforms whose message
    /// dispatch takes no return value ignore it.
    /// </summary>
    public nint Result { get; set; }
}
