using System.Reflection;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extensions for images.
/// </summary>
public static class ImageExtensions
{
    /// <summary>
    /// Sets the image source from a file path.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="path">File path.</param>
    /// <returns>The image for chaining.</returns>
    public static Image SourceFile(this Image image, string path)
    {
        image.Source = ImageSource.FromFile(path);
        return image;
    }

    /// <summary>
    /// Sets the image source from an embedded resource.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="assembly">Assembly containing the resource.</param>
    /// <param name="resourceName">Resource name.</param>
    /// <returns>The image for chaining.</returns>
    public static Image SourceResource(this Image image, Assembly assembly, string resourceName)
    {
        image.Source = ImageSource.FromResource(assembly, resourceName);
        return image;
    }

    /// <summary>
    /// Sets the image source from an embedded resource using anchor type.
    /// </summary>
    /// <typeparam name="TAnchor">Anchor type for assembly resolution.</typeparam>
    /// <param name="image">Target image.</param>
    /// <param name="resourceName">Resource name.</param>
    /// <returns>The image for chaining.</returns>
    public static Image SourceResource<TAnchor>(this Image image, string resourceName)
    {
        image.Source = ImageSource.FromResource<TAnchor>(resourceName);
        return image;
    }

    /// <summary>
    /// Sets the viewbox and units.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewbox">Viewbox rectangle.</param>
    /// <param name="units">Viewbox units.</param>
    /// <returns>The image for chaining.</returns>
    public static Image Viewbox(
        this Image image,
        [FluentSetsMember(nameof(Image.Viewbox))] Rect? viewbox,
        [FluentSetsMember(nameof(Image.ViewboxUnits))] ImageViewboxUnits units = ImageViewboxUnits.Pixels)
    {
        image.Viewbox = viewbox;
        image.ViewboxUnits = units;
        return image;
    }

    /// <summary>
    /// Sets the viewbox with pixel units.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewbox">Viewbox rectangle.</param>
    /// <returns>The image for chaining.</returns>
    [FluentSetsConstant(nameof(Image.ViewboxUnits), ImageViewboxUnits.Pixels)]
    public static Image ViewboxPixels(this Image image, [FluentSetsMember(nameof(Image.Viewbox))] Rect? viewbox)
    {
        image.Viewbox = viewbox;
        image.ViewboxUnits = ImageViewboxUnits.Pixels;
        return image;
    }

    /// <summary>
    /// Sets the viewbox with relative units.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewbox">Viewbox rectangle.</param>
    /// <returns>The image for chaining.</returns>
    [FluentSetsConstant(nameof(Image.ViewboxUnits), ImageViewboxUnits.RelativeToBoundingBox)]
    public static Image ViewboxRelative(this Image image, [FluentSetsMember(nameof(Image.Viewbox))] Rect? viewbox)
    {
        image.Viewbox = viewbox;
        image.ViewboxUnits = ImageViewboxUnits.RelativeToBoundingBox;
        return image;
    }

    /// <summary>Sets the viewbox and units; obsolete name of <see cref="Viewbox(Image, Rect?, ImageViewboxUnits)"/>.</summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewBox">Viewbox rectangle.</param>
    /// <param name="units">Viewbox units.</param>
    /// <returns>The image for chaining.</returns>
    [Obsolete("Use Viewbox.")]
    public static Image ViewBox(this Image image, Rect? viewBox, ImageViewBoxUnits units = ImageViewBoxUnits.Pixels)
    {
        image.Viewbox = viewBox;
        image.ViewboxUnits = (ImageViewboxUnits)units;
        return image;
    }

    /// <summary>Sets the viewbox with pixel units; obsolete name of <see cref="ViewboxPixels"/>.</summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewBox">Viewbox rectangle.</param>
    /// <returns>The image for chaining.</returns>
    [Obsolete("Use ViewboxPixels.")]
    public static Image ViewBoxPixels(this Image image, Rect? viewBox)
    {
        image.Viewbox = viewBox;
        image.ViewboxUnits = ImageViewboxUnits.Pixels;
        return image;
    }

    /// <summary>Sets the viewbox with relative units; obsolete name of <see cref="ViewboxRelative"/>.</summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewBox">Viewbox rectangle.</param>
    /// <returns>The image for chaining.</returns>
    [Obsolete("Use ViewboxRelative.")]
    public static Image ViewBoxRelative(this Image image, Rect? viewBox)
    {
        image.Viewbox = viewBox;
        image.ViewboxUnits = ImageViewboxUnits.RelativeToBoundingBox;
        return image;
    }

    /// <summary>Sets the viewbox units; obsolete name of the ViewboxUnits extension.</summary>
    /// <param name="image">Target image.</param>
    /// <param name="units">Viewbox units.</param>
    /// <returns>The image for chaining.</returns>
    [Obsolete("Use ViewboxUnits.")]
    public static Image ViewBoxUnits(this Image image, ImageViewBoxUnits units)
    {
        image.ViewboxUnits = (ImageViewboxUnits)units;
        return image;
    }
}
