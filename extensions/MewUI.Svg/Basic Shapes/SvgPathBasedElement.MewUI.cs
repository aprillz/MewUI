using Aprillz.MewUI;

namespace Svg;

public abstract partial class SvgPathBasedElement : SvgVisualElement
{
    public override Rect Bounds
    {
        get
        {
            var path = Path(null);
            if (path is null || path.IsEmpty)
            {
                return Rect.Empty;
            }

            var bounds = path.GetBounds();
            return TransformedBounds(bounds);
        }
    }
}
