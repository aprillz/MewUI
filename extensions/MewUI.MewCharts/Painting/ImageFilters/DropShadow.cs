namespace Aprillz.MewUI.MewCharts.Painting.ImageFilters;

/// <summary>
/// Draws a blurred, offset copy of the shape behind it.
/// </summary>
/// <remarks>
/// Rectangles, rounded rectangles, ellipses and pop-ups cast a shadow of their own shape; other
/// geometries cast the shadow of their bounding rectangle.
/// </remarks>
/// <param name="dx">The horizontal offset in DIPs.</param>
/// <param name="dy">The vertical offset in DIPs.</param>
/// <param name="sigmaX">The horizontal blur standard deviation in DIPs.</param>
/// <param name="sigmaY">The vertical blur standard deviation in DIPs.</param>
/// <param name="color">The shadow color.</param>
public class DropShadow(float dx, float dy, float sigmaX, float sigmaY, Color color) : ImageFilter
{
    /// <summary>Gets the horizontal offset in DIPs.</summary>
    public float Dx { get; } = dx;

    /// <summary>Gets the vertical offset in DIPs.</summary>
    public float Dy { get; } = dy;

    /// <summary>Gets the horizontal blur standard deviation in DIPs.</summary>
    public float SigmaX { get; } = sigmaX;

    /// <summary>Gets the vertical blur standard deviation in DIPs.</summary>
    public float SigmaY { get; } = sigmaY;

    /// <summary>Gets the shadow color.</summary>
    public Color Color { get; } = color;

    /// <inheritdoc/>
    public override ImageFilter Clone() => new DropShadow(Dx, Dy, SigmaX, SigmaY, Color);
}
