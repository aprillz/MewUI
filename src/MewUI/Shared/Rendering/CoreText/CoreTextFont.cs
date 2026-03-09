using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.CoreText;

internal sealed unsafe partial class CoreTextFont : FontBase
{
    private const int kCFNumberFloat64Type = 13;

    public nint FontRef { get; private set; }
    private readonly uint _createdDpi;
    private readonly Dictionary<uint, nint> _dpiFontRefs = new();
    private readonly object _gate = new();

    public CoreTextFont(
        string family,
        double size,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough,
        nint fontRef,
        uint createdDpi)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        FontRef = fontRef;
        _createdDpi = createdDpi == 0 ? 96u : createdDpi;
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
            InternalLeading = leadingPx / dpiScale;
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

    public static CoreTextFont Create(
        string family,
        double size,
        uint dpi,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough)
    {
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

            // Keep the public Size as the DIP size for layout/measurement consistency.
            return new CoreTextFont(family, size, weight, italic, underline, strikethrough, font, actualDpi);
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

    private static nint CreateStyledCTFont(nint cfFamilyName, double sizePx, FontWeight weight, bool italic)
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
        if (styled != 0)
        {
            CoreFoundation.CFRelease(baseFont);
            return styled;
        }

        return baseFont;
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

            // Build attributes dictionary with traits only (no family name needed — we copy from baseFont).
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
            if (descriptor != 0) CoreFoundation.CFRelease(descriptor);
            if (attrsDict != 0) CoreFoundation.CFRelease(attrsDict);
            if (traitsDict != 0) CoreFoundation.CFRelease(traitsDict);
            if (cfSlant != 0) CoreFoundation.CFRelease(cfSlant);
            if (cfWeight != 0) CoreFoundation.CFRelease(cfWeight);
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
                _ctLib = System.Runtime.InteropServices.NativeLibrary.Load(
                    "/System/Library/Frameworks/CoreText.framework/CoreText");
                _cfLib = System.Runtime.InteropServices.NativeLibrary.Load(
                    "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation");

                FamilyNameAttribute = ReadSymbol(_ctLib, "kCTFontFamilyNameAttribute");
                TraitsAttribute = ReadSymbol(_ctLib, "kCTFontTraitsAttribute");
                WeightTrait = ReadSymbol(_ctLib, "kCTFontWeightTrait");
                SlantTrait = ReadSymbol(_ctLib, "kCTFontSlantTrait");

                // Callback structs: CFDictionaryCreate takes a pointer TO the struct,
                // which is the symbol address itself (not dereferenced).
                KeyCallBacks = System.Runtime.InteropServices.NativeLibrary.GetExport(_cfLib, "kCFTypeDictionaryKeyCallBacks");
                ValueCallBacks = System.Runtime.InteropServices.NativeLibrary.GetExport(_cfLib, "kCFTypeDictionaryValueCallBacks");

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
            if (!System.Runtime.InteropServices.NativeLibrary.TryGetExport(lib, name, out var ptr) || ptr == 0)
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

    public override void Dispose()
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
    }

    private static unsafe partial class CoreText
    {
        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontDescriptorCreateWithAttributes(nint attributes);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontCreateCopyWithAttributes(nint font, double size, nint matrix, nint attributes);
    }

    private static partial class CoreTextNative
    {
        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetAscent(nint font);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetDescent(nint font);

        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial double CTFontGetLeading(nint font);
    }
}
