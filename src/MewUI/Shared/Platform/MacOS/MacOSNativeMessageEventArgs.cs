namespace Aprillz.MewUI;

/// <summary>
/// Provides macOS NSEvent data for the <see cref="Window.NativeMessage"/> event.
/// </summary>
public sealed class MacOSNativeMessageEventArgs : NativeMessageEventArgs
{
    internal MacOSNativeMessageEventArgs(nint nsEvent, int eventType)
    {
        NSEvent = nsEvent;
        EventType = eventType;
    }

    /// <summary>Native NSEvent handle.</summary>
    public nint NSEvent { get; }

    /// <summary>NSEvent type (e.g. LeftMouseDown=1, KeyDown=10, ScrollWheel=22).</summary>
    public int EventType { get; }
}
