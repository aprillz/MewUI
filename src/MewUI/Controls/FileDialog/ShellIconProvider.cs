namespace Aprillz.MewUI.Platform;

/// <summary>
/// Platform seam for the OS shell's file-type icons. Resolution is BY EXTENSION / TYPE only and never
/// touches the file on disk, so a disconnected network share or a spun-down drive can never block the UI
/// (see design.md §2.1). Returns null to fall back to the bundled vector icons (<see cref="FileIconElement"/>).
/// <para>
/// Core port mapping: this mirrors the platform-host registration pattern. Each implementation moves into
/// its platform assembly (Win32 SHGetFileInfo, MacOS NSWorkspace, X11 XDG icon theme) and registers as
/// <c>IShellIconProvider</c>; <see cref="ShellIconProviders.ForCurrentOS"/> here stands in for that resolution.
/// </para>
/// </summary>
/// <summary>Semantic place identity for the left sidebar, so providers can return the distinctive
/// special-folder icon (Downloads, Pictures, ...) instead of a generic folder.</summary>
public enum ShellPlaceKind
{
    Folder,
    Home,
    Desktop,
    Documents,
    Downloads,
    Music,
    Pictures,
    Videos,
    Applications,
    Drive,
}

public interface IShellIconProvider
{
    // Non-blocking, BY EXTENSION/TYPE only. Must not touch the file at `path` on disk.
    // Returns a decoded icon as an ImageSource, or null to fall back to vector icons.
    // Implementations cache by (extension, isDirectory, sizePx) so the native call runs once per type.
    ImageSource? GetIcon(string path, bool isDirectory, int sizePx);

    // Distinctive icon for a sidebar place. Resolved from a fixed system resource (Windows imageres.dll
    // via the known-folder registry entry, macOS CoreTypes.bundle .icns, Linux freedesktop icon name) so
    // it never stats the real path - safe for redirected/network folders. Null falls back to vector.
    ImageSource? GetPlaceIcon(ShellPlaceKind kind, int sizePx);

    // The ACTUAL per-file/volume icon by real path (e.g. an exe's embedded icon, a volume's custom icon).
    // This touches the file system, so it MUST be called off the UI thread (the async upgrade layer does this:
    // a generic placeholder shows first, this replaces it when ready). Returns null if unavailable.
    ImageSource? GetRealIcon(string path, int sizePx);
}

internal static class ShellIconProviders
{
    /// <summary>The active provider, exposed by the registered platform host (Null when none / headless).</summary>
    public static IShellIconProvider Current =>
        (Application.IsRunning ? Application.Current.PlatformHost?.ShellIconProvider : null) ?? NullShellIconProvider.Instance;
}

/// <summary>No shell icons available - always falls back to vector icons.</summary>
internal sealed class NullShellIconProvider : IShellIconProvider
{
    public static readonly NullShellIconProvider Instance = new();

    public ImageSource? GetIcon(string path, bool isDirectory, int sizePx) => null;

    public ImageSource? GetPlaceIcon(ShellPlaceKind kind, int sizePx) => null;

    public ImageSource? GetRealIcon(string path, int sizePx) => null;
}
