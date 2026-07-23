namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>macOS Finder sidebar convention: Favorites + Locations.</summary>
internal sealed class MacPlacesProvider : IPlacesProvider
{
    public List<PlaceItem> GetPlaces()
    {
        var items = new List<PlaceItem>();

        PlacesBuilder.AddHeader(items, MewUIStrings.SidebarFavorites.Value);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderHome.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.UserProfile), ShellPlaceKind.Home);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDesktop.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDocuments.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDownloads.Value, PlacesBuilder.DownloadsPath(), ShellPlaceKind.Downloads);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderApplications.Value, "/Applications", ShellPlaceKind.Applications);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderPictures.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);

        PlacesBuilder.AddHeader(items, MewUIStrings.SidebarLocations.Value);
        PlacesBuilder.AddVolumes(items);

        return items;
    }
}
