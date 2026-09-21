using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Com;

namespace Aprillz.MewUI.Native.DirectWrite;

#pragma warning disable CS0649 // Assigned by native code (COM vtable)

[StructLayout(LayoutKind.Sequential)]
internal struct DWRITE_COLOR_F
{
    public float r;
    public float g;
    public float b;
    public float a;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DWRITE_COLOR_GLYPH_RUN
{
    public DWRITE_GLYPH_RUN glyphRun;
    public DWRITE_GLYPH_RUN_DESCRIPTION* glyphRunDescription;
    public float baselineOriginX;
    public float baselineOriginY;
    public DWRITE_COLOR_F runColor;
    public ushort paletteIndex;
}

#pragma warning restore CS0649

/// <summary>
/// <c>IDWriteFactory2::TranslateColorGlyphRun</c> and the enumerator it hands back, which splits a
/// run into one monochrome glyph run per colour layer.
/// </summary>
internal static unsafe class DWriteColorGlyphRun
{
    /// <summary>A run that carries no colour glyph data; the caller draws it monochrome.</summary>
    internal const int DWRITE_E_NOCOLOR = unchecked((int)0x8898500C);

    /// <summary>Palette index meaning the layer takes the text's own colour rather than a palette entry.</summary>
    internal const ushort USE_TEXT_COLOR = 0xFFFF;

    private static readonly Guid IID_IDWriteFactory2 = new("0439FC60-CA44-4994-8DEE-3A9AF7B732EC");

    /// <summary>
    /// IDWriteFactory2::TranslateColorGlyphRun (vtable index 28). Returns
    /// <see cref="DWRITE_E_NOCOLOR"/> for a run with no colour layers, and E_NOINTERFACE when the
    /// system predates IDWriteFactory2.
    /// </summary>
    public static int Translate(
        IDWriteFactory* factory,
        float baselineOriginX,
        float baselineOriginY,
        in DWRITE_GLYPH_RUN glyphRun,
        DWRITE_MEASURING_MODE measuringMode,
        uint colorPaletteIndex,
        out nint enumerator)
    {
        enumerator = 0;
        int hr = ComHelpers.QueryInterface((nint)factory, in IID_IDWriteFactory2, out nint factory2);
        if (hr < 0 || factory2 == 0)
        {
            return hr < 0 ? hr : unchecked((int)0x80004002);
        }

        try
        {
            nint layers = 0;
            fixed (DWRITE_GLYPH_RUN* run = &glyphRun)
            {
                var vtbl = *(nint**)factory2;
                var fn = (delegate* unmanaged[Stdcall]<nint, float, float, DWRITE_GLYPH_RUN*,
                    DWRITE_GLYPH_RUN_DESCRIPTION*, DWRITE_MEASURING_MODE, void*, uint, nint*, int>)vtbl[28];
                hr = fn(factory2, baselineOriginX, baselineOriginY, run, null, measuringMode, null,
                    colorPaletteIndex, &layers);
            }

            enumerator = layers;
            return hr;
        }
        finally
        {
            ComHelpers.Release(factory2);
        }
    }

    /// <summary>IDWriteColorGlyphRunEnumerator::MoveNext (vtable index 3).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MoveNext(nint enumerator)
    {
        int hasRun = 0;
        var vtbl = *(nint**)enumerator;
        var fn = (delegate* unmanaged[Stdcall]<nint, int*, int>)vtbl[3];
        return fn(enumerator, &hasRun) >= 0 && hasRun != 0;
    }

    /// <summary>
    /// IDWriteColorGlyphRunEnumerator::GetCurrentRun (vtable index 4). The run stays owned by the
    /// enumerator and is only valid until the next <see cref="MoveNext"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DWRITE_COLOR_GLYPH_RUN* GetCurrentRun(nint enumerator)
    {
        DWRITE_COLOR_GLYPH_RUN* run = null;
        var vtbl = *(nint**)enumerator;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_COLOR_GLYPH_RUN**, int>)vtbl[4];
        return fn(enumerator, &run) >= 0 ? run : null;
    }
}
