using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Renders text with access key underlines for menu items (data model based, no AccessText instance).
/// Shares parsing logic with <see cref="AccessKeyHelper"/>.
/// </summary>
internal static class AccessKeyRenderer
{
    internal readonly record struct UnderlineMetrics(double FullWidth, double FullHeight, double PrefixWidth, double CharWidth);

    internal static void DrawParsed(
        IGraphicsContext context,
        string displayText,
        int underlineIndex,
        Rect bounds,
        TextFormat format,
        TextLayout layout,
        Color color,
        bool showAccessKeys,
        double dpiScale = 1.0,
        UnderlineMetrics? underlineMetrics = null)
    {
        if (string.IsNullOrEmpty(displayText))
            return;

        layout.EffectiveBounds = bounds;
        context.DrawTextLayout(displayText, format, layout, color);

        if (!showAccessKeys || underlineIndex < 0 || underlineIndex >= displayText.Length)
            return;

        var metrics = underlineMetrics ?? MeasureUnderline(context, displayText, underlineIndex, format, layout);
        DrawUnderline(context, bounds, format, metrics, color, dpiScale);
    }

    internal static UnderlineMetrics MeasureUnderline(
        IGraphicsContext context,
        string displayText,
        int underlineIndex,
        TextFormat format,
        TextLayout layout)
    {
        if (string.IsNullOrEmpty(displayText) || underlineIndex < 0 || underlineIndex >= displayText.Length)
        {
            return default;
        }

        double prefixWidth = 0;
        if (underlineIndex > 0)
        {
            var prefixBounds = new Rect(0, 0, double.PositiveInfinity, 0);
            var prefixConstraints = new TextLayoutConstraints(prefixBounds);
            prefixWidth = context.CreateTextLayout(displayText.AsSpan(0, underlineIndex), format, in prefixConstraints)?.MeasuredSize.Width ?? 0;
        }

        var charBounds = new Rect(0, 0, double.PositiveInfinity, 0);
        var charConstraints = new TextLayoutConstraints(charBounds);
        var charWidth = context.CreateTextLayout(displayText.AsSpan(underlineIndex, 1), format, in charConstraints)?.MeasuredSize.Width ?? 0;

        return new UnderlineMetrics(layout.MeasuredSize.Width, layout.MeasuredSize.Height, prefixWidth, charWidth);
    }

    internal static void DrawUnderline(
        IGraphicsContext context,
        Rect bounds,
        TextFormat format,
        UnderlineMetrics metrics,
        Color color,
        double dpiScale)
    {
        if (metrics.CharWidth <= 0)
        {
            return;
        }

        // Account for horizontal alignment
        double textX = bounds.X;
        if (format.HorizontalAlignment == TextAlignment.Center)
            textX = bounds.X + (bounds.Width - metrics.FullWidth) / 2;
        else if (format.HorizontalAlignment == TextAlignment.Right)
            textX = bounds.Right - metrics.FullWidth;

        // Account for vertical alignment
        double textY = bounds.Y;
        if (format.VerticalAlignment == TextAlignment.Center)
            textY = bounds.Y + (bounds.Height - metrics.FullHeight) / 2;
        else if (format.VerticalAlignment is TextAlignment.Bottom or TextAlignment.Right)
            textY = bounds.Bottom - metrics.FullHeight;

        double scale = dpiScale > 0 ? dpiScale : 1.0;
        double x = LayoutRounding.RoundToPixel(textX + metrics.PrefixWidth, scale);
        double w = LayoutRounding.RoundToPixel(metrics.CharWidth, scale);
        // Draw underline just below baseline (baseline = textY + Ascent, offset into descent)
        double baseline = textY + format.Font.Ascent;
        double y = LayoutRounding.RoundToPixel(baseline, scale);
        double h = LayoutRounding.RoundToPixel(0.5, scale);

        context.FillRectangle(new Rect(x, y, Math.Max(w, h), h), color);
    }
}
