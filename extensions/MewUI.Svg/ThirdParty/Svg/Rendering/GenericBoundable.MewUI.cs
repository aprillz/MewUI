using Aprillz.MewUI;

namespace Svg;

internal sealed class GenericBoundable : ISvgBoundable
{
    public static readonly GenericBoundable Empty = new(Rect.Empty);

    public GenericBoundable(double x, double y, double width, double height)
        : this(new Rect(x, y, width, height))
    {
    }

    public GenericBoundable(Rect bounds)
    {
        Bounds = bounds;
    }

    public Point Location => Bounds.Position;

    public Size Size => Bounds.Size;

    public Rect Bounds { get; }
}
