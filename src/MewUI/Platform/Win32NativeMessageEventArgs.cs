namespace Aprillz.MewUI;

/// <summary>
/// Provides Win32 window message data for the <see cref="Aprillz.MewUI.Window.NativeMessage"/> event.
/// </summary>
public sealed class Win32NativeMessageEventArgs : NativeMessageEventArgs
{
    internal Win32NativeMessageEventArgs(uint msg, nint wParam, nint lParam)
    {
        Msg = msg;
        WParam = wParam;
        LParam = lParam;
    }

    /// <summary>Win32 message identifier (e.g. WM_PAINT, WM_SIZE).</summary>
    public uint Msg { get; }

    /// <summary>First message parameter (WPARAM).</summary>
    public nint WParam { get; }

    /// <summary>Second message parameter (LPARAM).</summary>
    public nint LParam { get; }
}
