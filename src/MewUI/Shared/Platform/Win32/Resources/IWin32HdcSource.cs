namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Exposes a Win32 HDC for render targets backed by a device context.
/// Primarily used for layered-window presentation (UpdateLayeredWindow).
/// </summary>
public interface IWin32HdcSource
{
    /// <summary>
    /// Gets the underlying Win32 device context handle (HDC).
    /// </summary>
    nint Hdc { get; }
}

