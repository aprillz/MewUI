using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.CoreText;

internal static unsafe partial class CoreTextText
{
    private const int CTFontOrientationHorizontal = 0;
    // CoreGraphics bitmap format: BGRA premultiplied (little endian).
    private const uint kCGImageAlphaPremultipliedFirst = 2;

    private const uint kCGBitmapByteOrder32Little = 2u << 12;
    private const uint kCGBitmapInfo = kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little;

    public static TextBitmap Rasterize(
        CoreTextFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        uint dpi,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        int wrapWidthPx = 0,
        TextTrimming trimming = TextTrimming.None)
    {
        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        var ctFont = font.GetFontRef(dpi);
        if (text.IsEmpty || ctFont == 0)
        {
            return new TextBitmap(1, 1, new byte[4]);
        }

        // Extend the bitmap so glyphs at the text's trailing edge have room for
        // anti-aliasing / font smoothing. Text is still aligned to the original widthPx
        // boundary; the extra pixels are transparent and extend beyond it.
        int alignWidthPx = widthPx;
        int aaExtra = (int)Math.Ceiling(dpi / 96.0 * 2); // 2 DIP in device pixels
        widthPx += aaExtra;

        int stride = checked(widthPx * 4);
        var data = new byte[checked(stride * heightPx)];

        if (!RasterizeCore(ctFont, text, widthPx, heightPx, alignWidthPx,
                          color, horizontalAlignment, verticalAlignment,
                          wrapping, wrapWidthPx, trimming, data))
        {
            return new TextBitmap(1, 1, new byte[4]);
        }

        return new TextBitmap(widthPx, heightPx, data);
    }

    /// <summary>
    /// Rasterizes text into a caller-supplied buffer, avoiding the per-call <c>byte[]</c>
    /// allocation that <see cref="Rasterize"/> incurs. Used by owner-keyed text caches that
    /// reuse a single buffer per TextBlock instance even when the text content mutates.
    /// </summary>
    /// <remarks>
    /// <c>destBuffer</c> receives BGRA premultiplied pixels and must be at least
    /// <c>(widthPx + aaExtra) * heightPx * 4</c> bytes, where <c>aaExtra = ceil(dpi / 96 * 2)</c>;
    /// bytes beyond the rasterized region are not modified. <c>actualWidthPx</c> receives the
    /// AA-extended bitmap width — the same value <see cref="TextBitmap.WidthPx"/> would have on
    /// the equivalent <see cref="Rasterize"/> call — used for both the GPU upload extent and the
    /// image-pattern UV math. Returns false on any failure (bad font, empty text, buffer too
    /// small, CG init fail).
    /// </remarks>
    public static bool RasterizeInto(
        CoreTextFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        uint dpi,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        int wrapWidthPx,
        TextTrimming trimming,
        byte[] destBuffer,
        out int actualWidthPx,
        out int actualHeightPx)
    {
        actualWidthPx = 0;
        actualHeightPx = 0;
        ArgumentNullException.ThrowIfNull(destBuffer);

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        var ctFont = font.GetFontRef(dpi);
        if (text.IsEmpty || ctFont == 0)
        {
            return false;
        }

        int alignWidthPx = widthPx;
        int aaExtra = (int)Math.Ceiling(dpi / 96.0 * 2);
        widthPx += aaExtra;

        int stride = checked(widthPx * 4);
        int required = checked(stride * heightPx);
        if (destBuffer.Length < required)
        {
            return false;
        }

        if (!RasterizeCore(ctFont, text, widthPx, heightPx, alignWidthPx,
                          color, horizontalAlignment, verticalAlignment,
                          wrapping, wrapWidthPx, trimming, destBuffer))
        {
            return false;
        }

        actualWidthPx = widthPx;
        actualHeightPx = heightPx;
        return true;
    }

    /// <summary>
    /// Shared rasterization core used by both <see cref="Rasterize"/> and
    /// <see cref="RasterizeInto"/>. <paramref name="data"/> may be larger than
    /// <c>widthPx * heightPx * 4</c>; only the leading region is written.
    /// </summary>
    private static bool RasterizeCore(
        nint ctFont,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        int alignWidthPx,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        int wrapWidthPx,
        TextTrimming trimming,
        byte[] data)
    {
        int stride = widthPx * 4;

        fixed (byte* pData = data)
        {
            var colorspace = CGColorSpaceCreateDeviceRGB();
            if (colorspace == 0)
            {
                return false;
            }

            var ctx = CGBitmapContextCreate(pData, (nuint)widthPx, (nuint)heightPx, 8, (nuint)stride, colorspace, kCGBitmapInfo);
            CGColorSpaceRelease(colorspace);

            if (ctx == 0)
            {
                return false;
            }

            try
            {
                // Clear to transparent.
                CGContextClearRect(ctx, new CGRect(0, 0, widthPx, heightPx));

                // Enable anti-aliasing.
                CGContextSetShouldAntialias(ctx, true);
                CGContextSetAllowsAntialiasing(ctx, true);
                // Disable subpixel font smoothing: rendering onto a transparent background
                // causes RGB channel spread that produces color fringing and visually bolder
                // text when alpha-composited later. Use grayscale AA only.
                CGContextSetAllowsFontSmoothing(ctx, false);
                CGContextSetShouldSmoothFonts(ctx, false);
                CGContextSetShouldSubpixelPositionFonts(ctx, true);
                CGContextSetShouldSubpixelQuantizeFonts(ctx, true);

                // Fill with requested color.
                CGContextSetRGBFillColor(ctx, color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);

                // Layout.
                var metrics = GetLineMetrics(ctFont);
                if (wrapping == TextWrapping.Wrap && wrapWidthPx > 0 && alignWidthPx > wrapWidthPx)
                {
                    alignWidthPx = wrapWidthPx;
                }

                int lineWidthPx = wrapping == TextWrapping.Wrap && wrapWidthPx > 0 ? wrapWidthPx : alignWidthPx;

                // Build lines: untrimmed for comparison, trimmed for rendering.
                var linesNoTrim = BuildLines(ctFont, text, lineWidthPx, wrapping);
                var lines = trimming == TextTrimming.CharacterEllipsis
                    ? BuildLines(ctFont, text, lineWidthPx, wrapping, trimming)
                    : linesNoTrim;

                // Wrap + Ellipsis: if lines exceed available height, trim last visible line.
                bool wrapOverflowTrimmed = false;
                if (trimming == TextTrimming.CharacterEllipsis && wrapping != TextWrapping.NoWrap)
                {
                    int maxVisibleLines = Math.Max(1, (int)(heightPx / metrics.LineHeight));
                    if (lines.Count > maxVisibleLines)
                    {
                        lines.Lines.RemoveRange(maxVisibleLines, lines.Count - maxVisibleLines);

                        int lastIdx = lines.Count - 1;
                        var lastLine = lines[lastIdx];
                        if (lastLine.Length > 0)
                        {
                            var lineText = text.Slice(lastLine.Start, lastLine.Length);
                            double ellipsisW = MeasureRunWidth(ctFont, "...");
                            double maxTextW = Math.Max(0, lineWidthPx - ellipsisW);

                            // Trim text directly to fit within (lineWidthPx - ellipsisW).
                            // Don't use TrimLineWithEllipsis here because its "text fits" fast path
                            // omits the ellipsis width, which we always need for wrap overflow.
                            int trimLen = lastLine.Length;
                            double textW = MeasureRunWidth(ctFont, lineText);
                            if (textW > maxTextW)
                            {
                                // Estimation-based approach: avgCharWidth → estimatedLen
                                double avgChar = textW / trimLen;
                                trimLen = Math.Clamp((int)(maxTextW / avgChar), 0, lastLine.Length);
                                textW = trimLen > 0 ? MeasureRunWidth(ctFont, lineText.Slice(0, trimLen)) : 0;

                                if (textW > maxTextW)
                                {
                                    while (trimLen > 0 && textW > maxTextW)
                                    {
                                        trimLen--;
                                        textW = trimLen > 0 ? MeasureRunWidth(ctFont, lineText.Slice(0, trimLen)) : 0;
                                    }
                                }
                                else
                                {
                                    while (trimLen < lastLine.Length)
                                    {
                                        double next = MeasureRunWidth(ctFont, lineText.Slice(0, trimLen + 1));
                                        if (next > maxTextW) break;
                                        trimLen++;
                                        textW = next;
                                    }
                                }
                            }

                            lines.Lines[lastIdx] = new LineEntry(lastLine.Start, trimLen, textW + ellipsisW);
                        }
                        wrapOverflowTrimmed = true;
                    }
                }

                // CoreGraphics uses bottom-left origin. We'll compute baselines from top.
                double totalHeight = lines.Count * metrics.LineHeight;
                double topY = verticalAlignment switch
                {
                    TextAlignment.Center => (heightPx - totalHeight) / 2.0,
                    TextAlignment.Bottom => heightPx - totalHeight,
                    _ => 0.0
                };

                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    // Align text to the original content width (alignWidthPx), not the expanded
                    // bitmap width (widthPx). The extra bitmap pixels (aaExtra) provide room for
                    // anti-aliasing and glyph overhang on the trailing edge.
                    double x = horizontalAlignment switch
                    {
                        TextAlignment.Center => (alignWidthPx - line.Width) / 2.0,
                        TextAlignment.Right => Math.Max(0, alignWidthPx - line.Width),
                        _ => 0.0
                    };

                    // Baseline in "top-left" coordinates.
                    // Leading trim is handled centrally by GraphicsContextBase.
                    double baselineTop = topY + metrics.Ascent + i * metrics.LineHeight;
                    // Convert to CoreGraphics user space (bottom-left origin).
                    double baselineY = heightPx - baselineTop;

                    if (line.Length > 0)
                    {
                        DrawLineGlyphs(ctx, ctFont, text.Slice(line.Start, line.Length), x, baselineY);
                    }

                    // Detect if this line needs an ellipsis.
                    bool wasTrimmed;
                    if (wrapOverflowTrimmed && i == lines.Count - 1)
                        wasTrimmed = true;
                    else
                        wasTrimmed = i < linesNoTrim.Count && line.Length < linesNoTrim[i].Length;

                    if (wasTrimmed)
                    {
                        double textWidth = line.Length > 0 ? MeasureRunWidth(ctFont, text.Slice(line.Start, line.Length)) : 0;
                        DrawLineGlyphs(ctx, ctFont, "...", x + textWidth, baselineY);
                    }
                }

                return true;
            }
            finally
            {
                CGContextRelease(ctx);
            }
        }
    }

    public static Size Measure(CoreTextFont font, ReadOnlySpan<char> text, int maxWidthPx, TextWrapping wrapping)
    {
        uint dpi = 96;
        try
        {
            dpi = DpiHelper.GetSystemDpi();
        }
        catch
        {
            dpi = 96;
        }

        return Measure(font, text, maxWidthPx, wrapping, dpi);
    }

    public static Size Measure(CoreTextFont font, ReadOnlySpan<char> text, int maxWidthPx, TextWrapping wrapping, uint dpi)
    {
        var ctFont = font.GetFontRef(dpi);
        if (text.IsEmpty || ctFont == 0)
        {
            return Size.Empty;
        }

        maxWidthPx = Math.Max(0, maxWidthPx);

        var metrics = GetLineMetrics(ctFont);

        double maxLineWidth = 0;
        int totalLines = 0;

        // Split by explicit newlines first.
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool isBreak = i == text.Length || text[i] == '\n';
            if (!isBreak)
            {
                continue;
            }

            var segment = text.Slice(start, i - start).TrimEnd('\r');
            MeasureWrappedSegment(ctFont, segment, maxWidthPx, wrapping, ref maxLineWidth, ref totalLines);
            start = i + 1;
        }

        if (totalLines <= 0)
        {
            totalLines = 1;
        }

        // CoreText measures with DPI-scaled fonts, so padding must also scale with DPI
        // to ensure a consistent 1 DIP padding after the caller divides by dpiScale.
        double dpiScale = dpi / 96.0;
        double w = maxLineWidth + TextMeasurePolicy.WidthPaddingPx * dpiScale;

        double h = totalLines * metrics.LineHeight;
        return new Size(w, h);
    }

    private static void DrawLineGlyphs(nint ctx, nint ctFont, ReadOnlySpan<char> text, double x, double baselineY)
    {
        if (text.IsEmpty)
        {
            return;
        }

        // Reset text matrix to identity. CTLineDraw (used by DrawLineGlyphsWithFallback for
        // lines containing glyphs not in the base font, e.g. '→' U+2192) is documented to
        // advance the text position to the start of the next line as it draws — that
        // position is the tx/ty components of the text matrix. CGContextSaveGState does NOT
        // preserve the text matrix on macOS (it's not in the gstate save set per Apple's
        // documented list), so the residual translation from a previous fallback line
        // leaks into subsequent CTFontDrawGlyphs calls, shifting every glyph by the prior
        // line's end-X. Visual artifact: lines after the fallback line render at the right
        // edge of the fallback line's last character.
        //
        // Identity is the correct reset value for our usage:
        //   - scale (a=1, d=1): CT fonts apply their own size; text matrix scale must be 1.
        //   - skew (b=0, c=0): italic is a font attribute, not a text-matrix concern.
        //   - translation (tx=0, ty=0): we want absolute positions from the `pPos` array
        //     and the explicit CGContextSetTextPosition() in the fallback path to apply
        //     directly, with no residual offset.
        // It also matches the CGContext's default text matrix at construction time, so
        // the first call is a no-op and subsequent calls just clean up after fallback paths.
        CGContextSetTextMatrix(ctx, CGAffineTransform.Identity);

        int count = text.Length;
        Span<ushort> glyphs = count <= 1024 ? stackalloc ushort[count] : new ushort[count];
        Span<CGSize> advances = count <= 1024 ? stackalloc CGSize[count] : new CGSize[count];
        Span<CGPoint> positions = count <= 1024 ? stackalloc CGPoint[count] : new CGPoint[count];

        fixed (char* pChars = text)
        fixed (ushort* pGlyphs = glyphs)
        fixed (CGSize* pAdv = advances)
        fixed (CGPoint* pPos = positions)
        {
            if (!CTFontGetGlyphsForCharacters(ctFont, pChars, pGlyphs, (nuint)count))
            {
                // Fallback path:
                // - CTFontGetGlyphsForCharacters returns false when any character can't be mapped using this font.
                // - Many default fonts on macOS (e.g. SF Pro) do not include Hangul compatibility jamo (ㅁ, ㄱ, ...),
                //   which are commonly produced during IME composition.
                // - Rather than drawing nothing, we select a fallback CTFont per missing range via CTFontCreateForString.
                //
                // This is not a full shaping engine (complex scripts may still render incorrectly), but it restores
                // basic IME visibility and keeps measurement consistent with rendering.
                DrawLineGlyphsWithFallback(ctx, ctFont, text, x, baselineY);
                return;
            }

            _ = CTFontGetAdvancesForGlyphs(ctFont, CTFontOrientationHorizontal, pGlyphs, pAdv, (nuint)count);

            // Compact out low-surrogate slots (glyph 0, zero advance) so positions are correct.
            int drawCount = 0;
            double penX = x;
            for (int i = 0; i < count; i++)
            {
                if (i > 0 && char.IsLowSurrogate(text[i]))
                    continue;
                pGlyphs[drawCount] = pGlyphs[i];
                pPos[drawCount] = new CGPoint(penX, baselineY);
                penX += pAdv[i].width;
                drawCount++;
            }

            CTFontDrawGlyphs(ctFont, pGlyphs, pPos, (nuint)drawCount, ctx);
        }
    }

    private readonly struct LineMetrics
    {
        public required double Ascent { get; init; }

        public required double Descent { get; init; }

        public required double Leading { get; init; }

        public double LineHeight => Ascent + Descent + Leading;
    }

    private static LineMetrics GetLineMetrics(nint ctFont)
        => new()
        {
            Ascent = CTFontGetAscent(ctFont),
            Descent = CTFontGetDescent(ctFont),
            Leading = CTFontGetLeading(ctFont)
        };

    private sealed class LinesBuffer
    {
        public readonly List<LineEntry> Lines = new();

        public int Count => Lines.Count;

        public LineEntry this[int index] => Lines[index];

        public void Add(LineEntry entry) => Lines.Add(entry);
    }

    private readonly record struct LineEntry(int Start, int Length, double Width);

    private static LinesBuffer BuildLines(nint ctFont, ReadOnlySpan<char> text, int widthPx, TextWrapping wrapping, TextTrimming trimming = TextTrimming.None)
    {
        var buffer = new LinesBuffer();

        TextLayoutUtils.EnumerateLines(text, widthPx, wrapping, span => MeasureRunWidth(ctFont, span), line =>
        {
            buffer.Add(new LineEntry(line.Start, line.Length, line.Width));
        });

        if (buffer.Count == 0)
        {
            buffer.Add(new LineEntry(0, 0, 0));
        }

        // Post-pass: apply character-ellipsis trimming.
        if (trimming == TextTrimming.CharacterEllipsis && wrapping == TextWrapping.NoWrap)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                var entry = buffer[i];
                if (entry.Width > widthPx && entry.Length > 0)
                {
                    var lineText = text.Slice(entry.Start, entry.Length);
                    var trimmed = TextLayoutUtils.TrimLineWithEllipsis(lineText, entry.Start, widthPx, span => MeasureRunWidth(ctFont, span));
                    buffer.Lines[i] = new LineEntry(trimmed.Start, trimmed.Length, trimmed.Width);
                }
            }
        }

        return buffer;
    }

    private static void MeasureWrappedSegment(
        nint ctFont,
        ReadOnlySpan<char> segment,
        int maxWidthPx,
        TextWrapping wrapping,
        ref double maxLineWidth,
        ref int totalLines)
    {
        if (segment.IsEmpty)
        {
            totalLines++;
            return;
        }

        int localLines = 0;

        // Collect line end-char indices for overhang post-pass.
        // ReadOnlySpan<char> can't be captured in the lambda, so we store indices
        // and compute overhang after EnumerateLines returns.
        int lineCapacity = 32;
        var lineWidths = new double[lineCapacity];
        var lineEndIndices = new int[lineCapacity];
        int lineCount = 0;

        TextLayoutUtils.EnumerateLines(segment, maxWidthPx, wrapping, span => MeasureRunWidth(ctFont, span), line =>
        {
            if (lineCount >= lineCapacity)
            {
                lineCapacity *= 2;
                Array.Resize(ref lineWidths, lineCapacity);
                Array.Resize(ref lineEndIndices, lineCapacity);
            }
            lineWidths[lineCount] = line.Width;
            lineEndIndices[lineCount] = line.Length > 0 ? line.Start + line.Length - 1 : -1;
            lineCount++;
            localLines++;
        });

        // Post-pass: compute visual width (advance + last glyph overhang) per line.
        double localMax = maxLineWidth;
        for (int i = 0; i < lineCount; i++)
        {
            double visual = lineWidths[i];
            int endIdx = lineEndIndices[i];
            if (endIdx >= 0 && endIdx < segment.Length)
            {
                visual += GetLastGlyphOverhang(ctFont, segment.Slice(endIdx, 1));
            }
            if (visual > localMax)
            {
                localMax = visual;
            }
        }

        maxLineWidth = localMax;
        totalLines += localLines;
    }

    private static void AppendWrapped(LinesBuffer output, nint ctFont, ReadOnlySpan<char> segment, int widthPx, TextWrapping wrapping)
    {
        TextLayoutUtils.EnumerateLines(segment, widthPx, wrapping, span => MeasureRunWidth(ctFont, span), line =>
        {
            output.Add(new LineEntry(line.Start, line.Length, line.Width));
        });
    }

    private static double MeasureRunWidth(nint ctFont, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        // Always go through CTLine. The previous fast path summed per-glyph advances from
        // CTFontGetAdvancesForGlyphs, which ignores kerning pairs (and any other
        // OpenType GPOS positioning). Visible mismatch: prefix MeasureText with kerning
        // applied vs glyph extent without — produced extra inter-glyph gaps after kerned
        // pairs (e.g. Arial 'Te' on macOS rendered T+e ~5 px apart at 48 px instead of
        // the kerned 0 px). CTLine respects kerning + ligatures + GSUB/GPOS the same way
        // GDI's GetTextExtentPoint32 / DirectWrite's IDWriteTextLayout do. The single-
        // line measurement cost is dominated by CFAttributedString allocation; for the
        // typical SVG text-run sizes this is negligible compared to glyph rasterization.
        return MeasureRunWidthWithFallback(ctFont, text);
    }

    /// <summary>
    /// Returns how many pixels the last glyph extends beyond its advance width (right-side bearing).
    /// Used to prevent bitmap clipping during text alignment.
    /// </summary>
    private static double GetLastGlyphOverhang(nint ctFont, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        // Check only the last character.
        char lastChar = text[text.Length - 1];
        ushort glyph;
        CGSize advance;
        if (!CTFontGetGlyphsForCharacters(ctFont, &lastChar, &glyph, 1) || glyph == 0)
        {
            return 0;
        }

        CTFontGetAdvancesForGlyphs(ctFont, CTFontOrientationHorizontal, &glyph, &advance, 1);
        CGRect bounds;
        CTFontGetBoundingRectsForGlyphs(ctFont, CTFontOrientationHorizontal, &glyph, &bounds, 1);

        double glyphRight = bounds.origin.x + bounds.size.width;
        double overhang = glyphRight - advance.width;
        return overhang > 0 ? overhang : 0;
    }

    private static void DrawLineGlyphsWithFallback(nint ctx, nint baseFont, ReadOnlySpan<char> text, double x, double baselineY)
    {
        // Render the entire line through CTLine. This handles font cascading (emoji → Apple Color Emoji,
        // CJK → system CJK font, etc.) and GSUB shaping (ZWJ sequences → single combined glyph)
        // in one pass, avoiding state corruption from mixing CTLineDraw and CTFontDrawGlyphs.
        nint cfString = CreateCFString(text);
        if (cfString == 0) return;

        nint attrStr = CreateFontAttrStringWithContextColor(cfString, baseFont);
        CFRelease(cfString);
        if (attrStr == 0) return;

        nint line = CTLineCreateWithAttributedString(attrStr);
        CFRelease(attrStr);
        if (line == 0) return;

        CGContextSaveGState(ctx);
        CGContextSetTextPosition(ctx, x, baselineY);
        CTLineDraw(line, ctx);
        CGContextRestoreGState(ctx);
        CFRelease(line);
    }

    private static double MeasureRunWidthWithFallback(nint baseFont, ReadOnlySpan<char> text)
    {
        nint cfString = CreateCFString(text);
        if (cfString == 0) return 0;

        nint attrStr = CreateFontAttrString(cfString, baseFont);
        CFRelease(cfString);
        if (attrStr == 0) return 0;

        nint line = CTLineCreateWithAttributedString(attrStr);
        CFRelease(attrStr);
        if (line == 0) return 0;

        double w = CTLineGetTypographicBounds(line, null, null, null);
        CFRelease(line);
        return w;
    }

    #region CTLine helpers for complex grapheme clusters

    private static nint _kCTFontAttributeName;
    private static nint _kCTForegroundColorFromContextAttributeName;
    private static nint _kCFBooleanTrue;
    private static nint _kCFTypeDictKeyCallBacks;
    private static nint _kCFTypeDictValueCallBacks;
    private static bool _ctLineConstantsLoaded;

    private static bool EnsureCTLineConstants()
    {
        if (_ctLineConstantsLoaded) return _kCTFontAttributeName != 0;
        _ctLineConstantsLoaded = true;
        try
        {
            if (!NativeLibrary.TryLoad("/System/Library/Frameworks/CoreText.framework/CoreText", out var ct)) return false;
            if (!NativeLibrary.TryLoad("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", out var cf)) return false;

            if (NativeLibrary.TryGetExport(ct, "kCTFontAttributeName", out var p) && p != 0)
                _kCTFontAttributeName = Marshal.ReadIntPtr(p);
            if (NativeLibrary.TryGetExport(ct, "kCTForegroundColorFromContextAttributeName", out var p2) && p2 != 0)
                _kCTForegroundColorFromContextAttributeName = Marshal.ReadIntPtr(p2);
            if (NativeLibrary.TryGetExport(cf, "kCFBooleanTrue", out var p3) && p3 != 0)
                _kCFBooleanTrue = Marshal.ReadIntPtr(p3);
            NativeLibrary.TryGetExport(cf, "kCFTypeDictionaryKeyCallBacks", out _kCFTypeDictKeyCallBacks);
            NativeLibrary.TryGetExport(cf, "kCFTypeDictionaryValueCallBacks", out _kCFTypeDictValueCallBacks);
        }
        catch { }
        return _kCTFontAttributeName != 0;
    }

    /// <summary>
    /// Creates a CFAttributedString with font attribute only (for measurement).
    /// </summary>
    private static nint CreateFontAttrString(nint cfStr, nint font)
    {
        if (!EnsureCTLineConstants()) return 0;
        nint key = _kCTFontAttributeName;
        nint dict = CFDictionaryCreate(0, &key, &font, 1, _kCFTypeDictKeyCallBacks, _kCFTypeDictValueCallBacks);
        if (dict == 0) return 0;
        nint result = CFAttributedStringCreate(0, cfStr, dict);
        CFRelease(dict);
        return result;
    }

    /// <summary>
    /// Creates a CFAttributedString with font + kCTForegroundColorFromContextAttributeName=true
    /// so CTLineDraw uses the CGContext's current fill color for text rendering.
    /// </summary>
    private static nint CreateFontAttrStringWithContextColor(nint cfStr, nint font)
    {
        if (!EnsureCTLineConstants()) return 0;
        nint* keys = stackalloc nint[2];
        nint* vals = stackalloc nint[2];
        keys[0] = _kCTFontAttributeName;
        vals[0] = font;
        int count = 1;
        if (_kCTForegroundColorFromContextAttributeName != 0 && _kCFBooleanTrue != 0)
        {
            keys[count] = _kCTForegroundColorFromContextAttributeName;
            vals[count] = _kCFBooleanTrue;
            count++;
        }
        nint dict = CFDictionaryCreate(0, keys, vals, count, _kCFTypeDictKeyCallBacks, _kCFTypeDictValueCallBacks);
        if (dict == 0) return 0;
        nint result = CFAttributedStringCreate(0, cfStr, dict);
        CFRelease(dict);
        return result;
    }

    #endregion

    private static nint CreateCFString(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        fixed (char* pChars = text)
        {
            return CFStringCreateWithCharacters(allocator: 0, pChars, new nint(text.Length));
        }
    }

    #region CoreText/CoreGraphics interop

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CFRange
    {
        public readonly nint location;
        public readonly nint length;

        public CFRange(int location, int length)
        {
            this.location = location;
            this.length = length;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double x;
        public readonly double y;

        public CGPoint(double x, double y)
        { this.x = x; this.y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize
    {
        public readonly double width;
        public readonly double height;

        public CGSize(double width, double height)
        { this.width = width; this.height = height; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect
    {
        public readonly CGPoint origin;
        public readonly CGSize size;

        public CGRect(double x, double y, double width, double height)
        {
            origin = new CGPoint(x, y);
            size = new CGSize(width, height);
        }
    }

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial nint CTFontCreateForString(nint currentFont, nint @string, CFRange range);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial double CTFontGetAscent(nint font);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial double CTFontGetDescent(nint font);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial double CTFontGetLeading(nint font);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CTFontGetGlyphsForCharacters(nint font, char* characters, ushort* glyphs, nuint count);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial double CTFontGetAdvancesForGlyphs(nint font, int orientation, ushort* glyphs, CGSize* advances, nuint count);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial CGRect CTFontGetBoundingRectsForGlyphs(nint font, int orientation, ushort* glyphs, CGRect* boundingRects, nuint count);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial void CTFontDrawGlyphs(nint font, ushort* glyphs, CGPoint* positions, nuint count, nint context);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial nint CTLineCreateWithAttributedString(nint attrString);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial void CTLineDraw(nint line, nint context);

    [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    private static partial double CTLineGetTypographicBounds(nint line, double* ascent, double* descent, double* leading);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial nint CFStringCreateWithCharacters(nint allocator, char* chars, nint numChars);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial nint CFAttributedStringCreate(nint allocator, nint str, nint attributes);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint numValues, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint cf);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetTextPosition(nint context, double x, double y);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetTextMatrix(nint context, CGAffineTransform t);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGAffineTransform
    {
        public readonly double a, b, c, d, tx, ty;
        public CGAffineTransform(double a, double b, double c, double d, double tx, double ty)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.d = d;
            this.tx = tx;
            this.ty = ty;
        }
        public static readonly CGAffineTransform Identity = new(1, 0, 0, 1, 0, 0);
    }

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSaveGState(nint context);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextRestoreGState(nint context);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial nint CGColorSpaceCreateDeviceRGB();

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGColorSpaceRelease(nint colorSpace);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial nint CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint colorSpace, nuint bitmapInfo);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextRelease(nint context);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextClearRect(nint context, CGRect rect);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetRGBFillColor(nint context, double red, double green, double blue, double alpha);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetShouldAntialias(nint context, [MarshalAs(UnmanagedType.I1)] bool shouldAntialias);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetAllowsAntialiasing(nint context, [MarshalAs(UnmanagedType.I1)] bool allowsAntialiasing);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetAllowsFontSmoothing(nint context, [MarshalAs(UnmanagedType.I1)] bool allowsFontSmoothing);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetShouldSmoothFonts(nint context, [MarshalAs(UnmanagedType.I1)] bool shouldSmoothFonts);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetShouldSubpixelPositionFonts(nint context, [MarshalAs(UnmanagedType.I1)] bool shouldSubpixelPositionFonts);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextSetShouldSubpixelQuantizeFonts(nint context, [MarshalAs(UnmanagedType.I1)] bool shouldSubpixelQuantizeFonts);

    #endregion
}
