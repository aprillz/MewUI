using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Renders text with access key underlines for menu items (data model based, no AccessText instance).
/// Shares parsing logic with <see cref="AccessKeyHelper"/>.
/// </summary>
internal static class AccessKeyRenderer
{
    /// <summary>
    /// Draws text with an optional access key underline.
    /// </summary>
    public static void DrawText(
        IGraphicsContext context,
        string rawText,
        Rect bounds,
        IFont font,
        Color color,
        bool showAccessKeys,
        TextAlignment hAlign = TextAlignment.Left,
        TextAlignment vAlign = TextAlignment.Center,
        TextWrapping wrapping = TextWrapping.NoWrap,
        double dpiScale = 1.0)
    {
        if (string.IsNullOrEmpty(rawText))
            return;

        if (!AccessKeyHelper.TryParse(rawText, out _, out var displayText))
            displayText = rawText;

        context.DrawText(displayText, bounds, font, color, hAlign, vAlign, wrapping);

        if (!showAccessKeys)
            return;

        int underlineIndex = AccessKeyHelper.GetUnderlineIndex(rawText);
        if (underlineIndex < 0 || underlineIndex >= displayText.Length)
            return;

        DrawUnderline(context, displayText, underlineIndex, bounds, font, color, hAlign, vAlign, dpiScale);
    }

    internal static void DrawUnderline(
        IGraphicsContext context,
        string displayText,
        int underlineIndex,
        Rect bounds,
        IFont font,
        Color color,
        TextAlignment hAlign,
        TextAlignment vAlign,
        double dpiScale = 1.0)
    {
        var fullSize = context.MeasureText(displayText, font);

        var prefixWidth = underlineIndex > 0
            ? context.MeasureText(displayText.AsSpan(0, underlineIndex), font).Width
            : 0.0;
        var charWidth = context.MeasureText(displayText.AsSpan(underlineIndex, 1), font).Width;

        // Account for horizontal alignment
        double textX = bounds.X;
        if (hAlign == TextAlignment.Center)
            textX = bounds.X + (bounds.Width - fullSize.Width) / 2;
        else if (hAlign == TextAlignment.Right)
            textX = bounds.Right - fullSize.Width;

        // Account for vertical alignment
        double textY = bounds.Y;
        if (vAlign == TextAlignment.Center)
            textY = bounds.Y + (bounds.Height - fullSize.Height) / 2;
        else if (vAlign is TextAlignment.Bottom or TextAlignment.Right)
            textY = bounds.Bottom - fullSize.Height;

        double scale = dpiScale > 0 ? dpiScale : 1.0;
        double x = LayoutRounding.RoundToPixel(textX + prefixWidth, scale);
        double w = LayoutRounding.RoundToPixel(charWidth, scale);
        double y = LayoutRounding.RoundToPixel(textY + fullSize.Height, scale);
        double h = LayoutRounding.RoundToPixel(0.5, scale);

        context.FillRectangle(new Rect(x, y, Math.Max(w, h), h), color);
    }
}
