using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI font implementation.
/// </summary>
internal sealed class GdiFont : IFont
{
    private bool _disposed;
    private nint _perPixelAlphaHandle;

    public string Family { get; }
    public double Size { get; }
    public FontWeight Weight { get; }
    public bool IsItalic { get; }
    public bool IsUnderline { get; }
    public bool IsStrikethrough { get; }

    internal nint Handle { get; private set; }
    private uint Dpi { get; }

    public GdiFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, uint dpi)
    {
        Family = family;
        Size = size;
        Weight = weight;
        IsItalic = italic;
        IsUnderline = underline;
        IsStrikethrough = strikethrough;
        Dpi = dpi;

        // Font size in this framework is in DIPs (1/96 inch). Convert to pixels for GDI.
        // Negative height means use character height, not cell height.
        int height = -(int)Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero);

        Handle = CreateFontCore(height, GdiConstants.CLEARTYPE_QUALITY);

        if (Handle == 0)
        {
            throw new InvalidOperationException($"Failed to create font: {family}");
        }
    }

    private nint CreateFontCore(int height, uint quality)
    {
        return Gdi32.CreateFont(
            height,
            0, 0, 0,
            (int)Weight,
            IsItalic ? 1u : 0u,
            IsUnderline ? 1u : 0u,
            IsStrikethrough ? 1u : 0u,
            GdiConstants.DEFAULT_CHARSET,
            GdiConstants.OUT_TT_PRECIS,
            GdiConstants.CLIP_DEFAULT_PRECIS,
            quality,
            GdiConstants.DEFAULT_PITCH | GdiConstants.FF_DONTCARE,
            Family
        );
    }

    internal nint GetHandle(GdiFontRenderMode mode)
    {
        if (mode == GdiFontRenderMode.Default)
        {
            return Handle;
        }

        if (_perPixelAlphaHandle != 0)
        {
            return _perPixelAlphaHandle;
        }

        int height = -(int)Math.Round(Size * Dpi / 96.0, MidpointRounding.AwayFromZero);
        // Coverage mode uses grayscale AA so we can extract per-pixel alpha reliably.
        _perPixelAlphaHandle = CreateFontCore(height, GdiConstants.ANTIALIASED_QUALITY);
        return _perPixelAlphaHandle == 0 ? Handle : _perPixelAlphaHandle;
    }

    public void Dispose()
    {
        if (!_disposed && Handle != 0)
        {
            Gdi32.DeleteObject(Handle);
            Handle = 0;
            if (_perPixelAlphaHandle != 0)
            {
                Gdi32.DeleteObject(_perPixelAlphaHandle);
                _perPixelAlphaHandle = 0;
            }
            _disposed = true;
        }
    }
}

internal enum GdiFontRenderMode
{
    Default,
    Coverage
}
