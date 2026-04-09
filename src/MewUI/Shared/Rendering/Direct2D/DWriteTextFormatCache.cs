using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal readonly record struct DWriteTextFormatKey(
    string FontFamily,
    nint PrivateFontCollection,
    float FontSize,
    FontWeight FontWeight,
    bool IsItalic,
    TextAlignment HorizontalAlignment,
    TextAlignment VerticalAlignment,
    TextWrapping Wrapping);

internal sealed unsafe class DWriteTextFormatCache
{
    private readonly Dictionary<DWriteTextFormatKey, nint> _cache = new();

    public nint GetOrCreate(
        nint dwriteFactory,
        DirectWriteFont font,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping)
    {
        var key = new DWriteTextFormatKey(
            font.Family,
            font.PrivateFontCollection,
            (float)font.Size,
            font.Weight,
            font.IsItalic,
            horizontalAlignment,
            verticalAlignment,
            wrapping);

        if (_cache.TryGetValue(key, out nint cached))
        {
            return cached;
        }

        var weight = (DWRITE_FONT_WEIGHT)(int)font.Weight;
        var style = font.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
        int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)dwriteFactory, font.Family,
            font.PrivateFontCollection, weight, style, (float)font.Size, out nint textFormat);
        if (hr < 0 || textFormat == 0) return 0;

        DWriteVTable.SetTextAlignment(textFormat, horizontalAlignment switch
        {
            TextAlignment.Left => DWRITE_TEXT_ALIGNMENT.LEADING,
            TextAlignment.Center => DWRITE_TEXT_ALIGNMENT.CENTER,
            TextAlignment.Right => DWRITE_TEXT_ALIGNMENT.TRAILING,
            _ => DWRITE_TEXT_ALIGNMENT.LEADING
        });

        DWriteVTable.SetParagraphAlignment(textFormat, verticalAlignment switch
        {
            TextAlignment.Top => DWRITE_PARAGRAPH_ALIGNMENT.NEAR,
            TextAlignment.Center => DWRITE_PARAGRAPH_ALIGNMENT.CENTER,
            TextAlignment.Bottom => DWRITE_PARAGRAPH_ALIGNMENT.FAR,
            _ => DWRITE_PARAGRAPH_ALIGNMENT.NEAR
        });

        DWriteVTable.SetWordWrapping(textFormat,
            wrapping == TextWrapping.NoWrap ? DWRITE_WORD_WRAPPING.NO_WRAP : DWRITE_WORD_WRAPPING.WRAP);

        _cache[key] = textFormat;
        return textFormat;
    }

    public void ReleaseAll()
    {
        foreach (var (_, handle) in _cache)
        {
            if (handle != 0) ComHelpers.Release(handle);
        }
        _cache.Clear();
    }
}
