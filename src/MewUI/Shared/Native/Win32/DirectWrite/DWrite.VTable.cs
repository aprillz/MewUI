using System.Runtime.CompilerServices;

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
    {
        nint format = 0;
        const string locale = "en-us";
        fixed (char* pFamily = family)
        fixed (char* pLocale = locale)
        {
            var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, char*, nint, DWRITE_FONT_WEIGHT, DWRITE_FONT_STYLE, DWRITE_FONT_STRETCH, float, char*, nint*, int>)factory->lpVtbl[CreateTextFormatIndex];
            int hr = fn(factory, pFamily, 0, weight, style, DWRITE_FONT_STRETCH.NORMAL, size, pLocale, &format);
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

#pragma warning restore CS0649
