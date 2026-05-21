namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// How a graphics backend produces and presents per-pixel-transparent window content
/// on Win32. Selected by the platform window backend at creation time so the matching
/// window styles (e.g. <c>WS_EX_LAYERED</c> vs <c>WS_EX_NOREDIRECTIONBITMAP</c>) and
/// present path (<c>UpdateLayeredWindow</c> vs swap-chain present) are wired up.
/// </summary>
public enum Win32TransparencyMode
{
    /// <summary>
    /// The backend renders into a CPU pixel buffer that the platform hands to
    /// <c>UpdateLayeredWindow</c> each frame. Window uses <c>WS_EX_LAYERED</c>.
    /// Suits CPU-only backends (GDI, GDI+) and D2D's DC render target path.
    /// </summary>
    Bitmap,

    /// <summary>
    /// The backend owns a GPU swap-chain that presents alpha-preserving frames directly.
    /// Window uses <c>WS_EX_NOREDIRECTIONBITMAP</c> so DWM composes the swap-chain output
    /// with per-pixel alpha. No CPU readback per frame.
    /// </summary>
    Surface,

    /// <summary>
    /// The backend hands its output to the OS compositor via DWM blur-behind or
    /// DirectComposition visuals. Currently reserved for future GL / DComp paths.
    /// </summary>
    Composition,
}

/// <summary>
/// Implemented by an <see cref="Rendering.IGraphicsFactory"/> that wants the
/// Win32 platform to use a specific transparency presentation strategy. Backends that do
/// not implement this interface are treated as <see cref="Win32TransparencyMode.Bitmap"/>
/// (safe default — the existing layered-DIB path).
/// </summary>
public interface IWin32TransparencyCapabilities
{
    Win32TransparencyMode TransparencyMode { get; }
}
