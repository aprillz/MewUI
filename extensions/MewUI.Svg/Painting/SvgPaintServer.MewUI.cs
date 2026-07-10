using Aprillz.MewUI.Rendering;

namespace Svg;

public abstract partial class SvgPaintServer
{
    public virtual Brush GetBrush(SvgVisualElement styleOwner, ISvgRenderer renderer, float opacity, bool forStroke = false)
    {
        return null;
    }
}
