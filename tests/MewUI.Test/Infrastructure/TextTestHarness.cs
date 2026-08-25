using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Infrastructure;

internal static class TextTestHarness
{
    public static void Draw(
        IGraphicsContext context,
        IGraphicsFactory factory,
        string text,
        Rect bounds,
        IFont font,
        Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
        => Draw(context, factory, text.AsMemory(), bounds, font, color,
            horizontalAlignment, verticalAlignment, wrapping, trimming);

    public static ITextLayout CreateLayout(
        IGraphicsFactory factory,
        ReadOnlyMemory<char> text,
        IFont font,
        Rect bounds,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None,
        uint dpi = 96)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text,
            Dpi = dpi,
            DefaultStyle = ToStyle(font),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = bounds.Width,
                MaxHeight = bounds.Height,
                Alignment = horizontalAlignment,
                Wrapping = wrapping,
                Trimming = trimming
            },
            Transient = true
        });

    public static Size Measure(IGraphicsFactory factory, ReadOnlyMemory<char> text, IFont font, double maxWidth = double.PositiveInfinity)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text,
            DefaultStyle = ToStyle(font),
            Paragraph = new TextParagraphStyle { MaxWidth = maxWidth },
            Transient = true
        }).MeasuredSize;

    public static void Draw(
        IGraphicsContext context,
        IGraphicsFactory factory,
        ReadOnlyMemory<char> text,
        Rect bounds,
        IFont font,
        Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        var dpi = (uint)Math.Round(Math.Max(1, context.DpiScale) * 96);
        var layout = CreateLayout(factory, text, font, bounds, horizontalAlignment, wrapping, trimming, dpi);
        Draw(context, layout, bounds, color, verticalAlignment);
    }

    public static void Draw(
        IGraphicsContext context,
        ITextLayout layout,
        Rect bounds,
        Color color,
        TextAlignment verticalAlignment = TextAlignment.Top)
    {
        double y = verticalAlignment switch
        {
            TextAlignment.Center => bounds.Y + Math.Max(0, (bounds.Height - layout.ContentHeight) * 0.5),
            TextAlignment.Bottom => bounds.Y + Math.Max(0, bounds.Height - layout.ContentHeight),
            _ => bounds.Y
        };
        var options = new TextDrawOptions(color);
        context.Text.Draw(layout, new Point(bounds.X, y), in options);
    }

    private static TextRunStyle ToStyle(IFont font)
    {
        TextDecoration decoration = TextDecoration.None;
        if (font.IsUnderline) decoration |= TextDecoration.Underline;
        if (font.IsStrikethrough) decoration |= TextDecoration.Strikethrough;
        return new TextRunStyle(font.Family, font.Size, font.Weight, font.IsItalic, decoration);
    }
}
