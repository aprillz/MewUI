namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Immutable, backend-agnostic description of how a region is painted.
/// <para>
/// A brush owns no native resources and is never disposed; instances are safe to
/// share across threads and to reuse indefinitely. Concrete brushes implement value
/// equality, so descriptors with the same values compare equal.
/// </para>
/// </summary>
public abstract class Brush
{
    // private protected: the brush hierarchy is closed to this assembly so the render
    // bridge can pattern-match every concrete brush type exhaustively.
    private protected Brush() { }

    /// <summary>Wraps a <see cref="Color"/> as a <see cref="SolidColorBrush"/>.</summary>
    public static implicit operator Brush(Color color) => new SolidColorBrush(color);
}
