using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// Handles Wrap + CharacterEllipsis rendering for GDI-based backends.
/// GDI's DT_END_ELLIPSIS doesn't append "…" on the last visible line when
/// text overflows vertically with DT_WORDBREAK, so we render line-by-line.
/// </summary>
internal static class GdiWrappedEllipsisHelper
{
    /// <summary>
    /// Renders wrapped text line-by-line, appending ellipsis on the last visible line
    /// when the text overflows vertically. Returns false if no overflow (caller should
    /// use normal DrawText path).
    /// </summary>
    public static unsafe bool TryDrawWrappedWithEllipsis(
        nint hdc,
        ReadOnlySpan<char> text,
        RECT rect,
        int widthPx,
        int heightPx,
        TextAlignment hAlign,
        TextAlignment vAlign)
    {
        if (text.IsEmpty || widthPx <= 0 || heightPx <= 0)
        {
            return false;
        }

        // Measure single line height.
        var mRect = new RECT(0, 0, widthPx, 0);
        fixed (char* pM = "Mq")
        {
            Gdi32.DrawText(hdc, pM, 2, ref mRect,
                GdiConstants.DT_CALCRECT | GdiConstants.DT_SINGLELINE | GdiConstants.DT_NOPREFIX);
        }

        int lineHeight = Math.Max(1, mRect.Height);

        // Measure full wrapped text height.
        var fullRect = new RECT(0, 0, widthPx, 0);
        fixed (char* pText = text)
        {
            Gdi32.DrawText(hdc, pText, text.Length, ref fullRect,
                GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX);
        }

        if (fullRect.Height <= heightPx)
        {
            return false; // No vertical overflow, use normal path.
        }

        int maxVisibleLines = Math.Max(1, heightPx / lineHeight);

        // Build wrapped lines using TextLayout with GDI measurement.
        var lines = new List<TextLayout.LineSegment>();
        TextLayout.EnumerateLines(text, widthPx, TextWrapping.Wrap,
            span =>
            {
                if (span.IsEmpty) return 0;
                var r = new RECT(0, 0, int.MaxValue, 0);
                fixed (char* p = span)
                {
                    Gdi32.DrawText(hdc, p, span.Length, ref r,
                        GdiConstants.DT_CALCRECT | GdiConstants.DT_SINGLELINE | GdiConstants.DT_NOPREFIX);
                }
                return r.Width;
            },
            line => lines.Add(line));

        int linesToDraw = Math.Min(lines.Count, maxVisibleLines);
        bool hasOverflow = lines.Count > maxVisibleLines;

        // Vertical alignment.
        int totalH = linesToDraw * lineHeight;
        int startY = rect.top + vAlign switch
        {
            TextAlignment.Center => (heightPx - totalH) / 2,
            TextAlignment.Bottom => heightPx - totalH,
            _ => 0
        };

        uint hFormat = hAlign switch
        {
            TextAlignment.Center => GdiConstants.DT_CENTER,
            TextAlignment.Right => GdiConstants.DT_RIGHT,
            _ => GdiConstants.DT_LEFT
        };

        for (int i = 0; i < linesToDraw; i++)
        {
            var seg = lines[i];
            if (seg.Length <= 0) continue;

            int y = startY + i * lineHeight;
            var lineRect = new RECT(rect.left, y, rect.right, y + lineHeight);

            uint format = GdiConstants.DT_SINGLELINE | GdiConstants.DT_NOPREFIX | GdiConstants.DT_VCENTER | hFormat;

            if (i == linesToDraw - 1 && hasOverflow)
            {
                // Pass all remaining text from this line onward so GDI sees overflow and adds "…".
                format |= GdiConstants.DT_END_ELLIPSIS;
                int remainingLen = text.Length - seg.Start;
                fixed (char* p = text.Slice(seg.Start, remainingLen))
                {
                    Gdi32.DrawText(hdc, p, remainingLen, ref lineRect, format);
                }
            }
            else
            {
                fixed (char* p = text.Slice(seg.Start, seg.Length))
                {
                    Gdi32.DrawText(hdc, p, seg.Length, ref lineRect, format);
                }
            }
        }

        return true;
    }
}
