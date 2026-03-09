using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class DirectWriteFont : FontBase
{
    public DirectWriteFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, nint dwriteFactory)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        if (dwriteFactory == 0 || size <= 0) return;

        // Query font metrics via DirectWrite native API.
        nint collection = 0, fontFamily = 0, dwriteFont = 0;
        try
        {
            var factory = (IDWriteFactory*)dwriteFactory;
            int hr = DWriteVTable.GetSystemFontCollection(factory, out collection, checkForUpdates: false);
            if (hr < 0 || collection == 0) return;

            hr = DWriteVTable.FindFamilyName(collection, family, out uint familyIndex, out int exists);
            if (hr < 0 || exists == 0) return;

            hr = DWriteVTable.GetFontFamily(collection, familyIndex, out fontFamily);
            if (hr < 0 || fontFamily == 0) return;

            var dwWeight = (DWRITE_FONT_WEIGHT)(uint)weight;
            var dwStyle = italic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            hr = DWriteVTable.GetFirstMatchingFont(fontFamily, dwWeight, DWRITE_FONT_STRETCH.NORMAL, dwStyle, out dwriteFont);
            if (hr < 0 || dwriteFont == 0) return;

            DWriteVTable.GetFontMetrics(dwriteFont, out DWRITE_FONT_METRICS metrics);

            if (metrics.designUnitsPerEm == 0) return;

            double scale = size / metrics.designUnitsPerEm;
            Ascent = metrics.ascent * scale;
            Descent = metrics.descent * scale;
            // Internal leading = line height - em square = (ascent + descent + lineGap - designUnitsPerEm) * scale
            double leading = (metrics.ascent + metrics.descent + metrics.lineGap - metrics.designUnitsPerEm) * scale;
            InternalLeading = Math.Max(0, leading);
        }
        finally
        {
            ComHelpers.Release(dwriteFont);
            ComHelpers.Release(fontFamily);
            ComHelpers.Release(collection);
        }
    }
}
