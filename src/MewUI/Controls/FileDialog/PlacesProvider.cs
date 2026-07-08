namespace Aprillz.MewUI.Platform;

/// <summary>
/// Builds each OS's default shell sidebar structure (sections + entries). User folders come from the
/// BCL (<see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>); volumes come from the platform <see cref="IMountedVolumeProvider"/>.
/// <para>
/// Core port mapping: this composes a cross-platform "places" model. Special folders stay BCL-based;
/// volumes resolve through the registered platform <see cref="IMountedVolumeProvider"/>. The section
/// layout (Quick access/This PC, Favorites/Locations, Places/Devices) is the shell convention per OS.
/// Each entry is tagged with a <see cref="ShellPlaceKind"/> so its distinctive system icon can be resolved.
/// </para>
/// </summary>
internal static class PlacesProvider
{
    public static List<PlaceItem> GetPlaces()
    {
        var items = new List<PlaceItem>();

        if (OperatingSystem.IsMacOS())
        {
            AddHeader(items, "Favorites");
            AddFolder(items, "Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ShellPlaceKind.Home);
            AddFolder(items, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
            AddFolder(items, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
            AddFolder(items, "Downloads", GetDownloadsPath(), ShellPlaceKind.Downloads);
            AddFolder(items, "Applications", "/Applications", ShellPlaceKind.Applications);
            AddFolder(items, "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);

            AddHeader(items, "Locations");
            AddVolumes(items);
        }
        else if (OperatingSystem.IsWindows())
        {
            AddHeader(items, "Quick access");
            AddFolder(items, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
            AddFolder(items, "Downloads", GetDownloadsPath(), ShellPlaceKind.Downloads);
            AddFolder(items, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
            AddFolder(items, "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);
            AddFolder(items, "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), ShellPlaceKind.Music);
            AddFolder(items, "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), ShellPlaceKind.Videos);

            AddHeader(items, "This PC");
            AddVolumes(items);
        }
        else // Linux and other Unix
        {
            AddHeader(items, "Places");
            AddFolder(items, "Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ShellPlaceKind.Home);
            AddFolder(items, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
            AddFolder(items, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
            AddFolder(items, "Downloads", GetDownloadsPath(), ShellPlaceKind.Downloads);
            AddFolder(items, "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), ShellPlaceKind.Music);
            AddFolder(items, "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);
            AddFolder(items, "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), ShellPlaceKind.Videos);

            AddHeader(items, "Devices");
            AddVolumes(items);
        }

        return items;
    }

    private static void AddHeader(List<PlaceItem> items, string label)
        => items.Add(new PlaceItem(label, string.Empty, FileIconKind.Folder, IsHeader: true));

    private static void AddFolder(List<PlaceItem> items, string label, string path, ShellPlaceKind place)
    {
        if (!string.IsNullOrEmpty(path))
        {
            items.Add(new PlaceItem(label, path, FileIconKind.Folder, place));
        }
    }

    private static void AddVolumes(List<PlaceItem> items)
    {
        foreach (var volume in MountedVolumeProviders.ForCurrentOS().GetVolumes())
        {
            items.Add(new PlaceItem(volume.DisplayName, volume.Path, FileIconKind.Drive, ShellPlaceKind.Drive));
        }
    }

    private static string GetDownloadsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Windows/macOS: ~/Downloads. Linux: honor XDG_DOWNLOAD_DIR when set.
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
            if (!string.IsNullOrEmpty(xdg))
            {
                return xdg.Replace("$HOME", home);
            }
        }

        return string.IsNullOrEmpty(home) ? string.Empty : Path.Combine(home, "Downloads");
    }
}
