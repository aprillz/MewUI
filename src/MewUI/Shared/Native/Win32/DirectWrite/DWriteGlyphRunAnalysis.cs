using System.Runtime.CompilerServices;

using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Native.DirectWrite;

internal enum DWRITE_TEXTURE_TYPE : uint
{
    ALIASED_1x1 = 0,
    CLEARTYPE_3x1 = 1
}

/// <summary>
/// <c>IDWriteGlyphRunAnalysis</c>, DirectWrite's software glyph rasterizer. It comes straight off
/// the factory, so it needs no device context, render target or window.
/// </summary>
internal static unsafe class DWriteGlyphRunAnalysis
{
    /// <summary>IDWriteFactory::CreateGlyphRunAnalysis (vtable index 23).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Create(
        IDWriteFactory* factory,
        in DWRITE_GLYPH_RUN glyphRun,
        float pixelsPerDip,
        DWRITE_RENDERING_MODE renderingMode,
        DWRITE_MEASURING_MODE measuringMode,
        float baselineOriginX,
        float baselineOriginY,
        out nint analysis)
    {
        nint result = 0;
        fixed (DWRITE_GLYPH_RUN* run = &glyphRun)
        {
            var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, DWRITE_GLYPH_RUN*, float, void*,
                DWRITE_RENDERING_MODE, DWRITE_MEASURING_MODE, float, float, nint*, int>)factory->lpVtbl[23];
            int hr = fn(factory, run, pixelsPerDip, null, renderingMode, measuringMode,
                baselineOriginX, baselineOriginY, &result);
            analysis = result;
            return hr;
        }
    }

    /// <summary>
    /// IDWriteGlyphRunAnalysis::GetAlphaTextureBounds (vtable index 3). Returns an empty rectangle
    /// when the texture type does not match the rendering mode the analysis was created with.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetAlphaTextureBounds(nint analysis, DWRITE_TEXTURE_TYPE textureType, out RECT bounds)
    {
        RECT rect = default;
        var vtbl = *(nint**)analysis;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_TEXTURE_TYPE, RECT*, int>)vtbl[3];
        int hr = fn(analysis, textureType, &rect);
        bounds = rect;
        return hr;
    }

    /// <summary>
    /// IDWriteGlyphRunAnalysis::CreateAlphaTexture (vtable index 4). One byte per pixel for
    /// <see cref="DWRITE_TEXTURE_TYPE.ALIASED_1x1"/>, three for
    /// <see cref="DWRITE_TEXTURE_TYPE.CLEARTYPE_3x1"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateAlphaTexture(nint analysis, DWRITE_TEXTURE_TYPE textureType,
        in RECT bounds, Span<byte> destination)
    {
        fixed (RECT* rect = &bounds)
        fixed (byte* buffer = destination)
        {
            var vtbl = *(nint**)analysis;
            var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_TEXTURE_TYPE, RECT*, byte*, uint, int>)vtbl[4];
            return fn(analysis, textureType, rect, buffer, (uint)destination.Length);
        }
    }

    public static void Release(nint analysis) => ComHelpers.Release(analysis);
}
