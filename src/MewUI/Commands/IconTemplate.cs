using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Describes a square icon's layout size in DIPs and raster target size in device pixels.
/// </summary>
/// <param name="Dip">The layout size in DIPs.</param>
/// <param name="Pixel">The raster target size in device pixels.</param>
public readonly record struct IconTemplateSize(double Dip, int Pixel);

/// <summary>
/// Reusable icon presentation that creates an independent visual for the size requested by a
/// menu, toolbar or other command presenter.
/// </summary>
/// <remarks>
/// Each invocation must return a new, parentless element; visual instances cannot be shared by
/// simultaneous presenters. Non-visual resources such as image sources and frozen geometries may
/// be shared by the returned elements.
/// </remarks>
public sealed class IconTemplate
{
    private readonly Func<IconTemplateSize, FrameworkElement> _build;

    /// <summary>
    /// Creates a template from a callback that builds a new icon visual for a requested size.
    /// </summary>
    public IconTemplate(Func<IconTemplateSize, FrameworkElement> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    /// <summary>
    /// Builds one icon visual for the requested square presentation size.
    /// </summary>
    /// <remarks>
    /// <see cref="IconTemplateSize.Dip"/> is the layout size. Raster factories can use
    /// <see cref="IconTemplateSize.Pixel"/> to select an appropriately sized source.
    /// </remarks>
    public FrameworkElement Build(IconTemplateSize size)
    {
        if (!double.IsFinite(size.Dip) || size.Dip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Icon size must be a finite positive value.");
        }
        if (size.Pixel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Icon pixel size must be positive.");
        }

        var element = _build(size)
            ?? throw new InvalidOperationException("The icon template returned null.");
        if (element.Parent != null)
        {
            throw new InvalidOperationException("The icon template must return a new parentless element.");
        }

        return element;
    }

    internal static IconTemplateSize ResolveSize(double dip, double dpiScale)
    {
        if (!double.IsFinite(dip) || dip <= 0)
            throw new ArgumentOutOfRangeException(nameof(dip));
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpiScale));

        double pixels = Math.Ceiling(dip * dpiScale);
        if (pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dip), "Resolved icon pixel size is too large.");

        return new IconTemplateSize(dip, Math.Max(1, (int)pixels));
    }
}
