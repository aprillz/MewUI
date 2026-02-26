using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.CoreText;

internal sealed unsafe partial class CoreTextFont : IFont
{
    public nint FontRef { get; private set; }
    private readonly uint _createdDpi;
    private readonly Dictionary<uint, nint> _dpiFontRefs = new();
    private readonly object _gate = new();

    public string Family { get; }
    public double Size { get; }
    public FontWeight Weight { get; }
    public bool IsItalic { get; }
    public bool IsUnderline { get; }
    public bool IsStrikethrough { get; }

    public CoreTextFont(
        string family,
        double size,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough,
        nint fontRef,
        uint createdDpi)
    {
        Family = family;
        Size = size;
        Weight = weight;
        IsItalic = italic;
        IsUnderline = underline;
        IsStrikethrough = strikethrough;
        FontRef = fontRef;
        _createdDpi = createdDpi == 0 ? 96u : createdDpi;
        if (fontRef != 0)
        {
            _dpiFontRefs[_createdDpi] = fontRef;
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
        // MVP: weight/italic mapping is intentionally minimal; improved selection can be layered later.
        // MewUI font size is in DIPs (1/96 inch). When rasterizing via CoreGraphics into a pixel bitmap,
        // treat CTFont "size" as pixel size so retina/backing scale produces the expected physical size.
        uint actualDpi = dpi == 0 ? 96u : dpi;
        double sizePx = Math.Max(1, size * actualDpi / 96.0);

        nint name = 0;
        nint font = 0;
        try
        {
            fixed (char* p = family)
            {
                name = CoreFoundation.CFStringCreateWithCharacters(0, p, family.Length);
            }

            font = CoreText.CTFontCreateWithName(name, sizePx, 0);
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
            nint font = 0;
            try
            {
                fixed (char* p = Family)
                {
                    name = CoreFoundation.CFStringCreateWithCharacters(0, p, Family.Length);
                }

                double sizePx = Math.Max(1, Size * actualDpi / 96.0);
                font = CoreText.CTFontCreateWithName(name, sizePx, 0);
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

    public void Dispose()
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
    }

    private static unsafe partial class CoreText
    {
        [LibraryImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
        internal static partial nint CTFontCreateWithName(nint name, double size, nint matrix);
    }
}
