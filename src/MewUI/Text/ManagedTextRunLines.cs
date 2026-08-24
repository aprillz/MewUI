using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal sealed partial class ManagedTextEngine
{
    /// <summary>
    /// One column-bearing step of the text: a grapheme of a text fragment, or the whole of a tab, a
    /// break or an inline object. What the cluster list used to be, read off the fragments.
    /// </summary>
    private readonly struct LayoutCell(ManagedTextFragments fragments, int fragmentIndex, int start, int end)
    {
        public int FragmentIndex { get; } = fragmentIndex;
        public int Start { get; } = start;
        public int End { get; } = end;

        public ref readonly ManagedTextFragment Fragment => ref fragments.Items[FragmentIndex];
        public ManagedTextRunKind Kind => Fragment.Kind;
        public int Length => End - Start;

        /// <summary>Width of a text cell. A tab is resolved where it lands, so it reports none here.</summary>
        public double Width => Kind switch
        {
            ManagedTextRunKind.Text => fragments.AdvanceBetween(in Fragment, Start, End),
            ManagedTextRunKind.Inline => Fragment.Width,
            _ => 0
        };

        public bool IsBreakOpportunity(string text)
            => Fragment.BreaksLine ||
               (Kind == ManagedTextRunKind.Text && Length > 0 && char.IsWhiteSpace(text, Start));
    }

    private static List<LayoutCell> BuildCells(ManagedTextFragments fragments)
    {
        var cells = new List<LayoutCell>(Math.Max(4, fragments.BoundaryCount + fragments.Count));
        for (int index = 0; index < fragments.Count; index++)
        {
            ref readonly var fragment = ref fragments.Items[index];
            if (fragment.Kind != ManagedTextRunKind.Text)
            {
                cells.Add(new LayoutCell(fragments, index, fragment.TextStart, fragment.TextEnd));
                continue;
            }

            var boundaries = fragments.BoundariesOf(in fragment);
            for (int boundary = 0; boundary < boundaries.Length; boundary++)
            {
                int start = boundaries[boundary];
                int end = boundary + 1 < boundaries.Length ? boundaries[boundary + 1] : fragment.TextEnd;
                cells.Add(new LayoutCell(fragments, index, start, end));
            }
        }

        return cells;
    }

    /// <summary>
    /// Breaks the measured fragments into lines of runs. Mirrors the cluster assembler: cells are
    /// taken one at a time, so a break always lands on a text element without having to be snapped
    /// back to one.
    /// </summary>
    internal List<ManagedTextLine> AssembleRunLines(
        ITextBackendMeasurementContext context,
        TextLayoutRequestSnapshot snapshot,
        ManagedTextFragments fragments,
        List<ManagedTextRun> runs)
    {
        var cells = BuildCells(fragments);
        var lines = new List<ManagedTextLine>();
        var cellWidths = new double[cells.Count];
        double y = 0;
        int lineStart = 0;
        int index = 0;
        double maxWidth = NormalizeMaxWidth(snapshot.Paragraph.MaxWidth);
        Dictionary<IFont, double>? spaceWidths = null;

        while (index < cells.Count)
        {
            int scan = index;
            int lastBreak = -1;
            double width = 0;
            bool explicitBreak = false;

            while (scan < cells.Count)
            {
                var cell = cells[scan];
                if (cell.Kind == ManagedTextRunKind.NewLine)
                {
                    explicitBreak = true;
                    break;
                }

                double cellWidth = cell.Kind == ManagedTextRunKind.Tab
                    ? GetTabWidth(snapshot.Paragraph, width, GetSpaceWidth(context, cell.Fragment.Font, ref spaceWidths))
                    : cell.Width;
                bool exceeds = snapshot.Paragraph.Wrapping != TextWrapping.NoWrap &&
                               !double.IsPositiveInfinity(maxWidth) &&
                               width + cellWidth > maxWidth + WrapTolerance(maxWidth) &&
                               scan > index;
                if (exceeds)
                {
                    if (lastBreak >= index)
                    {
                        scan = lastBreak + 1;
                        break;
                    }

                    if (snapshot.Paragraph.Wrapping != TextWrapping.WrapWithOverflow)
                    {
                        break;
                    }
                }

                cellWidths[scan] = cellWidth;
                width += cellWidth;
                if (cell.IsBreakOpportunity(snapshot.Text))
                {
                    lastBreak = scan;
                }
                scan++;
            }

            int contentEnd = scan;
            if (contentEnd == index && !explicitBreak && scan < cells.Count)
            {
                contentEnd = ++scan;
            }

            var line = CreateRunLine(
                context,
                snapshot,
                fragments,
                cells,
                cellWidths,
                runs,
                index,
                contentEnd,
                y,
                explicitBreak ? cells[scan].Length : 0,
                lineStart);
            lines.Add(line);
            y = Math.Max(line.Metrics.Bounds.Y, line.Metrics.Bounds.Bottom + snapshot.Paragraph.LineSpacing);

            if (explicitBreak)
            {
                lineStart = cells[scan].End;
                index = scan + 1;
            }
            else
            {
                lineStart = contentEnd < cells.Count ? cells[contentEnd].Start : snapshot.Text.Length;
                index = contentEnd;
            }
        }

        if (cells.Count == 0 || cells[^1].Kind == ManagedTextRunKind.NewLine)
        {
            var font = GetFont(snapshot.DefaultStyle, snapshot.Dpi);
            double fontHeight = GetFontLineHeight(context, font);
            double height = ResolveLineHeight(snapshot.Paragraph, fontHeight, fontHeight);
            double baseline = ApplyHalfLeading(font.Ascent, height, font.Ascent + font.Descent);
            lines.Add(new ManagedTextLine(
                new TextLayoutLineMetrics(
                    snapshot.Text.Length, 0, 0, new Rect(ResolveLineX(snapshot.Paragraph, 0), y, 0, height), baseline),
                clusters: null)
            {
                RunStart = runs.Count,
                RunCount = 0
            });
        }

        return lines;
    }

    /// <summary>Lays the text out as runs over measured fragments, the way the cluster path does with clusters.</summary>
    internal ManagedTextLayout CreateLayoutViaRuns(TextLayoutRequestSnapshot snapshot)
    {
        using var context = CreateMeasurementContext(snapshot.Dpi);
        var fragments = MeasureFragments(context, snapshot);
        var runs = new List<ManagedTextRun>();
        var lines = AssembleRunLines(context, snapshot, fragments, runs);
        ApplyLineBoxTrim(snapshot, lines);

        double measuredWidth = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            var metrics = lines[index].Metrics;
            bool countTrailingWhitespace = !lines[index].IsTrimmed &&
                (metrics.NewLineLength > 0 || index == lines.Count - 1);
            measuredWidth = Math.Max(
                measuredWidth, countTrailingWhitespace ? metrics.Bounds.Width : metrics.VisibleWidth);
        }

        double contentHeight = lines.Count == 0 ? 0 : lines[^1].Metrics.Bounds.Bottom;
        return new ManagedTextLayout(
            this, snapshot, lines, new Size(measuredWidth, contentHeight), fragments, runs);
    }

    private ManagedTextLine CreateRunLine(
        ITextBackendMeasurementContext context,
        TextLayoutRequestSnapshot snapshot,
        ManagedTextFragments fragments,
        List<LayoutCell> cells,
        double[] cellWidths,
        List<ManagedTextRun> runs,
        int start,
        int end,
        double y,
        int newLineLength,
        int fallbackStart)
    {
        var defaultFont = GetFont(snapshot.DefaultStyle, snapshot.Dpi);
        double width = 0;
        double naturalHeight = 0;
        double baseline = 0;
        double textHeight = 0;
        for (int index = start; index < end; index++)
        {
            ref readonly var fragment = ref cells[index].Fragment;
            width += cellWidths[index];
            naturalHeight = Math.Max(naturalHeight, fragment.MeasuredHeight);
            baseline = Math.Max(baseline, fragment.Baseline);
            // Measured against the fonts' own ascent and descent, not the measured heights: those
            // already carry the font's line gap, so comparing with them would find nothing to split.
            textHeight = Math.Max(textHeight, fragment.Font.Ascent + fragment.Font.Descent);
        }

        double height = ResolveLineHeight(snapshot.Paragraph, GetFontLineHeight(context, defaultFont), naturalHeight);
        if (baseline <= 0)
        {
            baseline = defaultFont.Ascent;
        }
        if (textHeight <= 0)
        {
            textHeight = defaultFont.Ascent + defaultFont.Descent;
        }
        baseline = ApplyHalfLeading(baseline, height, textHeight);

        (double trailingWhitespace, int trailingWhitespaceLength) =
            GetTrailingWhitespace(snapshot, cells, cellWidths, start, end);
        double x = ResolveLineX(snapshot.Paragraph, width - trailingWhitespace);

        int runStart = runs.Count;
        double cursor = x;
        int cell = start;
        while (cell < end)
        {
            int last = cell;
            if (cells[cell].Kind == ManagedTextRunKind.Text)
            {
                while (last + 1 < end &&
                       cells[last + 1].Kind == ManagedTextRunKind.Text &&
                       cells[last + 1].FragmentIndex == cells[cell].FragmentIndex)
                {
                    last++;
                }
            }

            runs.Add(CreateRun(fragments, cells, cellWidths, cell, last, cursor));
            for (int index = cell; index <= last; index++)
            {
                cursor += cellWidths[index];
            }
            cell = last + 1;
        }

        int textStart = start == end ? fallbackStart : cells[start].Start;
        int textLength = start == end ? 0 : cells[end - 1].End - textStart;
        return new ManagedTextLine(
            new TextLayoutLineMetrics(
                textStart,
                textLength,
                newLineLength,
                new Rect(x, y, width, height),
                baseline,
                trailingWhitespace,
                trailingWhitespaceLength),
            clusters: null)
        {
            RunStart = runStart,
            RunCount = runs.Count - runStart
        };
    }

    private static ManagedTextRun CreateRun(
        ManagedTextFragments fragments,
        List<LayoutCell> cells,
        double[] cellWidths,
        int start,
        int last,
        double x)
    {
        ref readonly var fragment = ref cells[start].Fragment;
        int textStart = cells[start].Start;
        double width = 0;
        for (int index = start; index <= last; index++)
        {
            width += cellWidths[index];
        }

        int advanceStart = -1;
        float advanceBase = 0;
        if (fragment.Kind == ManagedTextRunKind.Text)
        {
            advanceStart = fragment.AdvanceStart + (textStart - fragment.TextStart);
            // A fragment split across lines shares its advances, so each run measures from its own
            // left edge rather than from the fragment's.
            advanceBase = (float)fragments.AdvanceTo(in fragment, textStart);
        }

        return new ManagedTextRun
        {
            TextStart = textStart,
            TextLength = cells[last].End - textStart,
            StyleIndex = fragment.StyleIndex,
            Font = fragment.Font,
            X = x,
            Width = width,
            AdvanceStart = advanceStart,
            AdvanceBase = advanceBase,
            MeasuredHeight = fragment.MeasuredHeight,
            Baseline = fragment.Baseline,
            Kind = fragment.Kind,
            InlineIndex = fragment.InlineIndex
        };
    }

    private static (double Width, int Length) GetTrailingWhitespace(
        TextLayoutRequestSnapshot snapshot,
        List<LayoutCell> cells,
        double[] cellWidths,
        int start,
        int end)
    {
        double width = 0;
        int length = 0;
        for (int index = end - 1; index >= start; index--)
        {
            var cell = cells[index];
            if (cell.Kind == ManagedTextRunKind.NewLine)
            {
                continue;
            }
            if (cell.Kind != ManagedTextRunKind.Text ||
                !IsWhitespaceRun(snapshot.Text, cell.Start, cell.Length))
            {
                break;
            }

            width += cellWidths[index];
            length += cell.Length;
        }

        return (width, length);
    }
}
