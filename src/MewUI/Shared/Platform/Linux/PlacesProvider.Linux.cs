namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>freedesktop sidebar convention: Places + Devices. Downloads honors XDG_DOWNLOAD_DIR.</summary>
internal sealed class LinuxPlacesProvider : IPlacesProvider
{
    public List<PlaceItem> GetPlaces()
    {
        var items = new List<PlaceItem>();

        PlacesBuilder.AddHeader(items, MewUIStrings.SidebarPlaces.Value);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderHome.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.UserProfile), ShellPlaceKind.Home);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDesktop.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDocuments.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDownloads.Value, DownloadsPath(), ShellPlaceKind.Downloads);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderMusic.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyMusic), ShellPlaceKind.Music);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderPictures.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderVideos.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyVideos), ShellPlaceKind.Videos);

        PlacesBuilder.AddHeader(items, MewUIStrings.SidebarDevices.Value);
        PlacesBuilder.AddVolumes(items);

        return items;
    }

    private static string DownloadsPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        if (!string.IsNullOrEmpty(xdg))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return xdg.Replace("$HOME", home);
        }
        return PlacesBuilder.DownloadsPath();
    }
}
