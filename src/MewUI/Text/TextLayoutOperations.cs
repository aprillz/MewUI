using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

/// <summary>Shared first-party adapter from control text properties to the retained text engine.</summary>
internal static class TextLayoutOperations
{
    public static ITextLayout GetOrCreate(
        IGraphicsFactory factory,
        string text,
        uint dpi,
        in TextRunStyle style,
        double maxWidth = double.PositiveInfinity,
        double maxHeight = double.PositiveInfinity,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None,
        TextAlignment alignment = TextAlignment.Left,
        object? owner = null,
        long revision = 0,
        bool transient = false)
    {
        var request = new TextLayoutRequest
        {
            Text = (text ?? string.Empty).AsMemory(),
            Dpi = dpi,
            DefaultStyle = style,
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = NormalizeConstraint(maxWidth),
                MaxHeight = NormalizeConstraint(maxHeight),
                Wrapping = wrapping,
                Trimming = trimming,
                Alignment = alignment
            },
            Revision = revision,
            Transient = transient
        };

        var policy = transient
            ? TextLayoutCachePolicy.None
            : owner is null ? TextLayoutCachePolicy.Content : TextLayoutCachePolicy.Owner;
        return factory.TextEngine.GetOrCreateLayout(request, policy, owner);
    }

    public static Size Measure(
        IGraphicsFactory factory,
        string text,
        uint dpi,
        in TextRunStyle style,
        double maxWidth = double.PositiveInfinity,
        TextWrapping wrapping = TextWrapping.NoWrap,
        bool transient = false)
        => string.IsNullOrEmpty(text)
            ? Size.Empty
            : GetOrCreate(factory, text, dpi, in style, maxWidth, wrapping: wrapping, transient: transient).MeasuredSize;

    public static void DrawInBounds(
        IGraphicsContext context,
        ITextLayout layout,
        Rect bounds,
        Color color,
        TextAlignment verticalAlignment = TextAlignment.Top,
        object? owner = null,
        ReadOnlyMemory<TextPaintSpan> paintSpans = default,
        bool transient = false)
    {
        double y = verticalAlignment switch
        {
            TextAlignment.Center => bounds.Y + Math.Max(0, (bounds.Height - layout.ContentHeight) * 0.5),
            TextAlignment.Bottom => bounds.Y + Math.Max(0, bounds.Height - layout.ContentHeight),
            _ => bounds.Y
        };
        var origin = new Point(bounds.X, y);
        var options = new TextDrawOptions(color, paintSpans, Owner: owner, Transient: transient);

        if (layout.MeasuredSize.Width <= bounds.Width + 0.5 && layout.ContentHeight <= bounds.Height + 0.5)
        {
            context.Text.Draw(layout, origin, in options);
            return;
        }

        context.Save();
        try
        {
            context.SetClip(bounds);
            context.Text.Draw(layout, origin, in options);
        }
        finally
        {
            context.Restore();
        }
    }

    private static double NormalizeConstraint(double value)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        return value;
    }
}
