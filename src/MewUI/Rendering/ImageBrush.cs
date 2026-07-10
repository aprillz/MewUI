using System.Numerics;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A brush that fills a region by tiling an image.
/// <para>
/// The image is drawn into <see cref="DestinationRect"/>; outside that rect the
/// image is repeated according to <see cref="TileMode"/>. <see cref="SourceRect"/>
/// selects which part of the image is used as the tile (full image by default).
/// </para>
/// <para>
/// The brush only references <see cref="Image"/>; it does not own it. Keeping the
/// image alive for at least as long as the brush is the caller's responsibility.
/// </para>
/// </summary>
public sealed class ImageBrush : Brush, IEquatable<ImageBrush>
{
    /// <summary>Gets the image that provides the tile.</summary>
    public IImage Image { get; }

    /// <summary>Gets the region within <see cref="Image"/> to use as the tile (in DIPs, image coordinates).</summary>
    public Rect SourceRect { get; }

    /// <summary>Gets the destination rectangle for one tile (in DIPs, local coordinates).</summary>
    public Rect DestinationRect { get; }

    /// <summary>Gets how the tile extends beyond <see cref="DestinationRect"/>.</summary>
    public TileMode TileMode { get; }

    /// <summary>Gets the overall opacity multiplier applied to the tile.</summary>
    public double Opacity { get; }

    /// <summary>
    /// Gets an optional transform applied to the tile geometry before rendering,
    /// or <see langword="null"/> for no additional transform. SVG <c>patternTransform</c>
    /// maps here.
    /// </summary>
    public Matrix3x2? Transform { get; }

    /// <summary>Creates an image brush that tiles <paramref name="image"/> to fill a region.</summary>
    /// <param name="image">Source image supplying the tile. Referenced, not owned.</param>
    /// <param name="sourceRect">Region within <paramref name="image"/> to use as the tile (in DIPs, image coordinates).</param>
    /// <param name="destinationRect">Destination rectangle for one tile (in DIPs, local coordinates).</param>
    /// <param name="tileMode">Tile extension mode beyond <paramref name="destinationRect"/>.</param>
    /// <param name="opacity">Overall opacity multiplier.</param>
    /// <param name="transform">Optional pre-fill transform applied to the tile geometry.</param>
    public ImageBrush(
        IImage image,
        Rect sourceRect,
        Rect destinationRect,
        TileMode tileMode = TileMode.Tile,
        double opacity = 1.0,
        Matrix3x2? transform = null)
    {
        Image = image;
        SourceRect = sourceRect;
        DestinationRect = destinationRect;
        TileMode = tileMode;
        Opacity = opacity;
        Transform = transform;
    }

    /// <inheritdoc/>
    public bool Equals(ImageBrush? other) =>
        other is not null &&
        ReferenceEquals(Image, other.Image) &&
        SourceRect.Equals(other.SourceRect) &&
        DestinationRect.Equals(other.DestinationRect) &&
        TileMode == other.TileMode &&
        Opacity.Equals(other.Opacity) &&
        Nullable.Equals(Transform, other.Transform);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ImageBrush);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        RuntimeHelpers.GetHashCode(Image),
        SourceRect,
        DestinationRect,
        TileMode,
        Opacity,
        Transform);
}
