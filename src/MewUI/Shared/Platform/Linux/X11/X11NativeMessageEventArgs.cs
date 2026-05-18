namespace Aprillz.MewUI;

/// <summary>
/// Provides X11 event data for the <see cref="Window.NativeMessage"/> event.
/// </summary>
public sealed class X11NativeMessageEventArgs : NativeMessageEventArgs
{
    internal X11NativeMessageEventArgs(int eventType, nint eventPointer)
    {
        EventType = eventType;
        EventPointer = eventPointer;
    }

    /// <summary>X11 event type (e.g. KeyPress=2, ButtonPress=4, Expose=12).</summary>
    public int EventType { get; }

    /// <summary>
    /// Pointer to the <c>XEvent</c> union on the stack.
    /// Valid only during the event callback; do not store this pointer.
    /// Use <c>Unsafe.Read&lt;XEvent&gt;(EventPointer.ToPointer())</c> to read the full structure.
    /// </summary>
    public nint EventPointer { get; }
}
