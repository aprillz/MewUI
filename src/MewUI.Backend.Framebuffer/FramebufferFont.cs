using SkiaSharp;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferFont : FontBase
{
    private readonly SKFontStyle _style;
    private readonly string? _fontFilePath;
    private SKTypeface? _typeface;
    private bool _ownsTypeface;
    private bool _disposed;

    public FramebufferFont(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
        : base(NormalizeFamily(family), size, weight, italic, underline, strikethrough)
    {
        Dpi = dpi == 0 ? 96 : dpi;
        _fontFilePath = ResolveFontFilePath(Family);
        _style = new SKFontStyle((SKFontStyleWeight)ToSkiaWeight(weight), SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        var typeface = ResolveTypeface();
        using var font = CreateSkFont(typeface);
        var metrics = font.Metrics;
        Ascent = -metrics.Ascent;
        Descent = metrics.Descent;
        InternalLeading = Math.Max(0, metrics.Leading);
        CapHeight = metrics.CapHeight > 0 ? metrics.CapHeight : Ascent * 0.7;
    }

    public uint Dpi { get; }

    internal SKTypeface ResolveTypeface()
    {
        if (_typeface is not null)
        {
            return _typeface;
        }

        if (!string.IsNullOrWhiteSpace(_fontFilePath))
        {
            var fromFile = SKTypeface.FromFile(_fontFilePath);
            if (fromFile is not null)
            {
                _typeface = fromFile;
                _ownsTypeface = true;
                return fromFile;
            }
        }

        var typeface = SKTypeface.FromFamilyName(Family, _style);
        if (typeface is not null)
        {
            _typeface = typeface;
            _ownsTypeface = true;
            return typeface;
        }

        _typeface = SKTypeface.Default;
        _ownsTypeface = false;
        return _typeface;
    }

    internal SKFont CreateSkFont(SKTypeface? typeface = null)
    {
        var font = new SKFont(typeface ?? ResolveTypeface(), (float)Size)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Normal,
            Subpixel = true,
        };
        return font;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsTypeface)
        {
            _typeface?.Dispose();
        }

        _typeface = null;
        _ownsTypeface = false;
        base.Dispose();
    }

    private static string NormalizeFamily(string family)
        => string.IsNullOrWhiteSpace(family) ? "sans-serif" : family;

    private static string? ResolveFontFilePath(string familyOrPath)
    {
        var registered = FontRegistry.Resolve(familyOrPath);
        if (registered is not null && File.Exists(registered.Value.FilePath))
        {
            return registered.Value.FilePath;
        }

        if (!FontResources.LooksLikeFontFilePath(familyOrPath))
        {
            return null;
        }

        try
        {
            var path = Path.GetFullPath(familyOrPath);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static int ToSkiaWeight(FontWeight weight)
        => weight switch
        {
            FontWeight.Thin => 100,
            FontWeight.ExtraLight => 200,
            FontWeight.Light => 300,
            FontWeight.Normal => 400,
            FontWeight.Medium => 500,
            FontWeight.SemiBold => 600,
            FontWeight.Bold => 700,
            FontWeight.ExtraBold => 800,
            FontWeight.Black => 900,
            _ => 400
        };
}
