using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class MenuTextLayoutCache
{
    private const int MaxEntries = 256;

    private readonly Dictionary<LayoutKey, Size> _measureSizes = new();
    private readonly Dictionary<LayoutKey, TextLayout> _renderLayouts = new();
    private readonly Dictionary<UnderlineKey, AccessKeyRenderer.UnderlineMetrics> _underlineMetrics = new();

    public void Invalidate()
    {
        _measureSizes.Clear();
        _renderLayouts.Clear();
        _underlineMetrics.Clear();
    }

    public Size Measure(
        IGraphicsContext context,
        string text,
        TextFormat format,
        double width,
        double height = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Size.Empty;
        }

        var key = LayoutKey.Create(text, width, height, format);
        if (_measureSizes.TryGetValue(key, out var size))
        {
            return size;
        }

        TrimIfNeeded(_measureSizes);
        var constraints = new TextLayoutConstraints(new Rect(0, 0, NormalizeConstraint(width), NormalizeConstraint(height)));
        var layout = context.CreateTextLayout(text, format, in constraints);
        size = layout?.MeasuredSize ?? Size.Empty;
        _measureSizes[key] = size;
        return size;
    }

    public TextLayout? EnsureRenderLayout(
        IGraphicsContext context,
        string text,
        TextFormat format,
        Rect bounds)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var key = LayoutKey.Create(text, bounds.Width, bounds.Height, format);
        if (!_renderLayouts.TryGetValue(key, out var layout))
        {
            TrimIfNeeded(_renderLayouts);
            var constraints = new TextLayoutConstraints(new Rect(0, 0, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height)));
            layout = context.CreateTextLayout(text, format, in constraints);
            if (layout == null)
            {
                return null;
            }

            _renderLayouts[key] = layout;
        }

        layout.EffectiveBounds = bounds;
        return layout;
    }

    public AccessKeyRenderer.UnderlineMetrics GetUnderlineMetrics(
        IGraphicsContext context,
        string text,
        int underlineIndex,
        TextFormat format,
        TextLayout layout)
    {
        if (string.IsNullOrEmpty(text) || underlineIndex < 0 || underlineIndex >= text.Length)
        {
            return default;
        }

        var key = UnderlineKey.Create(text, underlineIndex, format);
        if (_underlineMetrics.TryGetValue(key, out var metrics))
        {
            return metrics;
        }

        TrimIfNeeded(_underlineMetrics);
        metrics = AccessKeyRenderer.MeasureUnderline(context, text, underlineIndex, format, layout);
        _underlineMetrics[key] = metrics;
        return metrics;
    }

    private static double NormalizeConstraint(double value)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        return double.IsPositiveInfinity(value) ? double.PositiveInfinity : value;
    }

    private static void TrimIfNeeded<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        where TKey : notnull
    {
        if (dictionary.Count < MaxEntries)
        {
            return;
        }

        dictionary.Clear();
    }

    private readonly record struct LayoutKey(
        string Text,
        double Width,
        double Height,
        TextAlignment HorizontalAlignment,
        TextAlignment VerticalAlignment,
        TextWrapping Wrapping,
        TextTrimming Trimming)
    {
        public static LayoutKey Create(string text, double width, double height, TextFormat format)
            => new(
                text,
                NormalizeForKey(width),
                NormalizeForKey(height),
                format.HorizontalAlignment,
                format.VerticalAlignment,
                format.Wrapping,
                format.Trimming);
    }

    private readonly record struct UnderlineKey(
        string Text,
        int UnderlineIndex,
        TextAlignment HorizontalAlignment,
        TextAlignment VerticalAlignment,
        TextWrapping Wrapping,
        TextTrimming Trimming)
    {
        public static UnderlineKey Create(string text, int underlineIndex, TextFormat format)
            => new(
                text,
                underlineIndex,
                format.HorizontalAlignment,
                format.VerticalAlignment,
                format.Wrapping,
                format.Trimming);
    }

    private static double NormalizeForKey(double value)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(value))
        {
            return double.PositiveInfinity;
        }

        return Math.Round(value, 3);
    }
}
