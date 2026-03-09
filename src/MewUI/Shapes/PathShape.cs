using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Renders an arbitrary <see cref="PathGeometry"/>.
/// </summary>
public class PathShape : Shape
{
    public static readonly MewProperty<PathGeometry?> DataProperty =
        MewProperty<PathGeometry?>.Register<PathShape>(nameof(Data), null, MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Gets or sets the geometry that defines this path.
    /// </summary>
    public PathGeometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc/>
    protected override PathGeometry? GetDefiningGeometry() => Data;
}
