using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgPatternServer
{
    // Per-pattern tile cache. The pattern's rendered bitmap is invariant for a given set
    // of inputs (tile pixel size, viewBox, patternContentUnits, content bounds for OBB).
    // Without this, every frame re-rendered the pattern children into a fresh offscreen
    // bitmap — for SVGs with embedded image children (Highcharts patterns etc.) the
    // PNG decode + GPU upload + tile draw was ~80 ms even after caching the decoded
    // image at SvgImage-element level. Cached values live for the SvgPatternServer
    // element's lifetime; the document is otherwise long-lived.
    private int _cachedTilePixelWidth;
    private int _cachedTilePixelHeight;
    private double _cachedTileScaleX;
    private double _cachedTileScaleY;
    private double _cachedTileBoundsX;
    private double _cachedTileBoundsY;
    private double _cachedTileBoundsW;
    private double _cachedTileBoundsH;
    private SvgViewBox _cachedTileViewBox;
    private SvgCoordinateUnits _cachedTilePatternContentUnits;
    private IBitmapRenderTarget? _cachedTileTarget;
    private IImage? _cachedTileImage;

    /// <summary>
    /// Creates a tiling image brush by rendering this pattern's children to an offscreen
    /// bitmap at the computed tile size, then returning an <see cref="IImageBrush"/> that
    /// repeats the tile across the fill region. Ported from SvgPatternServer.Drawing.cs:
    /// same chain inheritance and coordinate-system handling (ObjectBoundingBox vs
    /// UserSpaceOnUse), but produces a MewUI brush instead of a GDI+ TextureBrush.
    /// </summary>
    public override IBrush? GetBrush(SvgVisualElement renderingElement, ISvgRenderer renderer, float opacity, bool forStroke = false)
    {
        // Walk the href chain to inherit attributes from an upstream pattern.
        var chain = new List<SvgPatternServer>();
        var curr = this;
        do
        {
            chain.Add(curr);
            curr = SvgDeferredPaintServer.TryGet<SvgPatternServer>(curr.InheritGradient, renderingElement);
        } while (curr is not null);

        var firstChildren = chain.Find(p => p.Children.Count > 0);
        if (firstChildren is null)
        {
            return null;
        }

        var firstX = chain.Find(p => p.X != SvgUnit.None);
        var firstY = chain.Find(p => p.Y != SvgUnit.None);
        var firstWidth = chain.Find(p => p.Width != SvgUnit.None);
        var firstHeight = chain.Find(p => p.Height != SvgUnit.None);
        if (firstWidth is null || firstHeight is null)
        {
            return null;
        }

        var firstPatternUnit = chain.Find(p => p._patternUnits.HasValue);
        var firstPatternContentUnit = chain.Find(p => p._patternContentUnits.HasValue);
        var firstViewBox = chain.Find(p => p.ViewBox != SvgViewBox.Empty);

        var xUnit = firstX is null ? new SvgUnit(0f) : firstX.X;
        var yUnit = firstY is null ? new SvgUnit(0f) : firstY.Y;
        var widthUnit = firstWidth.Width;
        var heightUnit = firstHeight.Height;

        var patternUnits = firstPatternUnit is null ? SvgCoordinateUnits.ObjectBoundingBox : firstPatternUnit.PatternUnits;
        var patternContentUnits = firstPatternContentUnit is null ? SvgCoordinateUnits.UserSpaceOnUse : firstPatternContentUnit.PatternContentUnits;
        var viewBox = firstViewBox is null ? SvgViewBox.Empty : firstViewBox.ViewBox;

        bool isPatternObjectBoundingBox = patternUnits == SvgCoordinateUnits.ObjectBoundingBox;
        try
        {
            if (isPatternObjectBoundingBox)
            {
                renderer.SetBoundable(renderingElement);
            }

            double x = xUnit.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this);
            double y = yUnit.ToDeviceValue(renderer, UnitRenderingType.Vertical, this);
            double width = widthUnit.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this);
            double height = heightUnit.ToDeviceValue(renderer, UnitRenderingType.Vertical, this);

            if (isPatternObjectBoundingBox)
            {
                // Unitless pattern dimensions are interpreted as fractions of the referencing
                // element's bounding box; percentages are already in device units.
                var bounds = renderer.GetBoundable().Bounds;
                if (xUnit.Type != SvgUnitType.Percentage) x *= bounds.Width;
                if (yUnit.Type != SvgUnitType.Percentage) y *= bounds.Height;
                if (widthUnit.Type != SvgUnitType.Percentage) width *= bounds.Width;
                if (heightUnit.Type != SvgUnitType.Percentage) height *= bounds.Height;
                x += bounds.X;
                y += bounds.Y;
            }

            if (width <= 0.0 || height <= 0.0)
            {
                return null;
            }

            var factory = renderer.GraphicsFactory;
            // Render tile at 1:1 (no DPI oversampling). Matches SvgPatternServer.Drawing.cs
            // reference (integer bitmap size). Rendering at target DPI and then scaling the
            // brush back down introduces sub-pixel tile boundaries, which appear as visible
            // seams with LINEAR wrap sampling whenever the tile's first/last pixel colors
            // differ (e.g. anti-aliased edges of the pattern content's background rect).
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height));

            // Clamp tile resolution. Pattern dimensions can blow up at large
            // window sizes (a percentage-based pattern in a fullscreen view
            // can demand a multi-thousand pixel tile). If FBO creation fails,
            // the graphics context throws mid-frame and main rendering loses
            // every draw queued after the pattern. See SvgFilter.MewUI for
            // the same rationale.
            const int maxOffscreenExtent = 4096;
            if (pixelWidth > maxOffscreenExtent || pixelHeight > maxOffscreenExtent)
            {
                double shrink = (double)maxOffscreenExtent / Math.Max(pixelWidth, pixelHeight);
                pixelWidth = Math.Max(1, (int)(pixelWidth * shrink));
                pixelHeight = Math.Max(1, (int)(pixelHeight * shrink));
            }

            // Cache check — reuse the rendered tile when the inputs that affect its pixels
            // haven't changed. patternUnits / x / y don't affect the tile pixels (only the
            // brush's destinationRect), so they're not part of the key. patternTransform
            // and opacity are applied at brush construction, also not part of the key.
            var contentBoundsForOBB = patternContentUnits == SvgCoordinateUnits.ObjectBoundingBox
                ? renderer.GetBoundable().Bounds
                : default;
            bool cacheHit = _cachedTileImage is not null
                && _cachedTileTarget is not null
                && _cachedTilePixelWidth == pixelWidth
                && _cachedTilePixelHeight == pixelHeight
                && _cachedTileScaleX == width
                && _cachedTileScaleY == height
                && _cachedTileViewBox.Equals(viewBox)
                && _cachedTilePatternContentUnits == patternContentUnits
                && _cachedTileBoundsX == contentBoundsForOBB.X
                && _cachedTileBoundsY == contentBoundsForOBB.Y
                && _cachedTileBoundsW == contentBoundsForOBB.Width
                && _cachedTileBoundsH == contentBoundsForOBB.Height;

            if (!cacheHit)
            {
                _cachedTileImage?.Dispose();
                _cachedTileTarget?.Dispose();
                _cachedTileImage = null;
                _cachedTileTarget = null;

                var newTarget = factory.CreateBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale: 1.0);
                try
                {
                    newTarget.Clear(Aprillz.MewUI.Color.Transparent);

                    using (var tileContext = factory.CreateContext(newTarget))
                    {
                        tileContext.BeginFrame(newTarget);
                        try
                        {
                            using var tileRenderer = new MewSvgRenderer(factory, tileContext);
                            tileRenderer.SetBoundable(renderingElement);

                            if (viewBox != SvgViewBox.Empty)
                            {
                                tileContext.Scale(width / viewBox.Width, height / viewBox.Height);
                            }
                            else if (patternContentUnits == SvgCoordinateUnits.ObjectBoundingBox)
                            {
                                var contentBounds = tileRenderer.GetBoundable().Bounds;
                                tileContext.Scale(contentBounds.Width, contentBounds.Height);
                            }

                            foreach (var child in firstChildren.Children)
                            {
                                child.RenderElement(tileRenderer);
                            }
                        }
                        finally
                        {
                            tileContext.EndFrame();
                        }
                    }

                    _cachedTileImage = factory.CreateImageFromPixelSource(newTarget);
                    _cachedTileTarget = newTarget;
                    _cachedTilePixelWidth = pixelWidth;
                    _cachedTilePixelHeight = pixelHeight;
                    _cachedTileScaleX = width;
                    _cachedTileScaleY = height;
                    _cachedTileViewBox = viewBox;
                    _cachedTilePatternContentUnits = patternContentUnits;
                    _cachedTileBoundsX = contentBoundsForOBB.X;
                    _cachedTileBoundsY = contentBoundsForOBB.Y;
                    _cachedTileBoundsW = contentBoundsForOBB.Width;
                    _cachedTileBoundsH = contentBoundsForOBB.Height;
                    newTarget = null!;
                }
                finally
                {
                    newTarget?.Dispose();
                }
            }

            var image = _cachedTileImage!;
            var patternTransform = PatternTransform?.GetMatrix();

            // ownedResources is empty — the cached tile/image are owned by this server
            // and shared across brushes. The brush only references them; brush disposal
            // doesn't release them. They live until the document/server is collected.
            return factory.CreateImageBrush(
                image,
                sourceRect: new Rect(0, 0, image.PixelWidth, image.PixelHeight),
                destinationRect: new Rect(x, y, width, height),
                tileMode: TileMode.Tile,
                opacity: opacity,
                transform: patternTransform,
                ownedResources: null);
        }
        finally
        {
            if (isPatternObjectBoundingBox)
            {
                renderer.PopBoundable();
            }
        }
    }
}
