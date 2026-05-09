using Aprillz.MewUI;

namespace Svg
{
    public interface ISvgBoundable
    {
        Point Location { get; }
        Size Size { get; }
        Rect Bounds { get; }
    }
}
