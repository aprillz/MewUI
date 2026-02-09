namespace Aprillz.MewUI;

/// <summary>
/// Represents a general point/rect transform between visual coordinate spaces.
/// This is a minimal WPF-compatible shape (translation-only for now).
/// </summary>
public abstract class GeneralTransform
{
    /// <summary>
    /// Transforms a point from the source coordinate space to the target coordinate space.
    /// </summary>
    /// <param name="point">The point to transform.</param>
    public abstract Point Transform(Point point);

    /// <summary>
    /// Transforms a bounding rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to transform.</param>
    public virtual Rect TransformBounds(Rect rect) =>
        new(Transform(rect.Position), rect.Size);

    /// <summary>
    /// Attempts to transform a point.
    /// </summary>
    /// <param name="inPoint">The input point.</param>
    /// <param name="result">The transformed point.</param>
    /// <returns><see langword="true"/> if the transformation succeeded.</returns>
    public virtual bool TryTransform(Point inPoint, out Point result)
    {
        result = Transform(inPoint);
        return true;
    }

    /// <summary>
    /// Gets the inverse transform.
    /// </summary>
    public abstract GeneralTransform Inverse { get; }
}

internal sealed class IdentityGeneralTransform : GeneralTransform
{
    public static readonly IdentityGeneralTransform Instance = new();

    private IdentityGeneralTransform() { }

    public override Point Transform(Point point) => point;

    public override Rect TransformBounds(Rect rect) => rect;

    public override GeneralTransform Inverse => this;
}

internal sealed class TranslateGeneralTransform : GeneralTransform
{
    private readonly double _dx;
    private readonly double _dy;

    public TranslateGeneralTransform(double dx, double dy)
    {
        _dx = dx;
        _dy = dy;
    }

    public override Point Transform(Point point) =>
        new(point.X + _dx, point.Y + _dy);

    public override Rect TransformBounds(Rect rect) =>
        rect.Offset(_dx, _dy);

    public override GeneralTransform Inverse =>
        new TranslateGeneralTransform(-_dx, -_dy);
}
