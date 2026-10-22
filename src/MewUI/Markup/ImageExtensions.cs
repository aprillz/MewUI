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
    /// Sets the view box and units.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewBox">View box rectangle.</param>
    /// <param name="units">View box units.</param>
    /// <returns>The image for chaining.</returns>
    public static Image ViewBox(
        this Image image,
        [FluentSetsMember(nameof(Image.ViewBox))] Rect? viewBox,
        [FluentSetsMember(nameof(Image.ViewBoxUnits))] ImageViewBoxUnits units = ImageViewBoxUnits.Pixels)
    {
        image.ViewBox = viewBox;
        image.ViewBoxUnits = units;
        return image;
    }

    /// <summary>
    /// Sets the view box with pixel units.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewBox">View box rectangle.</param>
    /// <returns>The image for chaining.</returns>
    [FluentSetsConstant(nameof(Image.ViewBoxUnits), ImageViewBoxUnits.Pixels)]
    public static Image ViewBoxPixels(this Image image, [FluentSetsMember(nameof(Image.ViewBox))] Rect? viewBox)
    {
        image.ViewBox = viewBox;
        image.ViewBoxUnits = ImageViewBoxUnits.Pixels;
        return image;
    }

    /// <summary>
    /// Sets the view box with relative units.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewBox">View box rectangle.</param>
    /// <returns>The image for chaining.</returns>
    [FluentSetsConstant(nameof(Image.ViewBoxUnits), ImageViewBoxUnits.RelativeToBoundingBox)]
    public static Image ViewBoxRelative(this Image image, [FluentSetsMember(nameof(Image.ViewBox))] Rect? viewBox)
    {
        image.ViewBox = viewBox;
        image.ViewBoxUnits = ImageViewBoxUnits.RelativeToBoundingBox;
        return image;
    }
}
