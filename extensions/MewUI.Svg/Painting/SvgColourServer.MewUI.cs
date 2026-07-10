using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgColourServer
{
    public override Brush GetBrush(SvgVisualElement styleOwner, ISvgRenderer renderer, float opacity, bool forStroke = false)
    {
        if (this == None)
        {
            return new SolidColorBrush(new Color(0, 0, 0, 0));
        }

        if (this == NotSet && forStroke)
        {
            return new SolidColorBrush(new Color(0, 0, 0, 0));
        }

        var color = Colour;
        var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return new SolidColorBrush(new Color(alpha, color.R, color.G, color.B));
    }
}
