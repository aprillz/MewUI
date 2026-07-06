using System.Diagnostics;
using System.Numerics;
using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgDocument
{
    internal SvgFontManager? FontManager { get; private set; }

    private static int GetWin32SystemDpi() => 96;

    public static SvgDocument Parse(string svg)
    {
        var doc = FromSvg<SvgDocument>(svg);
        return doc;
    }

    /// <summary>Parses SVG content and stamps <see cref="BaseUri"/> with <paramref name="baseUri"/>
    /// so SvgImage/SvgUse can resolve relative href values (e.g. <c>../images/foo.png</c>).
    /// Use this overload when the SVG text was loaded from a known on-disk path -
    /// <see cref="Parse(string)"/> alone has no anchor for relative href resolution.</summary>
    public static SvgDocument Parse(string svg, Uri baseUri)
    {
        var doc = FromSvg<SvgDocument>(svg);
        if (baseUri is not null)
        {
            doc.BaseUri = baseUri;
        }
        return doc;
    }

    /// <summary>Loads an SVG document from <paramref name="path"/> and stamps <see cref="BaseUri"/>
    /// with the file's absolute URI. Equivalent to <see cref="Open(string)"/> but lives on
    /// the MewUI surface for discoverability alongside <see cref="Parse(string)"/>.</summary>
    public new static SvgDocument Load(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }
        var svg = File.ReadAllText(path);
        return Parse(svg, new Uri(Path.GetFullPath(path)));
    }

    public double ViewBoxWidth
    {
        get
        {
            var viewBox = ViewBox;
            if (viewBox != SvgViewBox.Empty && viewBox.Width > 0)
            {
                return viewBox.Width;
            }

            // GetDimensions() implements the SVG spec resolution: Width/Height default
            // to 100%, and on the document fragment 100% is resolved against the union
            // of children's bounds. Don't read Width.Value directly - that returns 100
            // (the percent's numeric value) and yields a 1:1 viewport for SVGs without
            // explicit width/height (e.g. `<svg><rect width="120" height="60"/>...</svg>`).
            var size = GetDimensions();
            if (size.Width > 0)
            {
                return size.Width;
            }
            return 100;
        }
    }

    public double ViewBoxHeight
    {
        get
        {
            var viewBox = ViewBox;
            if (viewBox != SvgViewBox.Empty && viewBox.Height > 0)
            {
                return viewBox.Height;
            }

            var size = GetDimensions();
            if (size.Height > 0)
            {
                return size.Height;
            }
            return 100;
        }
    }

    public void Render(IGraphicsContext context, Rect destRect)
    {
        var factory = Application.IsRunning
            ? Application.Current.GraphicsFactory
            : Application.DefaultGraphicsFactory;

        using var renderer = new MewSvgRenderer(factory, context);
        var docSize = GetDimensions();
        var sourceWidth = Math.Max(1, docSize.Width);
        var sourceHeight = Math.Max(1, docSize.Height);
        var scaleX = destRect.Width / sourceWidth;
        var scaleY = destRect.Height / sourceHeight;

        renderer.Save();
        try
        {
            renderer.Transform =
                Matrix3x2.CreateScale((float)scaleX, (float)scaleY) *
                Matrix3x2.CreateTranslation((float)destRect.X, (float)destRect.Y) *
                renderer.Transform;
            Draw(renderer, new GenericBoundable(0, 0, sourceWidth, sourceHeight));
        }
        finally
        {
            // DrainToBaseline (not just Restore) so a mid-render exception that skipped some
            // element's own cleanup can't leave the context with leftover pushed state.
            renderer.DrainToBaseline();
        }
    }

    private static double GetFallbackExtent(SvgUnit unit, double defaultValue)
    {
        if (!unit.IsEmpty && !unit.IsNone && unit.Value > 0)
        {
            return unit.Value;
        }

        return defaultValue;
    }

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
