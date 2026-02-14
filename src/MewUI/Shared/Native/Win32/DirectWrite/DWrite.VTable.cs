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
}

#pragma warning restore CS0649
