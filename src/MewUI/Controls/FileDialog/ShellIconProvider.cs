namespace Aprillz.MewUI.Platform;

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

/// <summary>
/// Platform seam for the OS shell's file-type icons. The active implementation is exposed by the registered
/// <see cref="IPlatformHost"/> (native impls live in the platform assemblies); the default falls back to the
/// bundled vector icons (<see cref="FileIconElement"/>).
/// </summary>
public interface IShellIconProvider
{
    /// <summary>Icon for a file type. Non-blocking (by extension/type only, never touches <paramref name="path"/>
    /// on disk, so a disconnected share/spun-down drive cannot block the UI - design.md §2.1). Null falls back
    /// to vector icons.</summary>
    ImageSource? GetIcon(string path, bool isDirectory, int sizePx);

    /// <summary>Distinctive icon for a sidebar place, resolved from a fixed system resource (never stats the
    /// real path - safe for redirected/network folders). Null falls back to vector.</summary>
    ImageSource? GetPlaceIcon(ShellPlaceKind kind, int sizePx);

    /// <summary>The actual per-file/volume icon by real path (embedded exe icon, custom volume icon). Touches
    /// the file system, so callers must invoke it off the UI thread. Null if unavailable.</summary>
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
