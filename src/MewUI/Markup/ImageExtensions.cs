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
}
