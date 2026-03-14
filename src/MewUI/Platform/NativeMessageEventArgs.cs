namespace Aprillz.MewUI;

/// <summary>
/// Base class for native platform message/event arguments delivered through
/// <see cref="Aprillz.MewUI.Window.NativeMessage"/>.
/// </summary>
public class NativeMessageEventArgs
{
    /// <summary>
    /// Gets or sets a value indicating whether the message has been handled.
    /// When <see langword="true"/>, the framework skips its default processing for this message.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets or sets the result value to return to the platform.
    /// Only meaningful on Win32 (LRESULT); ignored on X11 and macOS.
    /// </summary>
    public nint Result { get; set; }
}
