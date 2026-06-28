using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI font implementation.
/// </summary>
internal sealed partial class GdiFont : FontBase, IGlyphOutlineFont
{
    private bool _disposed;
    private nint _perPixelAlphaHandle;
    private nint _outlineDc;
    private nint _outlineOldObject;

    // The weight-resolved GDI face name used to actually create the font, which can differ from the
    // requested family. Kept internal so IFont.Family reports the requested family, not the resolved name.
    private readonly string _gdiFace;

    internal nint Handle { get; private set; }
    private uint Dpi { get; }

    /// <summary>
    /// Cache: (baseFamilyName, weight) → resolved GDI face name (or null if no match).
    /// Populated once per unique (family, weight) pair via EnumFontFamiliesEx.
    /// </summary>
    private static readonly Dictionary<(string Family, FontWeight Weight), string?> _weightFaceCache = new();

    public GdiFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, uint dpi)
        : base(family, size, weight, italic, underline, strikethrough)   // IFont.Family = the REQUESTED family
    {
        Dpi = dpi;
        _gdiFace = ResolveFamily(family, weight);

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
            _gdiFace
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

    public unsafe bool TryAppendGlyphOutline(PathGeometry path, char ch, Point baselineOrigin, out double advance)
    {
        advance = 0;
        if (path is null || Handle == 0) return false;

        EnsureOutlineDc();
        if (_outlineDc == 0) return false;

        var matrix = MAT2.Identity;
        GLYPHMETRICS metrics;
        uint glyph = ch;

        // CFF / PostScript-outline OpenType fonts (.otf) reject GGO_NATIVE (0xFFFFFFFF); GGO_BEZIER
        // returns cubic splines instead, handled by ParseGlyphOutline's TT_PRIM_CSPLINE branch. Without
        // this fallback such fonts extract no outline at all (blank text).
        uint format = GdiConstants.GGO_NATIVE;
        uint size = GetGlyphOutlineW(_outlineDc, glyph, format, &metrics, 0, null, &matrix);
        if (size == 0xFFFFFFFF)
        {
            format = GdiConstants.GGO_BEZIER;
            size = GetGlyphOutlineW(_outlineDc, glyph, format, &metrics, 0, null, &matrix);
        }

        advance = metrics.gmCellIncX * (96.0 / Dpi);
        if (size == 0xFFFFFFFF || size == 0) return size != 0xFFFFFFFF;

        var buffer = new byte[size];
        fixed (byte* bufferPtr = buffer)
        {
            uint result = GetGlyphOutlineW(_outlineDc, glyph, format, &metrics, size, bufferPtr, &matrix);
            if (result == 0xFFFFFFFF)
            {
                advance = metrics.gmCellIncX * (96.0 / Dpi);
                return false;
            }
        }

        ParseGlyphOutline(path, buffer, baselineOrigin);
        return true;
    }

    private void EnsureOutlineDc()
    {
        if (_outlineDc != 0)
        {
            return;
        }

        _outlineDc = Gdi32.CreateCompatibleDC(0);
        if (_outlineDc == 0)
        {
            return;
        }

        _outlineOldObject = Gdi32.SelectObject(_outlineDc, Handle);
    }

    private void ParseGlyphOutline(PathGeometry path, byte[] buffer, Point baselineOrigin)
    {
        var data = buffer.AsSpan();
        int offset = 0;

        while (offset < data.Length)
        {
            int contourSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            int contourEnd = offset + contourSize;
            var start = ReadPointFx(data, offset + 8);
            path.MoveTo(ToWorldX(start.X, baselineOrigin.X), ToWorldY(start.Y, baselineOrigin.Y));

            int curveOffset = offset + 16;
            while (curveOffset < contourEnd)
            {
                ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(curveOffset, 2));
                ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(curveOffset + 2, 2));
                int pointsOffset = curveOffset + 4;

                switch (type)
                {
                    case GdiConstants.TT_PRIM_LINE:
                        for (int i = 0; i < count; i++)
                        {
                            var point = ReadPointFx(data, pointsOffset + (i * 8));
                            path.LineTo(ToWorldX(point.X, baselineOrigin.X), ToWorldY(point.Y, baselineOrigin.Y));
                        }
                        break;

                    case GdiConstants.TT_PRIM_QSPLINE:
                        for (int i = 0; i < count - 1; i++)
                        {
                            var control = ReadPointFx(data, pointsOffset + (i * 8));
                            var end = i < count - 2
                                ? Midpoint(control, ReadPointFx(data, pointsOffset + ((i + 1) * 8)))
                                : ReadPointFx(data, pointsOffset + ((count - 1) * 8));

                            path.QuadTo(
                                ToWorldX(control.X, baselineOrigin.X), ToWorldY(control.Y, baselineOrigin.Y),
                                ToWorldX(end.X, baselineOrigin.X), ToWorldY(end.Y, baselineOrigin.Y));
                        }
                        break;

                    case GdiConstants.TT_PRIM_CSPLINE:
                        for (int i = 0; i + 2 < count; i += 3)
                        {
                            var c1 = ReadPointFx(data, pointsOffset + (i * 8));
                            var c2 = ReadPointFx(data, pointsOffset + ((i + 1) * 8));
                            var end = ReadPointFx(data, pointsOffset + ((i + 2) * 8));
                            path.BezierTo(
                                ToWorldX(c1.X, baselineOrigin.X), ToWorldY(c1.Y, baselineOrigin.Y),
                                ToWorldX(c2.X, baselineOrigin.X), ToWorldY(c2.Y, baselineOrigin.Y),
                                ToWorldX(end.X, baselineOrigin.X), ToWorldY(end.Y, baselineOrigin.Y));
                        }
                        break;
                }

                curveOffset += 4 + (count * 8);
            }

            path.Close();
            offset = contourEnd;
        }
    }

    private double ToWorldX(double x, double baselineX) => baselineX + (x * (96.0 / Dpi));

    private double ToWorldY(double y, double baselineY) => baselineY - (y * (96.0 / Dpi));

    private static PointFx Midpoint(PointFx a, PointFx b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static PointFx ReadPointFx(ReadOnlySpan<byte> data, int offset)
        => new(ReadFixed(data, offset), ReadFixed(data, offset + 4));

    private static double ReadFixed(ReadOnlySpan<byte> data, int offset)
    {
        // FIXED layout in the GDI outline buffer is { WORD fract; SHORT value; } —
        // fract first (matches the corrected struct definition below).
        ushort fraction = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        short value = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 2, 2));
        return value + (fraction / 65536.0);
    }

    ~GdiFont() => ReleaseNativeHandles();

    public override void Dispose()
    {
        ReleaseNativeHandles();
        GC.SuppressFinalize(this);
    }

    private void ReleaseNativeHandles()
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
            if (_outlineDc != 0)
            {
                if (_outlineOldObject != 0)
                {
                    Gdi32.SelectObject(_outlineDc, _outlineOldObject);
                    _outlineOldObject = 0;
                }
                Gdi32.DeleteDC(_outlineDc);
                _outlineDc = 0;
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
        if (_weightFaceCache.TryGetValue(key, out var cached))
            return cached ?? family;

        // Enumerate all fonts that match the base family's charset.
        string? resolved = FindSubFamilyByWeight(family, (int)weight);
        _weightFaceCache[key] = resolved;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FIXED
    {
        // Per MS docs the on-wire layout is { WORD fract; SHORT value; } — fract FIRST.
        // Reversing this makes MAT2.Identity decode as ~0 in GDI's eyes and
        // GetGlyphOutlineW returns ERROR_INVALID_DATATYPE (1003).
        public ushort fract;
        public short value;

        public static FIXED One => new() { value = 1, fract = 0 };
        public static FIXED Zero => default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAT2
    {
        public FIXED eM11;
        public FIXED eM12;
        public FIXED eM21;
        public FIXED eM22;

        public static MAT2 Identity => new()
        {
            eM11 = FIXED.One,
            eM12 = FIXED.Zero,
            eM21 = FIXED.Zero,
            eM22 = FIXED.One
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GLYPHMETRICS
    {
        public uint gmBlackBoxX;
        public uint gmBlackBoxY;
        public POINT gmptGlyphOrigin;
        public short gmCellIncX;
        public short gmCellIncY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PointFx
    {
        public PointFx(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    [LibraryImport("gdi32.dll", EntryPoint = "GetGlyphOutlineW")]
    private static unsafe partial uint GetGlyphOutlineW(
        nint hdc,
        uint uChar,
        uint uFormat,
        GLYPHMETRICS* lpgm,
        uint cbBuffer,
        byte* lpvBuffer,
        MAT2* lpmat2);
}

internal enum GdiFontRenderMode
{
    Default,
    Coverage
}
