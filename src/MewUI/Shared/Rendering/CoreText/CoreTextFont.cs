using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.CoreText;

internal sealed unsafe partial class CoreTextFont : FontBase, IGlyphOutlineFont
{
    private const int kCFNumberFloat64Type = 13;

    public nint FontRef { get; private set; }
    private readonly uint _createdDpi;
    private readonly Dictionary<uint, nint> _dpiFontRefs = new();
    private readonly object _gate = new();
    /// <summary>True when bold weight was requested but the font's actual face has no bold
    /// trait (CoreText doesn't synthesize bold like GDI / DirectWrite / FreeType do). When
    /// set, glyph outline emission also appends a stroked copy of the path to thicken the
    /// rendered shape - Apple's recommended path-based fake-bold technique.</summary>
    private readonly bool _synthesizeBold;

    public CoreTextFont(
        string family,
        double size,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough,
        nint fontRef,
        uint createdDpi,
        bool synthesizeBold = false)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        FontRef = fontRef;
        _createdDpi = createdDpi == 0 ? 96u : createdDpi;
        _synthesizeBold = synthesizeBold;
        if (fontRef != 0)
        {
            _dpiFontRefs[_createdDpi] = fontRef;

            // Query metrics from CoreText and convert from pixel to DIP.
            double dpiScale = _createdDpi / 96.0;
            double ascentPx = CoreTextNative.CTFontGetAscent(fontRef);
            double descentPx = CoreTextNative.CTFontGetDescent(fontRef);
            double leadingPx = CoreTextNative.CTFontGetLeading(fontRef);
            Ascent = ascentPx / dpiScale;
            Descent = descentPx / dpiScale;
            // Internal leading = (ascent + descent + lineGap) - emSize.
            // CTFontGetLeading returns lineGap; emSize in pixels = size * dpiScale.
            InternalLeading = Math.Max(0, (ascentPx + descentPx + leadingPx) / dpiScale - size);
            double capHeightPx = CoreTextNative.CTFontGetCapHeight(fontRef);
            CapHeight = capHeightPx > 0 ? capHeightPx / dpiScale : Ascent * 0.7;
        }
    }

    public static CoreTextFont Create(
        string family,
        double size,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough)
    {
        return Create(family, size, dpi: 96, weight, italic, underline, strikethrough);
    }

    private static readonly HashSet<string> _registeredPaths = new(StringComparer.OrdinalIgnoreCase);

    public static CoreTextFont Create(
        string family,
        double size,
        uint dpi,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough)
    {
        var resolved = FontRegistry.Resolve(family);
        if (resolved != null)
        {
            EnsureRegisteredWithCoreText(resolved.Value.FilePath);
            family = resolved.Value.FamilyName;
        }

        // MewUI font size is in DIPs (1/96 inch). When rasterizing via CoreGraphics into a pixel bitmap,
        // treat CTFont "size" as pixel size so retina/backing scale produces the expected physical size.
        uint actualDpi = dpi == 0 ? 96u : dpi;
        double sizePx = Math.Max(1, size * actualDpi / 96.0);

        nint name = 0;
        try
        {
            fixed (char* p = family)
            {
                name = CoreFoundation.CFStringCreateWithCharacters(0, p, family.Length);
            }

            nint font = CreateStyledCTFont(name, sizePx, weight, italic);
            if (font == 0)
            {
                throw new InvalidOperationException("CTFontCreateWithName failed.");
            }

            // Detect bold-synthesis need: bold-class weight requested but the resulting
            // font's symbolic traits don't have kCTFontTraitBold set - the family lacks a
            // bold face on this system. CoreText (unlike GDI/DirectWrite/FreeType) doesn't
            // auto-thicken; the fake-bold path-stroke pass in TryAppendGlyphOutline picks
            // up this flag.
            bool wantBold = (int)weight >= (int)FontWeight.SemiBold;
            bool hasBoldTrait = (CoreText.CTFontGetSymbolicTraits(font) & kCTFontTraitBold) != 0;
            bool synthesizeBold = wantBold && !hasBoldTrait;

            // Keep the public Size as the DIP size for layout/measurement consistency.
            return new CoreTextFont(family, size, weight, italic, underline, strikethrough, font, actualDpi, synthesizeBold);
        }
        finally
        {
            if (name != 0)
            {
                CoreFoundation.CFRelease(name);
            }
        }
    }

    private static double MapFontWeight(FontWeight weight) => weight switch
    {
        FontWeight.Thin => -0.8,
        FontWeight.ExtraLight => -0.6,
        FontWeight.Light => -0.4,
        FontWeight.Normal => 0.0,
        FontWeight.Medium => 0.23,
        FontWeight.SemiBold => 0.3,
        FontWeight.Bold => 0.4,
        FontWeight.ExtraBold => 0.56,
        FontWeight.Black => 0.62,
        _ => 0.0
    };

    private static unsafe nint CreateStyledCTFont(nint cfFamilyName, double sizePx, FontWeight weight, bool italic)
    {
        nint baseFont = CoreText.CTFontCreateWithName(cfFamilyName, sizePx, 0);
        if (baseFont == 0)
        {
            return 0;
        }

        if (weight == FontWeight.Normal && !italic)
        {
            return baseFont;
        }

        // Apply weight/slant traits to the existing font via CTFontCreateCopyWithAttributes.
        // This works for system fonts (.AppleSystemUIFont) where descriptor-based family lookup fails.
        nint styled = TryCopyWithTraits(baseFont, sizePx, weight, italic);
        nint chosen = styled != 0 ? styled : baseFont;

        // CoreText doesn't synthesize italic - if the font family has no italic face,
        // CTFontCreateCopyWithAttributes returns a copy without the italic trait set
        // (silently). GDI / DirectWrite / FreeType all auto-skew when an italic face is
        // missing; mirror that here by recreating the font with an X-shear matrix when
        // italic was requested but the result lacks the kCTFontTraitItalic bit.
        if (italic && (CoreText.CTFontGetSymbolicTraits(chosen) & kCTFontTraitItalic) == 0)
        {
            // ~12° forward slant - matches GDI/DirectWrite synthesized oblique. The matrix is
            // {a=1, b=0, c=tan(12°), d=1, tx=0, ty=0}: shears X by tan(12°) per unit Y, so
            // a glyph at (x, y) renders at (x + y·tan(12°), y) - visual oblique.
            var skew = new CGAffineTransform { a = 1.0, b = 0.0, c = 0.21255656167002213, d = 1.0, tx = 0.0, ty = 0.0 };
            nint skewedRegular = CoreText.CTFontCreateWithNameAndMatrix(cfFamilyName, sizePx, &skew);
            if (skewedRegular != 0)
            {
                // Re-apply weight on top of the skewed font. CTFontCreateCopyWithAttributes
                // preserves the source font's matrix when its matrix parameter is null -
                // so the shear from skewedRegular carries through to the weight-applied copy.
                nint skewedWeighted = TryCopyWithTraits(skewedRegular, sizePx, weight, italic: false);
                nint finalFont = skewedWeighted != 0 ? skewedWeighted : skewedRegular;
                if (skewedWeighted != 0) CoreFoundation.CFRelease(skewedRegular);
                if (chosen != baseFont) CoreFoundation.CFRelease(chosen);
                CoreFoundation.CFRelease(baseFont);
                return finalFont;
            }
        }

        if (chosen != baseFont)
        {
            CoreFoundation.CFRelease(baseFont);
        }
        return chosen;
    }

    private static nint TryCopyWithTraits(nint baseFont, double sizePx, FontWeight weight, bool italic)
    {
        if (!CTConstants.IsAvailable)
        {
            return 0;
        }

        nint cfWeight = 0;
        nint cfSlant = 0;
        nint traitsDict = 0;
        nint attrsDict = 0;
        nint descriptor = 0;

        try
        {
            // Build traits dictionary.
            double weightVal = MapFontWeight(weight);
            cfWeight = CoreFoundation.CFNumberCreate(0, kCFNumberFloat64Type, &weightVal);
            if (cfWeight == 0)
            {
                return 0;
            }

            nint* traitKeys = stackalloc nint[2];
            nint* traitValues = stackalloc nint[2];
            int traitCount = 0;

            traitKeys[traitCount] = CTConstants.WeightTrait;
            traitValues[traitCount] = cfWeight;
            traitCount++;

            if (italic)
            {
                double slantVal = 1.0;
                cfSlant = CoreFoundation.CFNumberCreate(0, kCFNumberFloat64Type, &slantVal);
                if (cfSlant != 0)
                {
                    traitKeys[traitCount] = CTConstants.SlantTrait;
                    traitValues[traitCount] = cfSlant;
                    traitCount++;
                }
            }

            traitsDict = CoreFoundation.CFDictionaryCreate(
                0, traitKeys, traitValues, traitCount,
                CTConstants.KeyCallBacks, CTConstants.ValueCallBacks);
            if (traitsDict == 0)
            {
                return 0;
            }

            // Build attributes dictionary with traits only (no family name needed - we copy from baseFont).
            nint* attrKeys = stackalloc nint[1];
            nint* attrValues = stackalloc nint[1];
            attrKeys[0] = CTConstants.TraitsAttribute;
            attrValues[0] = traitsDict;

            attrsDict = CoreFoundation.CFDictionaryCreate(
                0, attrKeys, attrValues, 1,
                CTConstants.KeyCallBacks, CTConstants.ValueCallBacks);
            if (attrsDict == 0)
            {
                return 0;
            }

            descriptor = CoreText.CTFontDescriptorCreateWithAttributes(attrsDict);
            if (descriptor == 0)
            {
                return 0;
            }

            return CoreText.CTFontCreateCopyWithAttributes(baseFont, sizePx, 0, descriptor);
        }
        finally
        {
            if (descriptor != 0)
            {
                CoreFoundation.CFRelease(descriptor);
            }

            if (attrsDict != 0)
            {
                CoreFoundation.CFRelease(attrsDict);
            }

            if (traitsDict != 0)
            {
                CoreFoundation.CFRelease(traitsDict);
            }

            if (cfSlant != 0)
            {
                CoreFoundation.CFRelease(cfSlant);
            }

            if (cfWeight != 0)
            {
                CoreFoundation.CFRelease(cfWeight);
            }
        }
    }

    private static class CTConstants
    {
        private static readonly nint _ctLib;
        private static readonly nint _cfLib;

        public static readonly bool IsAvailable;
        public static readonly nint FamilyNameAttribute;
        public static readonly nint TraitsAttribute;
        public static readonly nint WeightTrait;
        public static readonly nint SlantTrait;
        public static readonly nint KeyCallBacks;
        public static readonly nint ValueCallBacks;

        static CTConstants()
        {
            try
            {
                _ctLib = NativeLibrary.Load(
                    "/System/Library/Frameworks/CoreText.framework/CoreText");
                _cfLib = NativeLibrary.Load(
                    "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation");

                FamilyNameAttribute = ReadSymbol(_ctLib, "kCTFontFamilyNameAttribute");
                TraitsAttribute = ReadSymbol(_ctLib, "kCTFontTraitsAttribute");
                WeightTrait = ReadSymbol(_ctLib, "kCTFontWeightTrait");
                SlantTrait = ReadSymbol(_ctLib, "kCTFontSlantTrait");

                // Callback structs: CFDictionaryCreate takes a pointer TO the struct,
                // which is the symbol address itself (not dereferenced).
                KeyCallBacks = NativeLibrary.GetExport(_cfLib, "kCFTypeDictionaryKeyCallBacks");
                ValueCallBacks = NativeLibrary.GetExport(_cfLib, "kCFTypeDictionaryValueCallBacks");

                IsAvailable = FamilyNameAttribute != 0 && TraitsAttribute != 0 &&
                              WeightTrait != 0 && SlantTrait != 0 &&
                              KeyCallBacks != 0 && ValueCallBacks != 0;
            }
            catch
            {
                IsAvailable = false;
            }
        }

        private static nint ReadSymbol(nint lib, string name)
        {
            if (!NativeLibrary.TryGetExport(lib, name, out var ptr) || ptr == 0)
            {
                return 0;
            }

            return Marshal.ReadIntPtr(ptr);
        }
    }

    internal nint GetFontRef(uint dpi)
    {
        uint actualDpi = dpi == 0 ? 96u : dpi;
        var baseRef = FontRef;
        if (baseRef == 0)
        {
            return 0;
        }

        if (actualDpi == _createdDpi)
        {
            return baseRef;
        }

        lock (_gate)
        {
            if (FontRef == 0)
            {
                return 0;
            }

            if (_dpiFontRefs.TryGetValue(actualDpi, out var cached) && cached != 0)
            {
                return cached;
            }

            // Create an additional CTFontRef for this DPI without mutating the base FontRef.
            nint name = 0;
            try
            {
                fixed (char* p = Family)
                {
                    name = CoreFoundation.CFStringCreateWithCharacters(0, p, Family.Length);
                }

                double sizePx = Math.Max(1, Size * actualDpi / 96.0);
                nint font = CreateStyledCTFont(name, sizePx, Weight, IsItalic);
                if (font == 0)
                {
                    return baseRef;
                }

                _dpiFontRefs[actualDpi] = font;
                return font;
            }
            finally
            {
                if (name != 0)
                {
                    CoreFoundation.CFRelease(name);
                }
            }
        }
    }

    ~CoreTextFont() => ReleaseNativeHandles();

    public override void Dispose()
    {
        ReleaseNativeHandles();
        GC.SuppressFinalize(this);
    }

    private void ReleaseNativeHandles()
    {
        Dictionary<uint, nint> refs;
        lock (_gate)
        {
            if (FontRef == 0)
            {
                return;
            }

            refs = new Dictionary<uint, nint>(_dpiFontRefs);
            _dpiFontRefs.Clear();
            FontRef = 0;
        }

        foreach (var kv in refs)
        {
            if (kv.Value != 0)
            {
                CoreFoundation.CFRelease(kv.Value);
            }
        }
    }

    private static void EnsureRegisteredWithCoreText(string filePath)
    {
        if (!_registeredPaths.Add(filePath))
        {
            return;
        }

        nint cfPath = 0;
        nint url = 0;
        try
        {
            fixed (char* p = filePath)
            {
                cfPath = CoreFoundation.CFStringCreateWithCharacters(0, p, filePath.Length);
            }

            url = CoreFoundation.CFURLCreateWithFileSystemPath(0, cfPath, 0 /* kCFURLPOSIXPathStyle */, false);
            if (url != 0)
            {
                CoreTextNative.CTFontManagerRegisterFontsForURL(url, 1 /* kCTFontManagerScopeProcess */, 0);
            }
        }
        finally
        {
            if (url != 0)
            {
                CoreFoundation.CFRelease(url);
            }

            if (cfPath != 0)
            {
                CoreFoundation.CFRelease(cfPath);
            }
        }
    }

    internal static unsafe partial class CoreFoundation
    {
        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static partial void CFRelease(nint cf);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static partial nint CFStringCreateWithCharacters(nint alloc, char* chars, nint numChars);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static partial nint CFNumberCreate(nint allocator, int theType, void* valuePtr);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static partial nint CFDictionaryCreate(
            nint allocator, nint* keys, nint* values, nint numValues,
            nint keyCallBacks, nint valueCallBacks);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        internal static partial nint CFURLCreateWithFileSystemPath(
            nint allocator, nint filePath, int pathStyle,
            [MarshalAs(UnmanagedType.Bool)] bool isDirectory);
    }

    private static unsafe partial class CoreText
    {
        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText", EntryPoint = "CTFontCreateWithName")]
        internal static partial nint CTFontCreateWithNameAndMatrix(nint name, double size, CGAffineTransform* matrix);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontDescriptorCreateWithAttributes(nint attributes);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontCreateCopyWithAttributes(nint font, double size, nint matrix, nint attributes);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial uint CTFontGetSymbolicTraits(nint font);
    }

    /// <summary>CGAffineTransform layout: <c>{ a, b, c, d, tx, ty }</c> as 6 CGFloats (double on 64-bit).
    /// Used as the matrix parameter for CTFont creation calls - a non-identity matrix lets CoreText apply
    /// affine transforms (e.g. X-shear for synthesized italic) to all glyph rendering and metrics.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CGAffineTransform
    {
        public double a;
        public double b;
        public double c;
        public double d;
        public double tx;
        public double ty;
    }

    /// <summary>kCTFontTraitItalic bit in CTFontSymbolicTraits - set when the font has an italic face.</summary>
    private const uint kCTFontTraitItalic = 1u;

    /// <summary>kCTFontTraitBold bit in CTFontSymbolicTraits - set when the font has a bold face.</summary>
    private const uint kCTFontTraitBold = 2u;

    private static partial class CoreTextNative
    {
        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetAscent(nint font);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetDescent(nint font);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetLeading(nint font);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetCapHeight(nint font);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static unsafe partial bool CTFontGetGlyphsForCharacters(nint font, char* characters, ushort* glyphs, nuint count);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontCreatePathForGlyph(nint font, ushort glyph, nint matrix);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CTFontManagerRegisterFontsForURL(nint fontURL, int scope, nint errors);
    }

    public unsafe bool TryAppendGlyphOutline(PathGeometry path, char ch, Point baselineOrigin, out double advance)
    {
        advance = 0;
        // Mirror FreeType: load the font at the renderer DPI so glyph coords come back in
        // render-DPI pixels, then scale by 96/dpi to land in DIP space at the user's Size.
        nint font = GetFontRef(_createdDpi);
        if (path is null || font == 0)
        {
            return false;
        }

        ushort glyph = 0;
        if (!CoreTextNative.CTFontGetGlyphsForCharacters(font, &ch, &glyph, 1) || glyph == 0)
        {
            return false;
        }

        nint cgPath = CoreGraphics.CGPathCreateMutable();
        nint glyphPath = 0;
        try
        {
            glyphPath = CoreTextNative.CTFontCreatePathForGlyph(font, glyph, 0);
            if (glyphPath == 0)
            {
                return false;
            }

            double dipScale = 96.0 / _createdDpi;
            double advanceWidth = CoreGraphics.CTFontAdvance(font, glyph);
            advance = advanceWidth * dipScale;

            var state = new GlyphPathState(path, baselineOrigin, dipScale);
            var handle = GCHandle.Alloc(state);
            nint strokedPath = 0;
            try
            {
                CoreGraphics.CGPathApply(glyphPath, GCHandle.ToIntPtr(handle), &ApplyPathElement);

                if (_synthesizeBold)
                {
                    // Apple's recommended fake-bold: stroke the glyph outline at ~4% of font
                    // size, then fill BOTH original and stroked-outline paths together. The
                    // stroked outline is itself a closed path tracing the stroke envelope -
                    // filling it produces a thin "halo" around the original glyph that fuses
                    // visually into a thicker shape. Stroke width is in font/raw units (same
                    // coordinate space as the glyph path returned by CTFontCreatePathForGlyph),
                    // so we scale Size by createdDpi/96 to land in pixel-design units.
                    double strokeWidthRaw = (Size * (_createdDpi / 96.0)) * 0.04;
                    // Butt caps + miter joins so the stroked outline preserves the original
                    // glyph's sharp corners and pointed tips (e.g. tops of A/V/W). Round
                    // caps/joins blunt those features and make the bold-synthesized glyph
                    // look soft / mushy - matches what GDI's bold-synthesis-by-overstrike
                    // and FreeType's emboldener avoid.
                    strokedPath = CoreGraphics.CGPathCreateCopyByStrokingPath(
                        glyphPath, transform: 0, strokeWidthRaw,
                        lineCap: 0 /* kCGLineCapButt */,
                        lineJoin: 0 /* kCGLineJoinMiter */,
                        miterLimit: 10.0);
                    if (strokedPath != 0)
                    {
                        CoreGraphics.CGPathApply(strokedPath, GCHandle.ToIntPtr(handle), &ApplyPathElement);
                    }
                }

                return !path.IsEmpty;
            }
            finally
            {
                handle.Free();
                if (strokedPath != 0)
                {
                    CoreGraphics.CGPathRelease(strokedPath);
                }
            }
        }
        finally
        {
            if (glyphPath != 0)
            {
                CoreGraphics.CGPathRelease(glyphPath);
            }

            if (cgPath != 0)
            {
                CoreGraphics.CGPathRelease(cgPath);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void ApplyPathElement(nint info, CGPathElement* element)
    {
        var state = (GlyphPathState)GCHandle.FromIntPtr(info).Target!;
        var points = element->points;

        switch (element->type)
        {
            case CGPathElementType.MoveToPoint:
                state.Path.MoveTo(state.ToWorldX(points[0].x), state.ToWorldY(points[0].y));
                break;

            case CGPathElementType.AddLineToPoint:
                state.Path.LineTo(state.ToWorldX(points[0].x), state.ToWorldY(points[0].y));
                break;

            case CGPathElementType.AddQuadCurveToPoint:
                state.Path.QuadTo(
                    state.ToWorldX(points[0].x), state.ToWorldY(points[0].y),
                    state.ToWorldX(points[1].x), state.ToWorldY(points[1].y));
                break;

            case CGPathElementType.AddCurveToPoint:
                state.Path.BezierTo(
                    state.ToWorldX(points[0].x), state.ToWorldY(points[0].y),
                    state.ToWorldX(points[1].x), state.ToWorldY(points[1].y),
                    state.ToWorldX(points[2].x), state.ToWorldY(points[2].y));
                break;

            case CGPathElementType.CloseSubpath:
                state.Path.Close();
                break;
        }
    }

    private sealed class GlyphPathState(PathGeometry path, Point baselineOrigin, double scale)
    {
        public PathGeometry Path { get; } = path;
        public double ToWorldX(double x) => baselineOrigin.X + (x * scale);
        public double ToWorldY(double y) => baselineOrigin.Y - (y * scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double x;
        public readonly double y;
    }

    private enum CGPathElementType : int
    {
        MoveToPoint = 0,
        AddLineToPoint = 1,
        AddQuadCurveToPoint = 2,
        AddCurveToPoint = 3,
        CloseSubpath = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CGPathElement
    {
        public CGPathElementType type;
        public CGPoint* points;
    }

    private static unsafe partial class CoreGraphics
    {
        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        internal static partial nint CGPathCreateMutable();

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        internal static partial void CGPathRelease(nint path);

        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        internal static unsafe partial void CGPathApply(nint path, nint info, delegate* unmanaged[Cdecl]<nint, CGPathElement*, void> function);

        /// <summary>
        /// CGPathCreateCopyByStrokingPath - returns a new path that traces the OUTLINE of
        /// stroking the source with the given line width / cap / join. Filling that outline
        /// produces a "halo" around the original glyph; appending it alongside the original
        /// fills both regions, effectively thickening the glyph (Apple's recommended fake-bold
        /// path-expansion technique when the font lacks a real bold face).
        /// </summary>
        [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        internal static partial nint CGPathCreateCopyByStrokingPath(
            nint path, nint transform, double lineWidth,
            int lineCap, int lineJoin, double miterLimit);

        internal static unsafe double CTFontAdvance(nint font, ushort glyph)
        {
            CGSize advance = default;
            CoreTextMeasure.CTFontGetAdvancesForGlyphs(font, 0, &glyph, &advance, 1);
            return advance.width;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize
    {
        public readonly double width;
        public readonly double height;
    }

    private static unsafe partial class CoreTextMeasure
    {
        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetAdvancesForGlyphs(nint font, int orientation, ushort* glyphs, CGSize* advances, nuint count);
    }
}
