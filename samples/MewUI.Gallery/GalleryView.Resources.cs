using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Gallery;

/// <summary>
/// The images and icon dictionary the pages draw, held as values a host fills. This app reads them
/// from disk before the window shows; the file-based app downloads them and they arrive later.
/// </summary>
sealed class GalleryResources
{
    /// <summary>The horizontal logo as pixels, for the pages that demonstrate raster images.</summary>
    public ObservableValue<IImageSource?> Logo { get; } = new(null);

    /// <summary>The same logo as vector art, which is what the app's own chrome draws.</summary>
    public ObservableValue<IImageSource?> LogoVector { get; } = new(null);

    public ObservableValue<IImageSource?> April { get; } = new(null);

    public ObservableValue<IImageSource?> Soonduk { get; } = new(null);

    public ObservableValue<IImageSource?> FolderOpen { get; } = new(null);

    public ObservableValue<IImageSource?> FolderClosed { get; } = new(null);

    public ObservableValue<IImageSource?> Document { get; } = new(null);

    /// <summary>The icon dictionary's XAML, or null until it arrives.</summary>
    public ObservableValue<string?> Icons { get; } = new(null);

    /// <summary>File names the hosts fetch, in the order the pages need them.</summary>
    public static string[] FileNames { get; } =
    [
        "logo_h.svg",
        "logo_h-480.png",
        "april.jpg",
        "soonduk.jpg",
        "folder-horizontal-open.png",
        "folder-horizontal.png",
        "document.png",
        "Icons.xaml",
    ];

    /// <summary>
    /// Routes one fetched file to the value that holds it, so a host only decides where bytes come
    /// from. Unknown names are ignored rather than throwing: a host may carry extra files.
    /// </summary>
    public void Apply(string fileName, byte[] content)
    {
        switch (fileName)
        {
            case "logo_h.svg": LogoVector.Value = SimpleSvgSource.FromBytes(content); break;
            case "logo_h-480.png": Logo.Value = ImageSource.FromBytes(content); break;
            case "april.jpg": April.Value = ImageSource.FromBytes(content); break;
            case "soonduk.jpg": Soonduk.Value = ImageSource.FromBytes(content); break;
            case "folder-horizontal-open.png": FolderOpen.Value = ImageSource.FromBytes(content); break;
            case "folder-horizontal.png": FolderClosed.Value = ImageSource.FromBytes(content); break;
            case "document.png": Document.Value = ImageSource.FromBytes(content); break;
            case "Icons.xaml": Icons.Value = System.Text.Encoding.UTF8.GetString(content); break;
        }
    }
}

partial class GalleryView
{
    /// <summary>The resources the pages bind to. The host fills them; excluded from fba generation.</summary>
    public static GalleryResources Resources { get; } = new();
}
