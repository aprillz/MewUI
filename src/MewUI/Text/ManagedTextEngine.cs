using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal sealed partial class ManagedTextEngine : ITextEngine, IDisposable
{
    // GDI DrawText stops reporting reliable extents above its 16-bit-era text limit.
    private const int FastPathSegmentLength = 32 * 1024;
    private const string ELLIPSIS = "...";
    private readonly IGraphicsFactory _factory;
    private readonly Dictionary<FontKey, IFont> _fonts = [];
    private readonly Dictionary<IFont, double> _fontLineHeights = [];
    private readonly ManagedTextLayoutCache _cache;
    private bool _disposed;

    public ManagedTextEngine(IGraphicsFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _cache = new ManagedTextLayoutCache(this);
    }

    public ITextLayoutCache ManagedCache => _cache;

    private ITextBackendMeasurementContext CreateMeasurementContext(uint dpi)
    {
        if (_factory is ITextBackendFactory backend)
        {
            return backend.CreateTextMeasurementContext(dpi);
        }

        throw new InvalidOperationException(
            $"Graphics backend '{_factory.Backend}' does not provide text measurement services.");
    }

    public ITextLayout CreateLayout(TextLayoutRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CreateLayoutCore(TextLayoutRequestSnapshot.Create(request));
    }

    public ITextLayout GetOrCreateLayout(
        TextLayoutRequest request,
        TextLayoutCachePolicy cachePolicy,
        object? owner = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = TextLayoutRequestSnapshot.Create(request);
        return _cache.GetOrCreate(snapshot, cachePolicy, owner);
    }

    internal ManagedTextLayout CreateLayoutCore(TextLayoutRequestSnapshot snapshot)
    {
        bool fastPath = snapshot.Paragraph.Wrapping == TextWrapping.NoWrap &&
                        snapshot.Paragraph.FlowDirection == TextFlowDirection.LeftToRight &&
                        snapshot.Paragraph.LetterSpacing == 0 &&
                        snapshot.Paragraph.Trimming == TextTrimming.None &&
                        snapshot.Runs.Length == 0 &&
                        snapshot.Inlines.Length == 0 &&
                        snapshot.Text.AsSpan().IndexOfAny('\r', '\n', '\t') < 0;

        using var context = CreateMeasurementContext(snapshot.Dpi);
        if (fastPath)
        {
            var font = GetFont(snapshot.DefaultStyle, snapshot.Dpi);
            var segments = MeasureFastPathSegments(context, snapshot.Text, font, out var measured);
            double height = ResolveLineHeight(
                snapshot.Paragraph, GetFontLineHeight(context, font), measured.Height);
            double width = measured.Width;
            double trailingWhitespace = MeasureTrailingWhitespace(context, snapshot, font);
            double x = ResolveLineX(snapshot.Paragraph, width - trailingWhitespace);
            if (x != 0)
            {
                for (int index = 0; index < segments.Count; index++)
                {
                    segments[index] = segments[index] with { X = segments[index].X + x };
                }
            }
            double baseline = ApplyHalfLeading(font.Ascent, height, font.Ascent + font.Descent);
            double trimTop = 0;
            double trimBottom = 0;
            if (snapshot.Paragraph.LineBoxTrim != LineBoxTrim.None)
            {
                trimTop = Math.Max(0, baseline - font.CapHeight);
                if (snapshot.Paragraph.LineBoxTrim == LineBoxTrim.CapAndBaseline)
                {
                    trimBottom = Math.Max(0, height - baseline);
                }
            }
            double boxHeight = height - trimTop - trimBottom;
            var line = new ManagedTextLine(
                new TextLayoutLineMetrics(
                    0, snapshot.Text.Length, 0, new Rect(x, 0, width, boxHeight), baseline - trimTop, trailingWhitespace),
                fastSegments: segments)
            {
                TrimTop = trimTop,
                TrimBottom = trimBottom
            };
            return new ManagedTextLayout(this, snapshot, [line], new Size(width, boxHeight), isFastPath: true);
        }

        return CreateRunLayout(context, snapshot);
    }

    internal double MeasureFastPathRange(TextLayoutRequestSnapshot snapshot, int start, int length)
    {
        start = Math.Clamp(start, 0, snapshot.Text.Length);
        length = Math.Clamp(length, 0, snapshot.Text.Length - start);
        if (length == 0)
        {
            return 0;
        }

        using var context = CreateMeasurementContext(snapshot.Dpi);
        return context.Measure(
            snapshot.Text.AsSpan(start, length),
            GetFont(snapshot.DefaultStyle, snapshot.Dpi)).Width;
    }

    internal double[]? MeasureFastPathAdvances(TextLayoutRequestSnapshot snapshot, int start, int length)
    {
        start = Math.Clamp(start, 0, snapshot.Text.Length);
        length = Math.Clamp(length, 0, snapshot.Text.Length - start);
        if (length == 0)
        {
            return [];
        }

        using var context = CreateMeasurementContext(snapshot.Dpi);
        return context.SupportsUtf16PrefixAdvances
            ? context.GetUtf16PrefixAdvances(
                snapshot.Text.AsSpan(start, length),
                GetFont(snapshot.DefaultStyle, snapshot.Dpi))
            : null;
    }

    private static List<ManagedTextSegment> MeasureFastPathSegments(
        ITextBackendMeasurementContext context,
        string text,
        IFont font,
        out Size measured)
    {
        var segmentEnds = new List<int>(Math.Max(1, text.Length / FastPathSegmentLength + 1));
        int start = 0;
        while (start < text.Length)
        {
            int end = FindFastPathSegmentEnd(text, start);
            segmentEnds.Add(end);
            start = end;
        }

        var segments = new List<ManagedTextSegment>(segmentEnds.Count);
        double x = 0;
        double height = 0;
        start = 0;
        foreach (int end in segmentEnds)
        {
            var size = context.Measure(text.AsSpan(start, end - start), font);
            segments.Add(new ManagedTextSegment(start, end - start, x, Math.Max(0, size.Width)));
            x += Math.Max(0, size.Width);
            height = Math.Max(height, size.Height);
            start = end;
        }
        measured = new Size(x, height);
        return segments;
    }

    private static int FindFastPathSegmentEnd(string text, int start)
    {
        int target = Math.Min(text.Length, start + FastPathSegmentLength);
        if (target == text.Length)
        {
            return target;
        }
        if (char.IsAscii(text[target - 1]) && char.IsAscii(text[target]))
        {
            return target;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text, start);
        while (enumerator.MoveNext())
        {
            int boundary = enumerator.ElementIndex;
            if (boundary >= target)
            {
                return boundary;
            }
        }
        return text.Length;
    }

    /// <summary>
    /// The height a line of this font takes. A run of glyphs reports the measured height, which
    /// some backends pad to whole device pixels above the design metrics; a line that renders no
    /// glyph at all - an empty line, or one holding only tabs - has no run to take that from, so
    /// it measures the font here instead of falling back to the unpadded metrics and coming out
    /// shorter than the lines around it.
    /// </summary>
    private double GetFontLineHeight(ITextBackendMeasurementContext context, IFont font)
    {
        if (!_fontLineHeights.TryGetValue(font, out double height))
        {
            height = Math.Max(font.Ascent + font.Descent, context.Measure(" ", font).Height);
            _fontLineHeights.Add(font, height);
        }
        return height;
    }

    internal IFont GetFont(TextRunStyle style, uint dpi)
    {
        var key = new FontKey(style.FontFamily, style.FontSize, style.Weight, style.Italic, dpi);
        if (!_fonts.TryGetValue(key, out var font))
        {
            // Decorations are drawn by the text renderer as geometry; baking them into the
            // backend font would double-draw where fonts render them natively (and the cache
            // key intentionally ignores them).
            font = _factory.CreateFont(
                style.FontFamily,
                style.FontSize,
                dpi,
                style.Weight,
                style.Italic);
            _fonts.Add(key, font);
        }

        return font;
    }

    /// <summary>Writes the text element starts of the range into <paramref name="destination"/> and returns how many there were.</summary>
    private static int GetTextElementBoundaries(string text, int start, int end, int[] destination)
    {
        if (start == end)
        {
            return 0;
        }

        int count = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text, start);
        while (enumerator.MoveNext())
        {
            int index = enumerator.ElementIndex;
            if (index >= end)
            {
                break;
            }

            // A CR LF pair is one break, so the LF does not start an element of its own.
            if (index > start && text[index - 1] == '\r' && text[index] == '\n')
            {
                continue;
            }

            destination[count++] = index;
        }

        return count;
    }

    private static double NormalizeMaxWidth(double width)
        => double.IsNaN(width) || width <= 0 || double.IsPositiveInfinity(width)
            ? double.PositiveInfinity
            : width;

    /// <summary>Trailing whitespace width of the whole text, measured only when alignment needs it.</summary>
    private static double MeasureTrailingWhitespace(
        ITextBackendMeasurementContext context,
        TextLayoutRequestSnapshot snapshot,
        IFont font)
    {
        if (snapshot.Paragraph.Alignment == TextAlignment.Left ||
            double.IsPositiveInfinity(NormalizeMaxWidth(snapshot.Paragraph.MaxWidth)))
        {
            return 0;
        }

        var text = snapshot.Text.AsSpan();
        int start = text.Length;
        while (start > 0 && char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }
        return start == text.Length ? 0 : context.Measure(text[start..], font).Width;
    }

    /// <summary>
    /// The whitespace runs a wrap or an explicit break left at the end of a line, in both units. The
    /// character count is what column arithmetic needs, since a caller placing a selection works in
    /// columns and cannot divide a width back into characters.
    /// </summary>
    private static bool IsWhitespaceRun(string text, int start, int length)
    {
        for (int index = start; index < start + length; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return false;
            }
        }
        return length > 0;
    }

    private static double ResolveLineX(TextParagraphStyle paragraph, double width)
    {
        double maxWidth = NormalizeMaxWidth(paragraph.MaxWidth);
        if (double.IsPositiveInfinity(maxWidth))
        {
            return 0;
        }

        return paragraph.Alignment switch
        {
            TextAlignment.Center => Math.Max(0, (maxWidth - width) * 0.5),
            TextAlignment.Right => Math.Max(0, maxWidth - width),
            _ => 0
        };
    }

    /// <summary>
    /// Slack the wrap decision allows before it breaks a line. Backends report glyph advances as
    /// single-precision floats, so the width a caller measured and the width accumulated here can
    /// differ by a float epsilon; laying text out in the width it just measured would otherwise
    /// wrap against its own measurement. The slack stays far below one device pixel at any scale.
    /// </summary>
    private static double WrapTolerance(double maxWidth)
        => Math.Max(1e-6, Math.Abs(maxWidth) * 1e-6);

    private static double ResolveLineHeight(TextParagraphStyle paragraph, double fontHeight, double measuredHeight)
        => paragraph.LineHeight is > 0
            ? paragraph.LineHeight.Value
            : Math.Max(fontHeight, measuredHeight);

    /// <summary>
    /// Splits the room a line box has beyond the text's own ascent and descent evenly above and below it,
    /// the way a CSS line box does. Given to the descent side alone, a font with line gap - or a line
    /// height the paragraph set - holds its text against the top of the box.
    /// </summary>
    private static double ApplyHalfLeading(double baseline, double lineHeight, double textHeight)
        => baseline + (Math.Max(0, lineHeight - textHeight) / 2);

    private double ResolveLineCapHeight(
        ManagedTextLine line,
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextRun> runs)
    {
        double maxAscent = double.MinValue;
        double capHeight = 0;
        for (int index = 0; index < line.RunCount; index++)
        {
            var font = runs[line.RunStart + index].Font;
            if (font.Ascent > maxAscent)
            {
                maxAscent = font.Ascent;
                capHeight = font.CapHeight;
            }
        }

        return line.RunCount > 0 ? capHeight : GetFont(snapshot.DefaultStyle, snapshot.Dpi).CapHeight;
    }

    /// <summary>Trims the first line's box to its cap height and, when requested, the last line's bottom to its baseline.</summary>
    private void ApplyLineBoxTrim(
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextLine> lines,
        List<ManagedTextRun> runs)
    {
        if (snapshot.Paragraph.LineBoxTrim == LineBoxTrim.None || lines.Count == 0)
        {
            return;
        }

        var first = lines[0];
        double topTrim = Math.Max(0, first.Metrics.Baseline - ResolveLineCapHeight(first, snapshot, runs));
        if (topTrim > 0)
        {
            first.TrimTop = topTrim;
            var metrics = first.Metrics;
            first.Metrics = metrics with
            {
                Bounds = new Rect(metrics.Bounds.X, metrics.Bounds.Y, metrics.Bounds.Width, metrics.Bounds.Height - topTrim),
                Baseline = metrics.Baseline - topTrim
            };
            for (int index = 1; index < lines.Count; index++)
            {
                var shifted = lines[index].Metrics;
                lines[index].Metrics = shifted with
                {
                    Bounds = new Rect(shifted.Bounds.X, shifted.Bounds.Y - topTrim, shifted.Bounds.Width, shifted.Bounds.Height)
                };
            }
        }

        if (snapshot.Paragraph.LineBoxTrim == LineBoxTrim.CapAndBaseline)
        {
            var last = lines[^1];
            var metrics = last.Metrics;
            double bottomTrim = Math.Max(0, metrics.Bounds.Height - metrics.Baseline);
            if (bottomTrim > 0)
            {
                last.TrimBottom = bottomTrim;
                last.Metrics = metrics with
                {
                    Bounds = new Rect(metrics.Bounds.X, metrics.Bounds.Y, metrics.Bounds.Width, metrics.Bounds.Height - bottomTrim)
                };
            }
        }
    }

    private static double GetSpaceWidth(ITextBackendMeasurementContext context, IFont font, ref Dictionary<IFont, double>? cache)
    {
        cache ??= [];
        if (!cache.TryGetValue(font, out double width))
        {
            // Tab stops must land on real space advances; MeasureText pads to whole pixels on some backends.
            var advances = context.GetUtf16PrefixAdvances(" ", font);
            width = advances is { Length: > 0 }
                ? advances[0]
                : context.Measure(" ", font).Width;
            width = Math.Max(1, width);
            cache.Add(font, width);
        }
        return width;
    }

    /// <summary>
    /// Distance from <paramref name="x"/> to the next tab stop: the first explicit stop ahead of
    /// the pen, otherwise a repeating stop every <see cref="TextParagraphStyle.TabSize"/> spaces.
    /// </summary>
    private static double GetTabWidth(TextParagraphStyle paragraph, double x, double spaceWidth)
    {
        foreach (double stop in paragraph.TabStops)
        {
            if (stop > x)
            {
                return stop - x;
            }
        }

        double interval = spaceWidth * Math.Max(1, paragraph.TabSize);
        return interval - x % interval;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cache.Dispose();
        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }
        _fonts.Clear();
        _fontLineHeights.Clear();
    }

    private readonly record struct FontKey(string Family, double Size, FontWeight Weight, bool Italic, uint Dpi);
}

internal sealed class TextLayoutRequestSnapshot
{
    private string? _contentKey;
    private string? _ownerKey;

    private TextLayoutRequestSnapshot(
        string text,
        uint dpi,
        TextParagraphStyle paragraph,
        TextRunStyle defaultStyle,
        GeometryStyleRun[] runs,
        InlineRun[] inlines,
        TextFidelity fidelity,
        long revision,
        bool transient)
    {
        Text = text;
        Dpi = dpi;
        Paragraph = paragraph;
        DefaultStyle = defaultStyle;
        Runs = runs;
        Inlines = inlines;
        Fidelity = fidelity;
        Revision = revision;
        Transient = transient;
    }

    public string Text { get; }
    public uint Dpi { get; }
    public TextParagraphStyle Paragraph { get; }
    public TextRunStyle DefaultStyle { get; }
    public GeometryStyleRun[] Runs { get; }
    public InlineRun[] Inlines { get; }
    public TextFidelity Fidelity { get; }
    public long Revision { get; }
    public bool Transient { get; }
    public string ContentKey => _contentKey ??= CreateCacheKey(includeText: true);
    public string OwnerKey => _ownerKey ??= CreateCacheKey(includeText: false);
    internal bool HasMaterializedContentKey => _contentKey is not null;

    public static TextLayoutRequestSnapshot Create(TextLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Paragraph);
        ValidateStyle(request.DefaultStyle, nameof(request.DefaultStyle));

        string text = request.Text.ToString();
        uint dpi = request.Dpi == 0 ? 96 : request.Dpi;
        var runs = request.Runs?.ToArray() ?? [];
        var inlines = request.Inlines?.ToArray() ?? [];
        HashSet<int>? textElementBoundaries = null;
        if (runs.Length > 0 || inlines.Length > 0)
        {
            textElementBoundaries = new HashSet<int>(StringInfo.ParseCombiningCharacters(text)) { text.Length };
        }
        Array.Sort(runs, static (left, right) => left.Start.CompareTo(right.Start));
        int previousEnd = 0;
        foreach (var run in runs)
        {
            ValidateRange(run.Start, run.Length, text.Length, nameof(request.Runs));
            ValidateStyle(run.Style, nameof(request.Runs));
            ValidateTextElementRange(run.Start, run.Length, textElementBoundaries!, nameof(request.Runs));
            if (run.Start < previousEnd)
            {
                throw new ArgumentException("Geometry style runs must not overlap.", nameof(request));
            }
            previousEnd = run.End;
        }

        Array.Sort(inlines, static (left, right) => left.Position.CompareTo(right.Position));
        previousEnd = 0;
        foreach (var inline in inlines)
        {
            ArgumentNullException.ThrowIfNull(inline.Object);
            ValidateRange(inline.Position, inline.Length, text.Length, nameof(request.Inlines));
            ValidateTextElementRange(inline.Position, inline.Length, textElementBoundaries!, nameof(request.Inlines));
            if (inline.Length <= 0 || inline.Position < previousEnd)
            {
                throw new ArgumentException("Inline runs must be non-empty and must not overlap.", nameof(request));
            }
            previousEnd = checked(inline.Position + inline.Length);
        }

        var paragraph = request.Paragraph with
        {
            TabStops = request.Paragraph.TabStops?.ToArray() ?? [],
            Culture = request.Paragraph.Culture ?? CultureInfo.CurrentUICulture
        };
        if (paragraph.LineHeight is <= 0 ||
            !double.IsFinite(paragraph.LineSpacing) ||
            double.IsNaN(paragraph.MaxWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Paragraph metrics must be finite; the line height must be positive.");
        }

        return new TextLayoutRequestSnapshot(
            text,
            dpi,
            paragraph,
            request.DefaultStyle,
            runs,
            inlines,
            request.Fidelity,
            request.Revision,
            request.Transient);
    }

    /// <summary>Index of the run that styles this position in <see cref="Runs"/>, or -1 for the default style.</summary>
    public int GetStyleIndex(int textIndex)
    {
        for (int index = 0; index < Runs.Length; index++)
        {
            if (textIndex >= Runs[index].Start && textIndex < Runs[index].End)
            {
                return index;
            }
            if (Runs[index].Start > textIndex)
            {
                break;
            }
        }
        return -1;
    }

    public TextRunStyle GetStyle(int textIndex)
    {
        foreach (var run in Runs)
        {
            if (textIndex >= run.Start && textIndex < run.End)
            {
                return run.Style;
            }
            if (run.Start > textIndex)
            {
                break;
            }
        }
        return DefaultStyle;
    }

    public bool TryGetInline(int position, out InlineRun inline)
    {
        foreach (var candidate in Inlines)
        {
            if (candidate.Position == position)
            {
                inline = candidate;
                return true;
            }
            if (candidate.Position > position)
            {
                break;
            }
        }
        inline = default;
        return false;
    }

    private string CreateCacheKey(bool includeText)
    {
        var builder = new StringBuilder(includeText ? Text.Length + 128 : 128);
        if (includeText)
        {
            builder.Append(Text);
        }
        builder.Append('\u001f').Append(Dpi).Append('\u001f').Append((int)Fidelity)
            .Append('\u001f').Append(Paragraph.MaxWidth.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append(Paragraph.MaxHeight.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append((int)Paragraph.Wrapping).Append('\u001f').Append((int)Paragraph.Trimming)
            .Append('\u001f').Append((int)Paragraph.Alignment).Append('\u001f').Append((int)Paragraph.FlowDirection)
            .Append('\u001f').Append(Paragraph.Culture.Name).Append('\u001f').Append(Paragraph.Language)
            .Append('\u001f').Append(Paragraph.LineHeight?.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append(Paragraph.LineSpacing.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append(Paragraph.LetterSpacing.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append((int)Paragraph.LineBoxTrim).Append(':').Append(Paragraph.TabSize);
        AppendStyle(builder, DefaultStyle);
        foreach (double tab in Paragraph.TabStops)
        {
            builder.Append('\u001e').Append(tab.ToString("R", CultureInfo.InvariantCulture));
        }
        foreach (var run in Runs)
        {
            builder.Append('\u001d').Append(run.Start).Append(':').Append(run.Length);
            AppendStyle(builder, run.Style);
        }
        foreach (var inline in Inlines)
        {
            builder.Append('\u001c').Append(inline.Position).Append(':').Append(inline.Length)
                .Append(':').Append(RuntimeHelpers.GetHashCode(inline.Object));
        }
        return builder.ToString();
    }

    private static void AppendStyle(StringBuilder builder, TextRunStyle style)
        => builder.Append('\u001b').Append(style.FontFamily)
            .Append(':').Append(style.FontSize.ToString("R", CultureInfo.InvariantCulture))
            .Append(':').Append((int)style.Weight).Append(':').Append(style.Italic)
            .Append(':').Append((int)style.Decoration).Append(':').Append(style.Culture?.Name)
            .Append(':').Append(style.Language);

    private static void ValidateStyle(TextRunStyle style, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(style.FontFamily) || style.FontSize <= 0 || double.IsNaN(style.FontSize))
        {
            throw new ArgumentException("Text styles require a font family and positive font size.", parameterName);
        }
    }

    private static void ValidateRange(int start, int length, int textLength, string parameterName)
    {
        if (start < 0 || length < 0 || start > textLength - length)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateTextElementRange(
        int start,
        int length,
        HashSet<int> boundaries,
        string parameterName)
    {
        if (!boundaries.Contains(start) || !boundaries.Contains(checked(start + length)))
        {
            throw new ArgumentException(
                "Text ranges must start and end at Unicode text-element boundaries.",
                parameterName);
        }
    }
}

internal readonly record struct ManagedTextSegment(int Start, int Length, double X, double Width)
{
    public int End => checked(Start + Length);
}

internal enum ManagedTextRunKind { Text, Tab, NewLine, Inline }

/// <summary>
/// One laid-out piece of a line: a stretch of text in a single style, or the single column a tab,
/// a line break or an inline object occupies. A text run's columns are read from the layout's
/// advance array between <see cref="AdvanceStart"/> and the run's length.
/// </summary>
internal struct ManagedTextRun
{
    public int TextStart;
    public int TextLength;
    public int StyleIndex;
    public IFont Font;
    public double X;
    public double Width;
    public int AdvanceStart;

    /// <summary>Advance the run starts at, subtracted from every read so a split fragment still measures from its own left edge.</summary>
    public float AdvanceBase;
    public double MeasuredHeight;
    public double Baseline;
    public ManagedTextRunKind Kind;
    public int InlineIndex;

    public readonly int TextEnd => checked(TextStart + TextLength);
}

internal sealed class ManagedTextLine(
    TextLayoutLineMetrics metrics,
    IReadOnlyList<ManagedTextSegment>? fastSegments = null)
{
    public TextLayoutLineMetrics Metrics { get; set; } = metrics;
    public IReadOnlyList<ManagedTextSegment>? FastSegments { get; } = fastSegments;

    // Range in the layout's run array. Count is -1 until the runs for this line are built.
    public int RunStart { get; set; }
    public int RunCount { get; set; } = -1;

    /// <summary>True when trimming dropped trailing content and an ellipsis follows the clusters.</summary>
    public bool IsTrimmed { get; set; }

    /// <summary>Line-box trim taken off the top; the ink still renders at the untrimmed position.</summary>
    public double TrimTop { get; set; }

    /// <summary>Line-box trim taken off the bottom (descent and line-height surplus).</summary>
    public double TrimBottom { get; set; }
}

internal sealed class ManagedTextLayoutCache : ITextLayoutCache, IDisposable
{
    private const int ContentCapacity = 256;
    private readonly ManagedTextEngine _engine;
    private readonly Dictionary<string, ManagedTextLayout> _content = [];
    private readonly Queue<string> _contentOrder = [];
    private readonly ConditionalWeakTable<object, OwnerEntry> _owners = new();
    private int _ownerCount;

    public ManagedTextLayoutCache(ManagedTextEngine engine) => _engine = engine;

    public int Count => _content.Count + _ownerCount;

    public ManagedTextLayout GetOrCreate(
        TextLayoutRequestSnapshot snapshot,
        TextLayoutCachePolicy policy,
        object? owner)
    {
        if (policy == TextLayoutCachePolicy.Owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (_owners.TryGetValue(owner, out var entry) &&
                entry.Revision == snapshot.Revision &&
                entry.OwnerKey == snapshot.OwnerKey)
            {
                return entry.Layout;
            }

            var layout = _engine.CreateLayoutCore(snapshot);
            if (entry is null)
            {
                _owners.Add(owner, new OwnerEntry(snapshot.Revision, snapshot.OwnerKey, layout));
                _ownerCount++;
            }
            else
            {
                entry.Revision = snapshot.Revision;
                entry.OwnerKey = snapshot.OwnerKey;
                entry.Layout = layout;
            }
            return layout;
        }

        if (snapshot.Inlines.Length > 0)
        {
            throw new ArgumentException("Layouts containing inline objects require owner caching.", nameof(snapshot));
        }
        if (_content.TryGetValue(snapshot.ContentKey, out var cached))
        {
            return cached;
        }

        var created = _engine.CreateLayoutCore(snapshot);
        _content.Add(snapshot.ContentKey, created);
        _contentOrder.Enqueue(snapshot.ContentKey);
        while (_content.Count > ContentCapacity && _contentOrder.TryDequeue(out string? oldest))
        {
            _content.Remove(oldest);
        }
        return created;
    }

    public void ReleaseOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owners.Remove(owner))
        {
            _ownerCount--;
        }
    }

    public void Trim()
    {
        _content.Clear();
        _contentOrder.Clear();
    }

    public void Dispose() => Trim();

    private sealed class OwnerEntry(long revision, string ownerKey, ManagedTextLayout layout)
    {
        public long Revision { get; set; } = revision;
        public string OwnerKey { get; set; } = ownerKey;
        public ManagedTextLayout Layout { get; set; } = layout;
    }
}
