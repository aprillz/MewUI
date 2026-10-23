using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Win32;

/// <summary>
/// Text capabilities a Win32 font exposes to the GDI and MewVG backends, independent of the
/// engine behind it. Implementations own whatever device state they need, so no device context
/// crosses this contract.
/// </summary>
internal interface IWin32TextFace
{
    /// <summary>
    /// Ink of a single-line run that falls outside its advance box and the font's ascent/descent
    /// band, in device-independent units. <see cref="TextInkOverhang.None"/> when unavailable.
    /// </summary>
    TextInkOverhang GetRunInkOverhang(ReadOnlySpan<char> text);

    /// <summary>
    /// Writes the cumulative advance after each UTF-16 code unit, in device-independent units.
    /// Returns false when <paramref name="destination"/> is shorter than the text or the face
    /// cannot answer, leaving it untouched.
    /// </summary>
    bool TryGetPrefixAdvances(ReadOnlySpan<char> text, double dpiScale, Span<double> destination);

    /// <summary>
    /// Size of <paramref name="text"/> in device-independent units. An infinite
    /// <paramref name="maxWidthDip"/> leaves the run unconstrained; line breaks in the text are
    /// honoured either way.
    /// </summary>
    Size Measure(ReadOnlySpan<char> text, double maxWidthDip, TextWrapping wrapping, double dpiScale);

    /// <summary>
    /// Baseline, in device-independent units from the top of a top-aligned single-line box, on which
    /// <see cref="TryRasterize"/> lands the first line of <paramref name="text"/>. False when the face
    /// draws every run on the font's own baseline, so a caller needs no correction.
    /// </summary>
    bool TryGetRunBaseline(ReadOnlySpan<char> text, double dpiScale, out double baseline);

    /// <summary>
    /// Rasterizes a text box into a straight-alpha BGRA bitmap. Returns false when the face has no
    /// rasterizer of its own, leaving the backend to use its existing path.
    /// </summary>
    bool TryRasterize(ReadOnlySpan<char> text, in Win32TextRasterizeRequest request, out TextBitmap bitmap);

    /// <summary>
    /// Rasterizes a text box into per-channel coverage, which a backend drawing over known pixels
    /// blends itself to keep subpixel antialiasing. Returns false when the face has no such
    /// rasterizer, leaving the caller to use <see cref="TryRasterize"/>.
    /// </summary>
    bool TryRasterizeSubpixel(ReadOnlySpan<char> text, in Win32TextRasterizeRequest request,
        out Win32TextCoverage coverage);
}

/// <summary>Raster placement shared by the backends that draw an <see cref="IWin32TextFace"/> run from its bitmap.</summary>
internal static class Win32TextFaceRuns
{
    /// <summary>
    /// How far a run's raster is pulled back to the font's own baseline, the one the text engine placed
    /// the run on; a fallback glyph makes the face lay the line out on a taller baseline. Zero when the
    /// face keeps the font's baseline or the text wraps.
    /// </summary>
    public static double ResolveOriginOffsetY(ReadOnlySpan<char> text, IFont font, TextWrapping wrapping, double dpiScale)
    {
        if (wrapping != TextWrapping.NoWrap || font is not IWin32TextFace face
            || !face.TryGetRunBaseline(text, dpiScale, out double runBaseline))
        {
            return 0;
        }

        return font.Ascent - runBaseline;
    }

    /// <summary>The ink overhang of a run whose raster is shifted by <paramref name="originOffsetY"/>.</summary>
    public static TextInkOverhang ShiftInkOverhang(TextInkOverhang ink, double originOffsetY)
        => TextInkOverhang.FromEdges(ink.Left, ink.Top - originOffsetY, ink.Right, ink.Bottom + originOffsetY);
}

/// <summary>
/// Per-channel glyph coverage for a text box, three bytes per pixel in red, green, blue order.
/// Carries no colour: the caller blends it against whatever the destination already holds.
/// </summary>
internal readonly record struct Win32TextCoverage(int WidthPx, int HeightPx, byte[] Data);

/// <summary>
/// The box a run is rasterized into. <see cref="InkInset"/> grows the raster past the run box so
/// glyph ink that overhangs it is kept; the run box then sits inset inside the raster. A
/// <see cref="Buffer"/> large enough for the result is filled in place and aliased by it, so the
/// caller has to be done with the previous result before it rasterizes again.
/// </summary>
internal readonly record struct Win32TextRasterizeRequest(
    int WidthPx,
    int HeightPx,
    Color Color,
    TextAlignment HorizontalAlignment,
    TextAlignment VerticalAlignment,
    TextWrapping Wrapping,
    TextTrimming Trimming,
    double DpiScale,
    TextInkInsetPx InkInset = default,
    byte[]? Buffer = null);
