using System.Runtime.InteropServices;
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

    /// <summary>
    /// Cache: (baseFamilyName, weight) → resolved GDI face name (or null if no match).
    /// Populated once per unique (family, weight) pair via EnumFontFamiliesEx.
    /// </summary>
    private static readonly Dictionary<(string Family, FontWeight Weight), string?> s_weightFaceCache = new();

    public GdiFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, uint dpi)
        : base(ResolveFamily(family, weight), size, weight, italic, underline, strikethrough)
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
        // GDI TEXTMETRIC doesn't expose cap height directly.
        // Approximate: pure ascent (without internal leading) → cap height + overshoot.
        // ~92% of pure ascent is a reasonable cap height approximation.
        CapHeight = (tm.tmAscent - tm.tmInternalLeading) * 0.92 / dpiScale;
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
        // Use ClearType quality for stronger hinting. The caller extracts coverage
        // from the max of R/G/B channels, so subpixel data is collapsed into alpha
        // but glyph shapes benefit from ClearType's tighter grid-fitting.
        _perPixelAlphaHandle = CreateFontCore(height, GdiConstants.CLEARTYPE_QUALITY);
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

    /// <summary>
    /// For non-standard weights (not 400/700), tries to find a GDI sub-family
    /// that matches the requested weight by enumerating fonts whose face name
    /// starts with the base family name.
    /// </summary>
    private static string ResolveFamily(string family, FontWeight weight)
    {
        // GDI natively handles Regular (400) and Bold (700) well.
        if (weight is FontWeight.Normal or FontWeight.Bold)
            return family;

        var key = (family, weight);
        if (s_weightFaceCache.TryGetValue(key, out var cached))
            return cached ?? family;

        // Enumerate all fonts that match the base family's charset.
        string? resolved = FindSubFamilyByWeight(family, (int)weight);
        s_weightFaceCache[key] = resolved;
        return resolved ?? family;
    }

    private static unsafe string? FindSubFamilyByWeight(string baseFamily, int targetWeight)
    {
        var hdc = User32.GetDC(0);
        try
        {
            var logFont = new LOGFONT();
            logFont.lfCharSet = (byte)GdiConstants.DEFAULT_CHARSET;
            logFont.SetFaceName(""); // enumerate all families, filter by prefix

            string? result = null;
            string prefix = baseFamily + " ";

            // EnumFontFamiliesEx callback: (LOGFONT*, TEXTMETRIC*, uint fontType, LPARAM)
            delegate* unmanaged[Stdcall]<LOGFONT*, nint, uint, nint, int> callback =
                &EnumCallback;

            var state = new EnumState { Prefix = prefix, TargetWeight = targetWeight };
            var handle = GCHandle.Alloc(state);
            try
            {
                Gdi32.EnumFontFamiliesEx(hdc, ref logFont, (nint)callback, GCHandle.ToIntPtr(handle), 0);
                result = state.Result;
            }
            finally
            {
                handle.Free();
            }

            return result;
        }
        finally
        {
            User32.ReleaseDC(0, hdc);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe int EnumCallback(LOGFONT* lf, nint textMetric, uint fontType, nint lParam)
    {
        var handle = GCHandle.FromIntPtr(lParam);
        var state = (EnumState)handle.Target!;

        // Read face name from LOGFONT
        var faceSpan = new ReadOnlySpan<char>(&lf->lfFaceName, 32);
        int nullIdx = faceSpan.IndexOf('\0');
        if (nullIdx >= 0) faceSpan = faceSpan[..nullIdx];
        var faceName = faceSpan.ToString();

        // Check if it's a sub-family of our base family and weight matches
        if (faceName.StartsWith(state.Prefix, StringComparison.OrdinalIgnoreCase)
            && lf->lfWeight == state.TargetWeight)
        {
            state.Result = faceName;
            return 0; // stop enumeration
        }

        return 1; // continue
    }

    private sealed class EnumState
    {
        public required string Prefix;
        public required int TargetWeight;
        public string? Result;
    }
}

internal enum GdiFontRenderMode
{
    Default,
    Coverage
}
