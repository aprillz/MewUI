using SkiaSharp;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal static class FramebufferText
{
    private const string Ellipsis = "...";

    public static TextLayout CreateLayout(ReadOnlySpan<char> text, TextFormat format, in TextLayoutConstraints constraints)
    {
        string value = text.ToString();
        double maxWidth = NormalizeConstraint(constraints.Bounds.Width);
        double maxHeight = NormalizeConstraint(constraints.Bounds.Height);
        var lines = new List<LineSegment>();
        double measuredWidth = 0;

        TextLayoutUtils.EnumerateLines(
            value,
            maxWidth >= int.MaxValue ? 0 : (int)Math.Ceiling(maxWidth),
            format.Wrapping,
            span => MeasureText(span, format.Font).Width,
            line =>
            {
                if (format.Trimming != TextTrimming.None && maxWidth < double.MaxValue && line.Width > maxWidth)
                {
                    var lineText = value.AsSpan(line.Start, line.Length);
                    line = TextLayoutUtils.TrimLineWithEllipsis(lineText, line.Start, maxWidth, span => MeasureText(span, format.Font).Width);
                }

                lines.Add(line);
                measuredWidth = Math.Max(measuredWidth, line.Width);
            });

        double lineHeight = GetLineHeight(format.Font);
        double contentHeight = Math.Max(lineHeight, lines.Count * lineHeight);
        var measuredSize = new Size(
            Math.Min(measuredWidth, maxWidth),
            Math.Min(contentHeight, maxHeight));

        return new TextLayout
        {
            MeasuredSize = measuredSize,
            EffectiveBounds = constraints.Bounds,
            EffectiveMaxWidth = maxWidth,
            ContentHeight = contentHeight,
        };
    }

    public static Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        using var textFont = TextFont.Create(font);
        string value = text.ToString();
        var width = textFont.Font.MeasureText(value);
        return new Size(Math.Ceiling(width), GetLineHeight(font));
    }

    public static Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.Wrap,
            Trimming = TextTrimming.None,
        };
        var layout = CreateLayout(text, format, new TextLayoutConstraints(new Rect(0, 0, maxWidth, double.PositiveInfinity)));
        return layout.MeasuredSize;
    }

    public static void DrawText(SKCanvas canvas, ReadOnlySpan<char> text, Rect bounds, TextFormat format, TextLayout layout, Color color)
    {
        using var textFont = TextFont.Create(format.Font);
        using var paint = new SKPaint
        {
            Color = ToSkColor(color),
            IsAntialias = true,
        };

        string value = text.ToString();
        var lines = new List<LineSegment>();
        TextLayoutUtils.EnumerateLines(
            value,
            layout.EffectiveMaxWidth >= int.MaxValue ? 0 : (int)Math.Ceiling(layout.EffectiveMaxWidth),
            format.Wrapping,
            span => MeasureText(span, format.Font).Width,
            line =>
            {
                if (format.Trimming != TextTrimming.None && layout.EffectiveMaxWidth < double.MaxValue && line.Width > layout.EffectiveMaxWidth)
                {
                    var lineText = value.AsSpan(line.Start, line.Length);
                    line = TextLayoutUtils.TrimLineWithEllipsis(lineText, line.Start, layout.EffectiveMaxWidth, span => MeasureText(span, format.Font).Width);
                }

                lines.Add(line);
            });

        double lineHeight = GetLineHeight(format.Font);
        double contentHeight = Math.Max(lineHeight, lines.Count * lineHeight);
        double y = bounds.Y + format.Font.Ascent;
        if (format.VerticalAlignment == TextAlignment.Center)
        {
            y += Math.Max(0, (bounds.Height - contentHeight) / 2.0);
        }
        else if (format.VerticalAlignment == TextAlignment.Right)
        {
            y += Math.Max(0, bounds.Height - contentHeight);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            string lineText = value.Substring(line.Start, line.Length);
            if (format.Trimming != TextTrimming.None && line.Start + line.Length < value.Length)
            {
                lineText += Ellipsis;
            }

            double x = bounds.X;
            double lineWidth = MeasureText(lineText, format.Font).Width;
            if (format.HorizontalAlignment == TextAlignment.Center)
            {
                x += Math.Max(0, (bounds.Width - lineWidth) / 2.0);
            }
            else if (format.HorizontalAlignment == TextAlignment.Right)
            {
                x += Math.Max(0, bounds.Width - lineWidth);
            }

            canvas.DrawText(lineText, (float)x, (float)(y + i * lineHeight), SKTextAlign.Left, textFont.Font, paint);
        }
    }

    internal static double GetLineHeight(IFont font)
        => Math.Max(1, font.Ascent + font.Descent + font.InternalLeading);

    internal static SKColor ToSkColor(Color color)
        => new(color.R, color.G, color.B, color.A);

    private static double NormalizeConstraint(double value)
        => double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? double.MaxValue : value;

    private static SKTypeface ResolveTypeface(IFont font)
        => font is FramebufferFont framebufferFont ? framebufferFont.ResolveTypeface() : SKTypeface.FromFamilyName(font.Family) ?? SKTypeface.Default;

    private static SKFont CreateSkFont(IFont font, SKTypeface typeface)
        => font is FramebufferFont framebufferFont
            ? framebufferFont.CreateSkFont(typeface)
            : new SKFont(typeface, (float)font.Size)
            {
                Edging = SKFontEdging.Antialias,
                Hinting = SKFontHinting.Normal,
                Subpixel = true,
            };

    private readonly ref struct TextFont
    {
        private readonly SKTypeface? _ownedTypeface;

        private TextFont(SKFont font, SKTypeface? ownedTypeface)
        {
            Font = font;
            _ownedTypeface = ownedTypeface;
        }

        public SKFont Font { get; }

        public static TextFont Create(IFont font)
        {
            if (font is FramebufferFont framebufferFont)
            {
                var typeface = framebufferFont.ResolveTypeface();
                return new TextFont(framebufferFont.CreateSkFont(typeface), ownedTypeface: null);
            }

            var ownedTypeface = SKTypeface.FromFamilyName(font.Family) ?? SKTypeface.Default;
            return new TextFont(CreateSkFont(font, ownedTypeface), ownedTypeface);
        }

        public void Dispose()
        {
            Font.Dispose();
            _ownedTypeface?.Dispose();
        }
    }
}
