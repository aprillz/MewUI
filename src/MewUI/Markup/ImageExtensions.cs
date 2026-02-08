using System.Reflection;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extensions for images.
/// </summary>
public static class ImageExtensions
{
    /// <summary>
    /// Sets the image source.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="source">Image source.</param>
    /// <returns>The image for chaining.</returns>
    public static Image Source(this Image image, IImageSource? source)
    {
        image.Source = source;
        return image;
    }

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
    /// Sets the stretch mode.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="stretch">Stretch mode.</param>
    /// <returns>The image for chaining.</returns>
    public static Image StretchMode(this Image image, ImageStretch stretch)
    {
        image.StretchMode = stretch;
        return image;
    }

    /// <summary>
    /// Sets the image scale quality.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="mode">Scale quality mode.</param>
    /// <returns>The image for chaining.</returns>
    public static Image ImageScaleQuality(this Image image, ImageScaleQuality mode)
    {
        image.ImageScaleQuality = mode;
        return image;
    }

    /// <summary>
    /// Sets the view box and units.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="viewBox">View box rectangle.</param>
    /// <param name="units">View box units.</param>
    /// <returns>The image for chaining.</returns>
    public static Image ViewBox(this Image image, Rect? viewBox, ImageViewBoxUnits units = ImageViewBoxUnits.Pixels)
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
    public static Image ViewBoxPixels(this Image image, Rect? viewBox)
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
    public static Image ViewBoxRelative(this Image image, Rect? viewBox)
    {
        image.ViewBox = viewBox;
        image.ViewBoxUnits = ImageViewBoxUnits.RelativeToBoundingBox;
        return image;
    }

    /// <summary>
    /// Sets the horizontal alignment.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="alignment">Horizontal alignment.</param>
    /// <returns>The image for chaining.</returns>
    public static Image AlignmentX(this Image image, ImageAlignmentX alignment)
    {
        image.AlignmentX = alignment;
        return image;
    }

    /// <summary>
    /// Sets the vertical alignment.
    /// </summary>
    /// <param name="image">Target image.</param>
    /// <param name="alignment">Vertical alignment.</param>
    /// <returns>The image for chaining.</returns>
    public static Image AlignmentY(this Image image, ImageAlignmentY alignment)
    {
        image.AlignmentY = alignment;
        return image;
    }
}
