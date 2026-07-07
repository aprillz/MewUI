namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>macOS Finder sidebar convention: Favorites + Locations.</summary>
internal sealed class MacPlacesProvider : IPlacesProvider
{
    public List<PlaceItem> GetPlaces()
    {
        var items = new List<PlaceItem>();

        PlacesBuilder.AddHeader(items, "Favorites");
        PlacesBuilder.AddFolder(items, "Home", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.UserProfile), ShellPlaceKind.Home);
        PlacesBuilder.AddFolder(items, "Desktop", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
        PlacesBuilder.AddFolder(items, "Documents", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
        PlacesBuilder.AddFolder(items, "Downloads", PlacesBuilder.DownloadsPath(), ShellPlaceKind.Downloads);
        PlacesBuilder.AddFolder(items, "Applications", "/Applications", ShellPlaceKind.Applications);
        PlacesBuilder.AddFolder(items, "Pictures", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);

        PlacesBuilder.AddHeader(items, "Locations");
        PlacesBuilder.AddVolumes(items);

        return items;
    }
}
