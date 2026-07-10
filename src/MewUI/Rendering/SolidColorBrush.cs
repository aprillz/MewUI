namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A brush that paints with a single solid <see cref="Color"/>.
/// </summary>
public sealed class SolidColorBrush : Brush, IEquatable<SolidColorBrush>
{
    /// <summary>Gets the brush color.</summary>
    public Color Color { get; }

    /// <summary>Creates a brush that paints with <paramref name="color"/>.</summary>
    public SolidColorBrush(Color color) => Color = color;

    /// <inheritdoc/>
    public bool Equals(SolidColorBrush? other) => other is not null && Color.Equals(other.Color);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as SolidColorBrush);

    /// <inheritdoc/>
    public override int GetHashCode() => Color.GetHashCode();
}
