using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Abstract interface for brush resources used to paint areas.
/// <para>
/// Brushes are created by <see cref="IGraphicsFactory"/> and are backend-specific.
/// They must be disposed when no longer needed.
/// </para>
/// </summary>
public interface IBrush : IDisposable { }

/// <summary>
/// A brush that paints with a single solid <see cref="Color"/>.
/// </summary>
public interface ISolidColorBrush : IBrush
{
    /// <summary>Gets the brush color.</summary>
    Color Color { get; }
}

/// <summary>
/// Base interface for gradient brushes.
/// </summary>
public interface IGradientBrush : IBrush
{
    /// <summary>Gets the color stops that define the gradient.</summary>
    IReadOnlyList<GradientStop> Stops { get; }

    /// <summary>Gets how the gradient extends beyond its defined bounds.</summary>
    SpreadMethod SpreadMethod { get; }

    /// <summary>Gets the coordinate space used for gradient geometry.</summary>
    GradientUnits GradientUnits { get; }

    /// <summary>
    /// Gets an optional additional transform applied to the gradient geometry,
    /// or <see langword="null"/> for no additional transform.
    /// </summary>
    Matrix3x2? GradientTransform { get; }
}

/// <summary>
/// A brush that paints with a linear gradient between two points.
/// </summary>
public interface ILinearGradientBrush : IGradientBrush
{
    /// <summary>Gets the start point of the gradient.</summary>
    Point StartPoint { get; }

    /// <summary>Gets the end point of the gradient.</summary>
    Point EndPoint { get; }
}

/// <summary>
/// A brush that paints with a radial gradient centered at a focal point.
/// </summary>
public interface IRadialGradientBrush : IGradientBrush
{
    /// <summary>Gets the center of the gradient ellipse.</summary>
    Point Center { get; }

    /// <summary>
    /// Gets the focal point (gradient origin) from which the gradient radiates.
    /// In SVG this is the <c>fx</c>/<c>fy</c> attribute; defaults to <see cref="Center"/>.
    /// </summary>
    Point GradientOrigin { get; }

    /// <summary>Gets the X radius of the gradient ellipse.</summary>
    double RadiusX { get; }

    /// <summary>Gets the Y radius of the gradient ellipse.</summary>
    double RadiusY { get; }
}

/// <summary>
/// How an image brush extends its image beyond a single tile.
/// </summary>
public enum TileMode
{
    /// <summary>Do not tile — fill outside the image's destination rect is transparent.</summary>
    None,

    /// <summary>Repeat the tile on both axes.</summary>
    Tile,

    /// <summary>Repeat on X axis only; Y axis is clamped/transparent.</summary>
    TileX,

    /// <summary>Repeat on Y axis only; X axis is clamped/transparent.</summary>
    TileY,
}

/// <summary>
/// A brush that fills a region by tiling an image.
/// <para>
/// The image is drawn into <see cref="DestinationRect"/>; outside that rect the
/// image is repeated according to <see cref="TileMode"/>. <see cref="SourceRect"/>
/// selects which part of the image is used as the tile (full image by default).
/// </para>
/// </summary>
public interface IImageBrush : IBrush
{
    /// <summary>Gets the image that provides the tile.</summary>
    IImage Image { get; }

    /// <summary>Gets the region within <see cref="Image"/> to use as the tile (in DIPs, image coordinates).</summary>
    Rect SourceRect { get; }

    /// <summary>Gets the destination rectangle for one tile (in DIPs, local coordinates).</summary>
    Rect DestinationRect { get; }

    /// <summary>Gets how the tile extends beyond <see cref="DestinationRect"/>.</summary>
    TileMode TileMode { get; }

    /// <summary>Gets the overall opacity multiplier applied to the tile.</summary>
    double Opacity { get; }

    /// <summary>
    /// Gets an optional transform applied to the tile geometry before rendering,
    /// or <see langword="null"/> for no additional transform. SVG <c>patternTransform</c>
    /// maps here.
    /// </summary>
    Matrix3x2? Transform { get; }
}
