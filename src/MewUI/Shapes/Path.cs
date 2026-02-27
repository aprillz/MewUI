using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Shapes;

/// <summary>
/// Renders an arbitrary <see cref="PathGeometry"/>.
/// </summary>
public class Path : Shape
{
    /// <summary>
    /// Gets or sets the geometry that defines this path.
    /// </summary>
    public PathGeometry? Data
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <inheritdoc/>
    protected override PathGeometry? GetDefiningGeometry() => Data;
}
