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
