namespace Aprillz.MewUI.Platform;

/// <summary>
/// Identifies the native window-surface family a platform host produces and a graphics backend
/// consumes. A backend only works against a matching platform, so the two are checked at
/// registration to fail a mismatched combination with a clear error instead of at the first render.
/// </summary>
internal enum PlatformSurfaceKind
{
    Win32,
    X11,
    MacOS,
}
