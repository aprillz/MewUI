namespace Aprillz.MewUI.Platform.Win32;

/// <summary>Windows Explorer sidebar convention: Quick access + This PC.</summary>
internal sealed class WindowsPlacesProvider : IPlacesProvider
{
    public List<PlaceItem> GetPlaces()
    {
        var items = new List<PlaceItem>();

        PlacesBuilder.AddHeader(items, MewUIStrings.SidebarQuickAccess.Value);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDesktop.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.Desktop), ShellPlaceKind.Desktop);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDownloads.Value, PlacesBuilder.DownloadsPath(), ShellPlaceKind.Downloads);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderDocuments.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyDocuments), ShellPlaceKind.Documents);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderPictures.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyPictures), ShellPlaceKind.Pictures);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderMusic.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyMusic), ShellPlaceKind.Music);
        PlacesBuilder.AddFolder(items, MewUIStrings.FolderVideos.Value, PlacesBuilder.SpecialFolder(Environment.SpecialFolder.MyVideos), ShellPlaceKind.Videos);

        PlacesBuilder.AddHeader(items, MewUIStrings.SidebarThisPC.Value);
        PlacesBuilder.AddVolumes(items);

        return items;
    }
}
