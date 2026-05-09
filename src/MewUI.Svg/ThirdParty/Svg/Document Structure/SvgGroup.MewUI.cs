using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgGroup
{
    public override PathGeometry Path(ISvgRenderer renderer)
    {
        return GetPaths(this, renderer);
    }

    public override Rect Bounds
    {
        get
        {
            var bounds = Rect.Empty;
            foreach (var child in Children)
            {
                if (child is not SvgVisualElement visual)
                {
                    continue;
                }

                var childBounds = visual.Bounds;
                if (childBounds.IsEmpty)
                {
                    continue;
                }

                bounds = bounds.IsEmpty ? childBounds : Union(bounds, childBounds);
            }

            return TransformedBounds(bounds);
        }
    }

    private static Rect Union(Rect a, Rect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }
}
