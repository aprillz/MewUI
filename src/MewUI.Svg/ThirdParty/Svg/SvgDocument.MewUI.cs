using System;

namespace Svg;

public partial class SvgDocument
{
    internal SvgFontManager? FontManager { get; private set; }

    private void Draw(ISvgRenderer renderer, ISvgBoundable boundable)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(boundable);

        using (FontManager = new SvgFontManager())
        {
            renderer.SetBoundable(boundable);
            try
            {
                Render(renderer);
            }
            finally
            {
                renderer.PopBoundable();
                FontManager = null;
            }
        }
    }

    public void Draw(ISvgRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        Draw(renderer, this);
    }
}
