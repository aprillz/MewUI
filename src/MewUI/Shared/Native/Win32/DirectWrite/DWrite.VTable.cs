using Aprillz.MewUI.Native.Com;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.DirectWrite;

#pragma warning disable CS0649 // Assigned by native code (COM vtable)

internal unsafe struct IDWriteFactory
{
    public void** lpVtbl;
}

internal static unsafe class DWriteVTable
{
    private const uint GetSystemFontCollectionIndex = 3;
    private const uint CreateTextFormatIndex = 15;
    private const uint CreateTextLayoutIndex = 18;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSystemFontCollection(IDWriteFactory* factory, out nint fontCollection, bool checkForUpdates)
    {
        fontCollection = 0;
        nint collection = 0;
        int check = checkForUpdates ? 1 : 0;
        var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, nint*, int, int>)factory->lpVtbl[GetSystemFontCollectionIndex];
        int hr = fn(factory, &collection, check);
        fontCollection = collection;
        return hr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateTextFormat(
        IDWriteFactory* factory,
        string family,
        DWRITE_FONT_WEIGHT weight,
        DWRITE_FONT_STYLE style,
        float size,
        out nint textFormat)
        => CreateTextFormat(factory, family, 0, weight, style, size, out textFormat);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateTextFormat(
        IDWriteFactory* factory,
        string family,
        nint fontCollection,
        DWRITE_FONT_WEIGHT weight,
        DWRITE_FONT_STYLE style,
        float size,
        out nint textFormat)
        => CreateTextFormat(factory, family, fontCollection, weight, style, size,
            Rendering.FontFallback.ResolvedLocale, out textFormat);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateTextFormat(
        IDWriteFactory* factory,
        string family,
        nint fontCollection,
        DWRITE_FONT_WEIGHT weight,
        DWRITE_FONT_STYLE style,
        float size,
        string locale,
        out nint textFormat)
    {
        nint format = 0;
        fixed (char* pFamily = family)
        fixed (char* pLocale = locale)
        {
            var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, char*, nint, DWRITE_FONT_WEIGHT, DWRITE_FONT_STYLE, DWRITE_FONT_STRETCH, float, char*, nint*, int>)factory->lpVtbl[CreateTextFormatIndex];
            int hr = fn(factory, pFamily, fontCollection, weight, style, DWRITE_FONT_STRETCH.NORMAL, size, pLocale, &format);
            textFormat = format;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateTextLayout(
        IDWriteFactory* factory,
        string text,
        nint textFormat,
        float maxWidth,
        float maxHeight,
        out nint textLayout)
    {
        nint layout = 0;
        fixed (char* pText = text)
        {
            var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, char*, uint, nint, float, float, nint*, int>)factory->lpVtbl[CreateTextLayoutIndex];
            int hr = fn(factory, pText, (uint)text.Length, textFormat, maxWidth, maxHeight, &layout);
            textLayout = layout;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateTextLayout(
        IDWriteFactory* factory,
        ReadOnlySpan<char> text,
        nint textFormat,
        float maxWidth,
        float maxHeight,
        out nint textLayout)
    {
        nint layout = 0;
        fixed (char* pText = text)
        {
            var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, char*, uint, nint, float, float, nint*, int>)factory->lpVtbl[CreateTextLayoutIndex];
            int hr = fn(factory, pText, (uint)text.Length, textFormat, maxWidth, maxHeight, &layout);
            textLayout = layout;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetTextAlignment(nint textFormat, DWRITE_TEXT_ALIGNMENT alignment)
    {
        var vtbl = *(nint**)textFormat;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_TEXT_ALIGNMENT, int>)vtbl[3];
        return fn(textFormat, alignment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetParagraphAlignment(nint textFormat, DWRITE_PARAGRAPH_ALIGNMENT alignment)
    {
        var vtbl = *(nint**)textFormat;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_PARAGRAPH_ALIGNMENT, int>)vtbl[4];
        return fn(textFormat, alignment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetWordWrapping(nint textFormat, DWRITE_WORD_WRAPPING wrapping)
    {
        var vtbl = *(nint**)textFormat;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_WORD_WRAPPING, int>)vtbl[5];
        return fn(textFormat, wrapping);
    }

    /// <summary>
    /// IDWriteTextFormat::SetTrimming (vtable index 9).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetTrimming(nint textFormat, in DWRITE_TRIMMING trimming, nint trimmingSign)
    {
        var vtbl = *(nint**)textFormat;
        fixed (DWRITE_TRIMMING* pTrimming = &trimming)
        {
            var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_TRIMMING*, nint, int>)vtbl[9];
            return fn(textFormat, pTrimming, trimmingSign);
        }
    }

    /// <summary>
    /// IDWriteFactory::CreateEllipsisTrimmingSign (vtable index 20).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateEllipsisTrimmingSign(IDWriteFactory* factory, nint textFormat, out nint trimmingSign)
    {
        trimmingSign = 0;
        nint sign = 0;
        var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, nint, nint*, int>)factory->lpVtbl[20];
        int hr = fn(factory, textFormat, &sign);
        trimmingSign = sign;
        return hr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMetrics(nint textLayout, out DWRITE_TEXT_METRICS metrics)
    {
        metrics = default;
        var vtbl = *(nint**)textLayout;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_TEXT_METRICS*, int>)vtbl[60];
        fixed (DWRITE_TEXT_METRICS* p = &metrics)
        {
            return fn(textLayout, p);
        }
    }

    // --- IDWriteFactory: CreateFontFileReference (vtable index 7) ---

    /// <summary>IDWriteFactory::CreateFontFileReference (vtable index 7).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateFontFileReference(IDWriteFactory* factory, string filePath, out nint fontFile)
    {
        fontFile = 0;
        nint ff = 0;
        fixed (char* pPath = filePath)
        {
            // IDWriteFactory::CreateFontFileReference(filePath, lastWriteTime, fontFile)
            // lastWriteTime = null → use current file time
            var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, char*, void*, nint*, int>)factory->lpVtbl[7];
            int hr = fn(factory, pPath, null, &ff);
            fontFile = ff;
            return hr;
        }
    }

    // --- IDWriteFactory: CreateFontFace (vtable index 9) ---

    /// <summary>IDWriteFactory::CreateFontFace (vtable index 9).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateFontFace(IDWriteFactory* factory, DWRITE_FONT_FACE_TYPE faceType,
        nint fontFile, uint faceIndex, DWRITE_FONT_SIMULATIONS simulations, out nint fontFace)
    {
        fontFace = 0;
        nint face = 0;
        nint pFile = fontFile;
        // IDWriteFactory::CreateFontFace(faceType, numberOfFiles, fontFiles[], faceIndex, simulations, fontFace)
        var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, DWRITE_FONT_FACE_TYPE, uint, nint*, uint, DWRITE_FONT_SIMULATIONS, nint*, int>)factory->lpVtbl[9];
        int hr = fn(factory, faceType, 1, &pFile, faceIndex, simulations, &face);
        fontFace = face;
        return hr;
    }

    // --- IDWriteFontFace: GetMetrics (vtable index 7) ---

    /// <summary>IDWriteFontFace::GetMetrics (vtable index 8). void return.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetFontFaceMetrics(nint fontFace, out DWRITE_FONT_METRICS metrics)
    {
        metrics = default;
        var vtbl = *(nint**)fontFace;
        // IDWriteFontFace: IUnknown(3) + GetType(3) + GetFiles(4) + GetIndex(5)
        //                  + GetSimulations(6) + IsSymbolFont(7) + GetMetrics(8)
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_FONT_METRICS*, void>)vtbl[8];
        fixed (DWRITE_FONT_METRICS* p = &metrics)
        {
            fn(fontFace, p);
        }
    }

    // --- IDWriteFontCollection vtable ---

    /// <summary>IDWriteFontCollection::FindFamilyName (vtable index 5).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindFamilyName(nint fontCollection, string familyName, out uint index, out int exists)
    {
        index = 0;
        exists = 0;
        uint idx = 0;
        int ex = 0;
        var vtbl = *(nint**)fontCollection;
        fixed (char* pName = familyName)
        {
            var fn = (delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)vtbl[5];
            int hr = fn(fontCollection, pName, &idx, &ex);
            index = idx;
            exists = ex;
            return hr;
        }
    }

    /// <summary>IDWriteFontCollection::GetFontFamily (vtable index 4).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetFontFamily(nint fontCollection, uint index, out nint fontFamily)
    {
        fontFamily = 0;
        nint ff = 0;
        var vtbl = *(nint**)fontCollection;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)vtbl[4];
        int hr = fn(fontCollection, index, &ff);
        fontFamily = ff;
        return hr;
    }

    /// <summary>IDWriteFontFamily::GetFirstMatchingFont (vtable index 7).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetFirstMatchingFont(nint fontFamily, DWRITE_FONT_WEIGHT weight, DWRITE_FONT_STRETCH stretch, DWRITE_FONT_STYLE style, out nint matchingFont)
    {
        matchingFont = 0;
        nint mf = 0;
        var vtbl = *(nint**)fontFamily;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_FONT_WEIGHT, DWRITE_FONT_STRETCH, DWRITE_FONT_STYLE, nint*, int>)vtbl[7];
        int hr = fn(fontFamily, weight, stretch, style, &mf);
        matchingFont = mf;
        return hr;
    }

    /// <summary>IDWriteFont::GetMetrics (vtable index 11).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetFontMetrics(nint dwriteFont, out DWRITE_FONT_METRICS metrics)
    {
        metrics = default;
        var vtbl = *(nint**)dwriteFont;
        // IDWriteFont::GetMetrics is void return (not HRESULT).
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_FONT_METRICS*, void>)vtbl[11];
        fixed (DWRITE_FONT_METRICS* p = &metrics)
        {
            fn(dwriteFont, p);
        }
    }
}

/// <summary>
/// IDWriteFactory2+ vtable helpers for font fallback builder.
/// IDWriteFactory2 inherits from IDWriteFactory1 which inherits from IDWriteFactory.
/// </summary>
internal static unsafe class DWriteFactory2VTable
{
    // IDWriteFactory:  3 (IUnknown) + 21 methods = vtable[0..23]
    // IDWriteFactory1: extends with 2 methods   = vtable[24..25]
    // IDWriteFactory2: extends with 4 methods   = vtable[26..29]

    /// <summary>
    /// IDWriteFactory2::GetSystemFontFallback (vtable index 26).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSystemFontFallback(IDWriteFactory* factory, out nint fontFallback)
    {
        fontFallback = 0;
        nint fb = 0;
        var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, nint*, int>)factory->lpVtbl[26];
        int hr = fn(factory, &fb);
        fontFallback = fb;
        return hr;
    }

    /// <summary>
    /// IDWriteFactory2::CreateFontFallbackBuilder (vtable index 27).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateFontFallbackBuilder(IDWriteFactory* factory, out nint builder)
    {
        builder = 0;
        nint b = 0;
        var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, nint*, int>)factory->lpVtbl[27];
        int hr = fn(factory, &b);
        builder = b;
        return hr;
    }
}

/// <summary>
/// IDWriteFontFallbackBuilder vtable helpers.
/// </summary>
internal static unsafe class DWriteFontFallbackBuilderVTable
{
    /// <summary>
    /// IDWriteFontFallbackBuilder::AddMapping (vtable index 3).
    /// Maps a set of Unicode ranges to one or more font families with a locale and scale.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddMapping(
        nint builder,
        DWRITE_UNICODE_RANGE* ranges,
        uint rangesCount,
        char** targetFamilyNames,
        uint targetFamilyNamesCount,
        nint fontCollection,
        char* localeName,
        char* baseFamilyName,
        float scale)
    {
        var vtbl = *(nint**)builder;
        var fn = (delegate* unmanaged[Stdcall]<nint, DWRITE_UNICODE_RANGE*, uint, char**, uint, nint, char*, char*, float, int>)vtbl[3];
        return fn(builder, ranges, rangesCount, targetFamilyNames, targetFamilyNamesCount, fontCollection, localeName, baseFamilyName, scale);
    }

    /// <summary>
    /// IDWriteFontFallbackBuilder::AddMappings (vtable index 4).
    /// Copies all mappings from an existing IDWriteFontFallback.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddMappings(nint builder, nint fontFallback)
    {
        var vtbl = *(nint**)builder;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[4];
        return fn(builder, fontFallback);
    }

    /// <summary>
    /// IDWriteFontFallbackBuilder::CreateFontFallback (vtable index 5).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateFontFallback(nint builder, out nint fontFallback)
    {
        fontFallback = 0;
        nint fb = 0;
        var vtbl = *(nint**)builder;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[5];
        int hr = fn(builder, &fb);
        fontFallback = fb;
        return hr;
    }
}

/// <summary>
/// IDWriteTextLayout vtable helper for setting custom font fallback.
/// IDWriteTextLayout2 extends IDWriteTextLayout1 extends IDWriteTextLayout extends IDWriteTextFormat.
/// </summary>
internal static unsafe class DWriteTextLayout2VTable
{
    // IDWriteTextLayout2 IID — required for SetFontFallback.
    private static readonly Guid IID_IDWriteTextLayout2 = new("1093C18F-8D5E-43F0-B064-0917311B525E");

    // IDWriteTextFormat:  3 (IUnknown) + 25 methods = vtable[0..27]
    // IDWriteTextLayout:  +39 methods               = vtable[28..66]
    // IDWriteTextLayout1: +4 methods                 = vtable[67..70]
    // IDWriteTextLayout2: +9 methods                 = vtable[71..79]
    //   SetFontFallback = 78

    /// <summary>
    /// IDWriteTextLayout2::SetFontFallback (vtable index 78).
    /// QIs for IDWriteTextLayout2 first; returns E_NOINTERFACE if unavailable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetFontFallback(nint textLayout, nint fontFallback)
    {
        int hr = ComHelpers.QueryInterface(textLayout, in IID_IDWriteTextLayout2, out nint layout2);
        if (hr < 0 || layout2 == 0) return hr;

        var vtbl = *(nint**)layout2;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[78];
        hr = fn(layout2, fontFallback);
        ComHelpers.Release(layout2);
        return hr;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct DWRITE_UNICODE_RANGE
{
    public uint first;
    public uint last;
}

#pragma warning restore CS0649
