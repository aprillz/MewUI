using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI font implementation.
/// </summary>
internal sealed class GdiFont : FontBase
{
    private bool _disposed;
    private nint _perPixelAlphaHandle;

    internal nint Handle { get; private set; }
    private uint Dpi { get; }

    public GdiFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, uint dpi)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        Dpi = dpi;

        // Font size in this framework is in DIPs (1/96 inch). Convert to pixels for GDI.
        // Negative height means use character height, not cell height.
        int height = -(int)Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero);

        Handle = CreateFontCore(height, GdiConstants.CLEARTYPE_QUALITY);

        if (Handle == 0)
        {
            throw new InvalidOperationException($"Failed to create font: {family}");
        }

        // Query font metrics and convert from pixels to DIPs.
        double dpiScale = dpi / 96.0;
        var hdc = User32.GetDC(0);
        var oldFont = Gdi32.SelectObject(hdc, Handle);
        Gdi32.GetTextMetrics(hdc, out TEXTMETRIC tm);
        Gdi32.SelectObject(hdc, oldFont);
        User32.ReleaseDC(0, hdc);

        InternalLeadingPx = tm.tmInternalLeading;
        Ascent = tm.tmAscent / dpiScale;
        Descent = tm.tmDescent / dpiScale;
        InternalLeading = tm.tmInternalLeading / dpiScale;
    }

    /// <summary>Internal leading in pixels (for use by rasterizers operating in pixel space).</summary>
    internal int InternalLeadingPx { get; }

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

    public override void Dispose()
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
