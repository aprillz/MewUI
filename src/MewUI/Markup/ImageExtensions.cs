using System.Reflection;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

public static class ImageExtensions
{
    public static Image Source(this Image image, IImageSource? source)
    {
        image.Source = source;
        return image;
    }

    public static Image SourceFile(this Image image, string path)
    {
        image.Source = ImageSource.FromFile(path);
        return image;
    }

    public static Image SourceResource(this Image image, Assembly assembly, string resourceName)
    {
        image.Source = ImageSource.FromResource(assembly, resourceName);
        return image;
    }

    public static Image SourceResource<TAnchor>(this Image image, string resourceName)
    {
        image.Source = ImageSource.FromResource<TAnchor>(resourceName);
        return image;
    }

    public static Image StretchMode(this Image image, ImageStretch stretch)
    {
        image.StretchMode = stretch;
        return image;
    }

    public static Image ViewBox(this Image image, Rect? viewBox, ImageViewBoxUnits units = ImageViewBoxUnits.Pixels)
    {
        image.ViewBox = viewBox;
        image.ViewBoxUnits = units;
        return image;
    }

    public static Image ViewBoxPixels(this Image image, Rect? viewBox)
    {
        image.ViewBox = viewBox;
        image.ViewBoxUnits = ImageViewBoxUnits.Pixels;
        return image;
    }

    public static Image ViewBoxRelative(this Image image, Rect? viewBox)
    {
        image.ViewBox = viewBox;
        image.ViewBoxUnits = ImageViewBoxUnits.RelativeToBoundingBox;
        return image;
    }

    public static Image AlignmentX(this Image image, ImageAlignmentX alignment)
    {
        image.AlignmentX = alignment;
        return image;
    }

    public static Image AlignmentY(this Image image, ImageAlignmentY alignment)
    {
        image.AlignmentY = alignment;
        return image;
    }
}
