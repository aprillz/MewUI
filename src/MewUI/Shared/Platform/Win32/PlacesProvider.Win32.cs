namespace Aprillz.MewUI.Platform.Win32;

/// <summary>Windows Explorer sidebar convention: Quick access + This PC.</summary>
internal sealed class WindowsPlacesProvider : IPlacesProvider
{
    public List<PlaceItem> GetPlaces()
    {
        var items = new List<PlaceItem>();

        PlacesBuilder.AddHeader(items, "Quick access");
        PlacesBuilder.AddFolder(items, "Desktop", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
        PlacesBuilder.AddFolder(items, "Downloads", PlacesBuilder.DownloadsPath(), ShellPlaceKind.Downloads);
        PlacesBuilder.AddFolder(items, "Documents", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
        PlacesBuilder.AddFolder(items, "Pictures", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);
        PlacesBuilder.AddFolder(items, "Music", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyMusic), ShellPlaceKind.Music);
        PlacesBuilder.AddFolder(items, "Videos", PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyVideos), ShellPlaceKind.Videos);

        PlacesBuilder.AddHeader(items, "This PC");
        PlacesBuilder.AddVolumes(items);

        return items;
    }
}
