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
        int wrapWidthPx = 0)
    {
        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        var ctFont = font.GetFontRef(dpi);
        if (text.IsEmpty || ctFont == 0)
        {
            return new TextBitmap(1, 1, new byte[4]);
        }

        int stride = checked(widthPx * 4);
        var data = new byte[checked(stride * heightPx)];

        fixed (byte* pData = data)
        {
            var colorspace = CGColorSpaceCreateDeviceRGB();
            if (colorspace == 0)
            {
                return new TextBitmap(1, 1, new byte[4]);
            }

            var ctx = CGBitmapContextCreate(pData, (nuint)widthPx, (nuint)heightPx, 8, (nuint)stride, colorspace, kCGBitmapInfo);
            CGColorSpaceRelease(colorspace);

            if (ctx == 0)
            {
                return new TextBitmap(1, 1, new byte[4]);
            }

            try
            {
                // Clear to transparent.
                CGContextClearRect(ctx, new CGRect(0, 0, widthPx, heightPx));

                // Enable anti-aliasing.
                CGContextSetShouldAntialias(ctx, true);
                CGContextSetAllowsAntialiasing(ctx, true);
                CGContextSetAllowsFontSmoothing(ctx, true);
                // Improve text quality for bitmap-rendered glyphs (matches typical AppKit defaults).
                CGContextSetShouldSmoothFonts(ctx, true);
                CGContextSetShouldSubpixelPositionFonts(ctx, true);
                CGContextSetShouldSubpixelQuantizeFonts(ctx, true);

                // Fill with requested color.
                CGContextSetRGBFillColor(ctx, color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);

                // Layout.
                var metrics = GetLineMetrics(ctFont);
                if (wrapping == TextWrapping.Wrap && wrapWidthPx > 0 && widthPx > wrapWidthPx)
                {
                    widthPx = wrapWidthPx;
                }

                int lineWidthPx = wrapping == TextWrapping.Wrap && wrapWidthPx > 0 ? wrapWidthPx : widthPx;
                var lines = BuildLines(ctFont, text, lineWidthPx, wrapping);

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
                    double x = horizontalAlignment switch
                    {
                        TextAlignment.Center => (widthPx - line.Width) / 2.0,
                        TextAlignment.Right => widthPx - line.Width,
                        _ => 0.0
                    };

                    // Baseline in "top-left" coordinates.
                    // Distribute font leading evenly above and below the glyph box to avoid a "top-aligned" look
                    // when LineHeight includes non-zero leading (notably on macOS/CoreText).
                    double baselineTop = topY + (metrics.Leading * 0.5) + metrics.Ascent + i * metrics.LineHeight;
                    // Convert to CoreGraphics user space (bottom-left origin).
                    double baselineY = heightPx - baselineTop;

                    if (line.Length > 0)
                    {
                        DrawLineGlyphs(ctx, ctFont, text.Slice(line.Start, line.Length), x, baselineY);
                    }
                }

                return new TextBitmap(widthPx, heightPx, data);
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

        double w = TextMeasurePolicy.ApplyWidthPadding(maxLineWidth);

        double h = totalLines * metrics.LineHeight;
        return new Size(w, h);
    }

    private static void DrawLineGlyphs(nint ctx, nint ctFont, ReadOnlySpan<char> text, double x, double baselineY)
    {
        if (text.IsEmpty)
        {
            return;
        }

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

            double penX = x;
            for (int i = 0; i < count; i++)
            {
                pPos[i] = new CGPoint(penX, baselineY);
                penX += pAdv[i].width;
            }

            CTFontDrawGlyphs(ctFont, pGlyphs, pPos, (nuint)count, ctx);
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

    private static LinesBuffer BuildLines(nint ctFont, ReadOnlySpan<char> text, int widthPx, TextWrapping wrapping)
    {
        var buffer = new LinesBuffer();

        TextLayout.EnumerateLines(text, widthPx, wrapping, span => MeasureRunWidth(ctFont, span), line =>
        {
            buffer.Add(new LineEntry(line.Start, line.Length, line.Width));
        });

        if (buffer.Count == 0)
        {
            buffer.Add(new LineEntry(0, 0, 0));
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

        double localMax = maxLineWidth;
        int localLines = 0;

        TextLayout.EnumerateLines(segment, maxWidthPx, wrapping, span => MeasureRunWidth(ctFont, span), line =>
        {
            if (line.Width > localMax)
            {
                localMax = line.Width;
            }
            localLines++;
        });

        maxLineWidth = localMax;
        totalLines += localLines;
    }

    private static void AppendWrapped(LinesBuffer output, nint ctFont, ReadOnlySpan<char> segment, int widthPx, TextWrapping wrapping)
    {
        TextLayout.EnumerateLines(segment, widthPx, wrapping, span => MeasureRunWidth(ctFont, span), line =>
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

        int count = text.Length;
        Span<ushort> glyphs = count <= 1024 ? stackalloc ushort[count] : new ushort[count];
        Span<CGSize> advances = count <= 1024 ? stackalloc CGSize[count] : new CGSize[count];

        fixed (char* pChars = text)
        fixed (ushort* pGlyphs = glyphs)
        fixed (CGSize* pAdv = advances)
        {
            if (!CTFontGetGlyphsForCharacters(ctFont, pChars, pGlyphs, (nuint)count))
            {
                return MeasureRunWidthWithFallback(ctFont, text);
            }

            _ = CTFontGetAdvancesForGlyphs(ctFont, CTFontOrientationHorizontal, pGlyphs, pAdv, (nuint)count);

            double width = 0;
            for (int i = 0; i < count; i++)
            {
                width += pAdv[i].width;
            }
            return width;
        }
    }

    private static void DrawLineGlyphsWithFallback(nint ctx, nint baseFont, ReadOnlySpan<char> text, double x, double baselineY)
    {
        nint cfString = CreateCFString(text);
        if (cfString == 0)
        {
            return;
        }

        try
        {
            double penX = x;
            Span<ushort> glyphsBuffer = stackalloc ushort[2];
            Span<CGSize> advancesBuffer = stackalloc CGSize[2];
            int i = 0;
            while (i < text.Length)
            {
                int clusterLen = (i + 1 < text.Length && char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1])) ? 2 : 1;
                var slice = text.Slice(i, clusterLen);

                var glyphs = glyphsBuffer.Slice(0, clusterLen);
                var advances = advancesBuffer.Slice(0, clusterLen);

                nint fontToUse = baseFont;
                bool ok;

                fixed (char* pChars = slice)
                fixed (ushort* pGlyphs = glyphs)
                fixed (CGSize* pAdv = advances)
                {
                    ok = CTFontGetGlyphsForCharacters(baseFont, pChars, pGlyphs, (nuint)clusterLen);
                    if (ok)
                    {
                        _ = CTFontGetAdvancesForGlyphs(baseFont, CTFontOrientationHorizontal, pGlyphs, pAdv, (nuint)clusterLen);
                    }
                }

                if (!ok)
                {
                    // Ask CoreText for a font that can render this range.
                    fontToUse = CTFontCreateForString(baseFont, cfString, new CFRange(i, clusterLen));
                    if (fontToUse == 0)
                    {
                        i += clusterLen;
                        continue;
                    }

                    try
                    {
                        fixed (char* pChars = slice)
                        fixed (ushort* pGlyphs = glyphs)
                        fixed (CGSize* pAdv = advances)
                        {
                            if (!CTFontGetGlyphsForCharacters(fontToUse, pChars, pGlyphs, (nuint)clusterLen))
                            {
                                i += clusterLen;
                                continue;
                            }

                            _ = CTFontGetAdvancesForGlyphs(fontToUse, CTFontOrientationHorizontal, pGlyphs, pAdv, (nuint)clusterLen);
                        }

                        // Draw this cluster with the fallback font.
                        for (int j = 0; j < clusterLen; j++)
                        {
                            ushort glyph = glyphs[j];
                            if (glyph == 0)
                            {
                                continue;
                            }

                            var pos = new CGPoint(penX, baselineY);
                            CTFontDrawGlyphs(fontToUse, &glyph, &pos, 1, ctx);
                            penX += advances[j].width;
                        }
                    }
                    finally
                    {
                        CFRelease(fontToUse);
                    }
                }
                else
                {
                    // Draw this cluster with the base font.
                    for (int j = 0; j < clusterLen; j++)
                    {
                        ushort glyph = glyphs[j];
                        if (glyph == 0)
                        {
                            continue;
                        }

                        var pos = new CGPoint(penX, baselineY);
                        CTFontDrawGlyphs(baseFont, &glyph, &pos, 1, ctx);
                        penX += advances[j].width;
                    }
                }

                i += clusterLen;
            }
        }
        finally
        {
            CFRelease(cfString);
        }
    }

    private static double MeasureRunWidthWithFallback(nint baseFont, ReadOnlySpan<char> text)
    {
        nint cfString = CreateCFString(text);
        if (cfString == 0)
        {
            return 0;
        }

        try
        {
            double width = 0;
            Span<ushort> glyphsBuffer = stackalloc ushort[2];
            Span<CGSize> advancesBuffer = stackalloc CGSize[2];
            int i = 0;
            while (i < text.Length)
            {
                int clusterLen = (i + 1 < text.Length && char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1])) ? 2 : 1;
                var slice = text.Slice(i, clusterLen);

                var glyphs = glyphsBuffer.Slice(0, clusterLen);
                var advances = advancesBuffer.Slice(0, clusterLen);

                bool ok;
                fixed (char* pChars = slice)
                fixed (ushort* pGlyphs = glyphs)
                fixed (CGSize* pAdv = advances)
                {
                    ok = CTFontGetGlyphsForCharacters(baseFont, pChars, pGlyphs, (nuint)clusterLen);
                    if (ok)
                    {
                        _ = CTFontGetAdvancesForGlyphs(baseFont, CTFontOrientationHorizontal, pGlyphs, pAdv, (nuint)clusterLen);
                    }
                }

                if (!ok)
                {
                    nint fallbackFont = CTFontCreateForString(baseFont, cfString, new CFRange(i, clusterLen));
                    if (fallbackFont == 0)
                    {
                        i += clusterLen;
                        continue;
                    }

                    try
                    {
                        fixed (char* pChars = slice)
                        fixed (ushort* pGlyphs = glyphs)
                        fixed (CGSize* pAdv = advances)
                        {
                            if (!CTFontGetGlyphsForCharacters(fallbackFont, pChars, pGlyphs, (nuint)clusterLen))
                            {
                                i += clusterLen;
                                continue;
                            }

                            _ = CTFontGetAdvancesForGlyphs(fallbackFont, CTFontOrientationHorizontal, pGlyphs, pAdv, (nuint)clusterLen);
                        }

                        for (int j = 0; j < clusterLen; j++)
                        {
                            width += advances[j].width;
                        }
                    }
                    finally
                    {
                        CFRelease(fallbackFont);
                    }
                }
                else
                {
                    for (int j = 0; j < clusterLen; j++)
                    {
                        width += advances[j].width;
                    }
                }

                i += clusterLen;
            }

            return width;
        }
        finally
        {
            CFRelease(cfString);
        }
    }

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
    private static partial void CTFontDrawGlyphs(nint font, ushort* glyphs, CGPoint* positions, nuint count, nint context);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial nint CFStringCreateWithCharacters(nint allocator, char* chars, nint numChars);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint cf);

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
