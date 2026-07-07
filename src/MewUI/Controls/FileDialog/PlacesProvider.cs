namespace Aprillz.MewUI.Platform;

/// <summary>
/// The dialog's sidebar places (sections + entries). The layout (which sections/folders, order, labels) is
/// an OS shell convention (Finder Favorites/Locations vs Explorer Quick access/This PC), so it is provided
/// by the registered <see cref="IPlatformHost"/>; the default is empty. Special-folder paths are BCL and
/// volumes come from the registered <see cref="IMountedVolumeProvider"/> - both cross-platform. Entries are
/// tagged with a <see cref="ShellPlaceKind"/> so their distinctive system icon can be resolved.
/// </summary>
public interface IPlacesProvider
{
    List<PlaceItem> GetPlaces();
}

internal static class PlacesProviders
{
    /// <summary>The active provider, exposed by the registered platform host (empty when none / headless).</summary>
    public static IPlacesProvider Current =>
        (Application.IsRunning ? Application.Current.PlatformHost?.PlacesProvider : null) ?? EmptyPlacesProvider.Instance;
}

/// <summary>No places (headless / unregistered platform).</summary>
internal sealed class EmptyPlacesProvider : IPlacesProvider
{
    public static readonly EmptyPlacesProvider Instance = new();

    public List<PlaceItem> GetPlaces() => new();
}

/// <summary>
/// Neutral (BCL-only) helpers the platform place providers compose their layout from. No OS branching:
/// each platform provider decides the sections/order; these just materialize entries.
/// </summary>
internal static class PlacesBuilder
{
    public static void AddHeader(List<PlaceItem> items, string label)
        => items.Add(new PlaceItem(label, string.Empty, FileIconKind.Folder, IsHeader: true));

    public static void AddFolder(List<PlaceItem> items, string label, string path, ShellPlaceKind place)
    {
        if (!string.IsNullOrEmpty(path))
        {
            items.Add(new PlaceItem(label, path, FileIconKind.Folder, place));
        }
    }

    public static void AddVolumes(List<PlaceItem> items)
    {
        foreach (var volume in MountedVolumeProviders.Current.GetVolumes())
        {
            items.Add(new PlaceItem(volume.DisplayName, volume.Path, FileIconKind.Drive, ShellPlaceKind.Drive));
        }
    }

    public static string SpecialFolder(Environment.SpecialFolder folder)
        => Environment.GetFolderPath(folder);

    /// <summary>~/Downloads (the cross-platform default). The X11 provider overrides this with XDG.</summary>
    public static string DownloadsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? string.Empty : Path.Combine(home, "Downloads");
    }
}
