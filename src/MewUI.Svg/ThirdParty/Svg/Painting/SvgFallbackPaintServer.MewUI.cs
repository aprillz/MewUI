using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgFallbackPaintServer
{
    public override IBrush GetBrush(SvgVisualElement styleOwner, ISvgRenderer renderer, float opacity, bool forStroke = false)
    {
        var primary = _primary?.GetBrush(styleOwner, renderer, opacity, forStroke);
        if (primary is not null)
        {
            return primary;
        }

        if (_fallbacks is null)
        {
            return null;
        }

        foreach (var fallback in _fallbacks)
        {
            var brush = fallback.GetBrush(styleOwner, renderer, opacity, forStroke);
            if (brush is not null)
            {
                return brush;
            }
        }

        return null;
    }
}
