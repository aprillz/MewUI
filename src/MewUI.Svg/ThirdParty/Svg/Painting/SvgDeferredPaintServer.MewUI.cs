using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgDeferredPaintServer
{
    public override IBrush GetBrush(SvgVisualElement styleOwner, ISvgRenderer renderer, float opacity, bool forStroke = false)
    {
        EnsureServer(styleOwner);
        return (_concreteServer ?? _fallbackServer ?? NotSet)?.GetBrush(styleOwner, renderer, opacity, forStroke);
    }
}
