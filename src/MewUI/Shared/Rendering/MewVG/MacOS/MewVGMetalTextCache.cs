using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering.CoreText;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// The text drawing state of one Metal MewVG context: a glyph atlas, the quads of texts laid out against it, the
/// CoreText fonts its glyphs were drawn from, and the ink boxes of those glyphs.
/// </summary>
internal sealed partial class MewVGMetalTextCache : IDisposable
{
    // Glyph positions are kept to a quarter pixel each way, so a glyph has at most sixteen atlas entries.
    private const int SUBPIXEL_STEPS = 4;
    // Ink boxes kept apart from the atlas, since measuring one costs more than drawing the glyph; past this count
    // they are measured again.
    private const int MAX_GLYPH_BOUNDS = 65536;

    private readonly MewVGMetal _vg;
    private readonly MewVGGlyphRunCache<GlyphRunKey> _runs = new();
    private readonly MewVGGlyphRunBuilder _builder = new();
    private readonly AtlasLineTarget _target;
    // CoreText fonts seen by the atlas, retained so a glyph keyed by a font is never confused with another font
    // reusing its address.
    private readonly Dictionary<nint, FontToken> _fonts = new();
    private readonly Dictionary<GlyphBoundsKey, CoreTextGlyphBounds> _glyphBounds = new();
    private readonly CoreTextText.GlyphRasterizer _rasterizer = new();
    private MewVGGlyphAtlas? _atlas;
    private bool _disposed;

    public MewVGMetalTextCache(MewVGMetal vg)
    {
        _vg = vg;
        _target = new AtlasLineTarget(this);
    }

    private MewVGGlyphAtlas Atlas => _atlas ??= new MewVGGlyphAtlas(_vg);

    /// <summary>
    /// The atlas quads of a text in a raster of the size <see cref="CoreTextText.Layout"/> reports for it, laid out
    /// now or kept from an earlier frame; null when the font cannot lay the text out.
    /// </summary>
    public MewVGGlyphRun? GetOrLayout(
        CoreTextFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        uint dpi,
        TextAlignment horizontalAlignment,
        TextWrapping wrapping,
        TextTrimming trimming,
        TextInkInsetPx inset,
        double letterSpacingPx,
        bool keep)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint fontRef = font.GetFontRef(dpi);
        var key = new GlyphRunKey(fontRef, string.GetHashCode(text), widthPx, heightPx, dpi, horizontalAlignment, wrapping,
            trimming, inset, letterSpacingPx);
        if (keep && _runs.TryGet(key, text, Atlas, out var kept))
        {
            return kept;
        }

        _builder.Reset();
        if (!CoreTextText.Layout(font, text, widthPx, heightPx, dpi, horizontalAlignment, TextAlignment.Top, wrapping,
                widthPx, trimming, inset, letterSpacingPx, _target.Begin(widthPx, heightPx, dpi, inset), out _, out _))
        {
            return null;
        }

        var run = _builder.ToRun();
        if (keep)
        {
            _runs.Store(key, text, run);
        }

        return run;
    }

    /// <summary>Uploads the glyphs added since the last call; called before each flush of draws.</summary>
    public void FlushGlyphUploads()
    {
        if (!_disposed)
        {
            _atlas?.FlushUploads();
        }
    }

    /// <summary>Called once a frame's draws are committed: pages past the atlas budget go again.</summary>
    public void ReleasePendingDeletes()
    {
        if (!_disposed)
        {
            _atlas?.EndFrame();
        }
    }

    /// <summary>Drops every kept text and glyph; only called between frames.</summary>
    public void Trim()
    {
        if (_disposed)
        {
            return;
        }

        _runs.Clear();
        _atlas?.Clear();
        _glyphBounds.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Trim();
        _atlas?.Dispose();
        _rasterizer.Dispose();
        foreach (var token in _fonts.Values)
        {
            CFRelease(token.FontRef);
        }

        _fonts.Clear();
        _disposed = true;
    }

    private CoreTextGlyphBounds BoundsFor(FontToken token, ushort glyph)
    {
        var key = new GlyphBoundsKey(token, glyph);
        if (!_glyphBounds.TryGetValue(key, out var bounds))
        {
            if (_glyphBounds.Count >= MAX_GLYPH_BOUNDS)
            {
                _glyphBounds.Clear();
            }

            bounds = CoreTextText.GetGlyphBounds(token.FontRef, glyph);
            _glyphBounds[key] = bounds;
        }

        return bounds;
    }

    private FontToken TokenFor(nint fontRef)
    {
        if (!_fonts.TryGetValue(fontRef, out var token))
        {
            token = new FontToken(CFRetain(fontRef));
            _fonts[fontRef] = token;
        }

        return token;
    }

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial nint CFRetain(nint cf);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint cf);

    /// <summary>A retained CoreText font, the identity the atlas keys its glyphs by.</summary>
    private sealed class FontToken(nint fontRef)
    {
        public nint FontRef { get; } = fontRef;
    }

    private readonly record struct GlyphBoundsKey(FontToken Font, ushort Glyph);

    private readonly record struct GlyphRunKey(
        nint FontRef,
        int TextHash,
        int WidthPx,
        int HeightPx,
        uint Dpi,
        TextAlignment HorizontalAlignment,
        TextWrapping Wrapping,
        TextTrimming Trimming,
        TextInkInsetPx Inset,
        double LetterSpacingPx);

    /// <summary>
    /// Turns the glyphs of the lines a CoreText layout places into atlas quads, drawing glyphs the atlas lacks at a
    /// quarter-pixel offset and cutting each quad to the raster.
    /// </summary>
    private sealed class AtlasLineTarget(MewVGMetalTextCache owner) : ICoreTextLineTarget, ICoreTextGlyphSink
    {
        private int _rasterWidthPx;
        private int _rasterHeightPx;
        private double _lineX;
        private double _lineBaselineTop;

        public AtlasLineTarget Begin(int widthPx, int heightPx, uint dpi, TextInkInsetPx inset)
        {
            (_rasterWidthPx, _rasterHeightPx) = CoreTextText.GetBitmapSizePx(Math.Max(1, widthPx), Math.Max(1, heightPx), dpi, inset);
            return this;
        }

        public void Line(nint ctFont, ReadOnlySpan<char> text, double x, double baselineTop)
        {
            _lineX = x;
            _lineBaselineTop = baselineTop;
            CoreTextText.ForEachLineGlyph(ctFont, text, this);
        }

        public void Glyph(nint font, ushort glyph, double x, double y)
        {
            double penX = _lineX + x;
            double penY = _lineBaselineTop + y;
            int wholeX = (int)Math.Floor(penX);
            int wholeY = (int)Math.Floor(penY);
            int stepX = (int)Math.Round((penX - wholeX) * SUBPIXEL_STEPS);
            int stepY = (int)Math.Round((penY - wholeY) * SUBPIXEL_STEPS);
            if (stepX == SUBPIXEL_STEPS)
            {
                wholeX++;
                stepX = 0;
            }

            if (stepY == SUBPIXEL_STEPS)
            {
                wholeY++;
                stepY = 0;
            }

            var atlas = owner.Atlas;
            var token = owner.TokenFor(font);
            uint atlasKey = glyph | ((uint)stepX << 16) | ((uint)stepY << 20);
            if (!atlas.TryGet(token, atlasKey, out var atlasGlyph))
            {
                if (!owner._rasterizer.TryRasterize(font, glyph, owner.BoundsFor(token, glyph), (double)stepX / SUBPIXEL_STEPS,
                        (double)stepY / SUBPIXEL_STEPS, out var bitmap) || bitmap.Width == 0)
                {
                    return;
                }

                bool added = bitmap.IsColor
                    ? atlas.TryAddColor(token, atlasKey, bitmap.Pixels, bitmap.Width, bitmap.Height, out atlasGlyph, bitmap.Left, bitmap.Top)
                    : atlas.TryAddCoverage(token, atlasKey, bitmap.Pixels, bitmap.Width, bitmap.Height, out atlasGlyph, bitmap.Left, bitmap.Top);
                if (!added)
                {
                    return;
                }
            }

            Add(atlasGlyph, wholeX + atlasGlyph.Left, wholeY + atlasGlyph.Top);
        }

        private void Add(in MewVGAtlasGlyph glyph, int left, int top)
        {
            int right = Math.Min(left + glyph.Width, _rasterWidthPx);
            int bottom = Math.Min(top + glyph.Height, _rasterHeightPx);
            int clippedLeft = Math.Max(left, 0);
            int clippedTop = Math.Max(top, 0);
            if (right <= clippedLeft || bottom <= clippedTop)
            {
                return;
            }

            owner._builder.Add(glyph, new MewVGImageQuad(
                clippedLeft,
                clippedTop,
                right - clippedLeft,
                bottom - clippedTop,
                glyph.X + (clippedLeft - left),
                glyph.Y + (clippedTop - top),
                right - clippedLeft,
                bottom - clippedTop));
        }
    }
}
