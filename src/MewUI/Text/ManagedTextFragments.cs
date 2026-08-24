using System.Buffers;
using System.Globalization;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

/// <summary>
/// One measured piece of a layout's text: a stretch in a single style, or the single column a tab,
/// a line break or an inline object occupies. Text pieces carry their columns in the fragment set's
/// advance array rather than as an object per grapheme.
/// </summary>
internal struct ManagedTextFragment
{
    public int TextStart;
    public int TextLength;
    public int StyleIndex;
    public IFont Font;
    public ManagedTextRunKind Kind;
    public int InlineIndex;

    /// <summary>Height the backend measured for this piece, which a fallback glyph can raise above the font's own.</summary>
    public double MeasuredHeight;
    public double Baseline;

    /// <summary>Total width. A tab's is resolved while breaking lines, since it depends on where the tab lands.</summary>
    public double Width;

    /// <summary>Set by an inline object that stands in for text a line may break after.</summary>
    public bool BreaksLine;

    /// <summary>Index in the advance array of this piece's first code unit, or -1 for a piece that has no columns.</summary>
    public int AdvanceStart;

    public int BoundaryStart;
    public int BoundaryCount;

    public readonly int TextEnd => checked(TextStart + TextLength);
}

/// <summary>
/// The measured pieces of one layout, with the advances and text-element boundaries they index into.
/// Replaces the per-grapheme cluster list as the input to line breaking.
/// </summary>
internal sealed class ManagedTextFragments
{
    public ManagedTextFragment[] Items = [];
    public int Count;

    /// <summary>Cumulative width from each text piece's start, one entry per code unit.</summary>
    public float[] Advances = [];
    public int AdvanceCount;

    /// <summary>Text-element starts, absolute, in the order the pieces were measured.</summary>
    public int[] Boundaries = [];
    public int BoundaryCount;

    public ReadOnlySpan<ManagedTextFragment> Span => Items.AsSpan(0, Count);

    /// <summary>Width of the piece's text from its start up to <paramref name="offset"/>.</summary>
    public double AdvanceTo(in ManagedTextFragment fragment, int offset)
    {
        if (fragment.AdvanceStart < 0 || offset <= fragment.TextStart)
        {
            return 0;
        }

        int limit = Math.Min(offset, fragment.TextEnd);
        return Advances[fragment.AdvanceStart + (limit - fragment.TextStart) - 1];
    }

    /// <summary>Width of the piece's text between two offsets inside it.</summary>
    public double AdvanceBetween(in ManagedTextFragment fragment, int start, int end)
        => AdvanceTo(in fragment, end) - AdvanceTo(in fragment, start);

    public ReadOnlySpan<int> BoundariesOf(in ManagedTextFragment fragment)
        => Boundaries.AsSpan(fragment.BoundaryStart, fragment.BoundaryCount);

    /// <summary>Index of the fragment covering this position.</summary>
    public int IndexOfFragment(int textStart)
    {
        int low = 0;
        int high = Count - 1;
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if (Items[middle].TextStart <= textStart)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return low;
    }

    public void AddFragment(in ManagedTextFragment fragment)
    {
        if (Count == Items.Length)
        {
            Array.Resize(ref Items, Math.Max(4, Items.Length * 2));
        }
        Items[Count++] = fragment;
    }

    public void AddBoundary(int boundary)
    {
        if (BoundaryCount == Boundaries.Length)
        {
            Array.Resize(ref Boundaries, Math.Max(16, Boundaries.Length * 2));
        }
        Boundaries[BoundaryCount++] = boundary;
    }

    public void EnsureAdvanceCapacity(int required)
    {
        if (Advances.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(16, Advances.Length);
        while (capacity < required)
        {
            capacity *= 2;
        }
        Array.Resize(ref Advances, capacity);
    }
}

internal sealed partial class ManagedTextEngine
{
    /// <summary>
    /// Measures the text into fragments: the same pieces the cluster list described, with each
    /// piece's columns written into one advance array instead of an object per grapheme.
    /// </summary>
    /// <summary>Measures the text into fragments through a context of its own.</summary>
    internal ManagedTextFragments MeasureFragments(TextLayoutRequestSnapshot snapshot)
    {
        using var context = CreateMeasurementContext(snapshot.Dpi);
        return MeasureFragments(context, snapshot);
    }

    internal ManagedTextFragments MeasureFragments(
        ITextBackendMeasurementContext context,
        TextLayoutRequestSnapshot snapshot)
    {
        var fragments = new ManagedTextFragments();
        string text = snapshot.Text;
        // One element per code unit is the most there can be, and the array goes back to the pool
        // once the fragments have copied the boundaries they keep.
        int[] boundaryBuffer = ArrayPool<int>.Shared.Rent(Math.Max(1, text.Length));
        try
        {
            MeasureFragmentsCore(context, snapshot, fragments, boundaryBuffer);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(boundaryBuffer);
        }

        return fragments;
    }

    private void MeasureFragmentsCore(
        ITextBackendMeasurementContext context,
        TextLayoutRequestSnapshot snapshot,
        ManagedTextFragments fragments,
        int[] boundaryBuffer)
    {
        string text = snapshot.Text;
        int boundaryCount = GetTextElementBoundaries(text, 0, text.Length, boundaryBuffer);
        var boundaries = boundaryBuffer.AsSpan(0, boundaryCount);
        int index = 0;

        while (index < boundaries.Length)
        {
            int start = boundaries[index];
            int end = index + 1 < boundaries.Length ? boundaries[index + 1] : text.Length;
            int styleIndex = snapshot.GetStyleIndex(start);
            var style = snapshot.GetStyle(start);
            var font = GetFont(style, snapshot.Dpi);

            if (snapshot.TryGetInline(start, out var inline))
            {
                AddInlineFragment(fragments, snapshot, in inline, start, end - start, styleIndex, font);
                int inlineEnd = checked(inline.Position + inline.Length);
                while (index + 1 < boundaries.Length && boundaries[index + 1] < inlineEnd)
                {
                    index++;
                }
                index++;
                continue;
            }

            var span = text.AsSpan(start, end - start);
            if (span is ['\r'] or ['\n'] or ['\r', '\n'] or ['\t'])
            {
                fragments.AddFragment(new ManagedTextFragment
                {
                    TextStart = start,
                    TextLength = end - start,
                    StyleIndex = styleIndex,
                    Font = font,
                    Kind = span[0] == '\t' ? ManagedTextRunKind.Tab : ManagedTextRunKind.NewLine,
                    InlineIndex = -1,
                    MeasuredHeight = font.Ascent + font.Descent,
                    Baseline = font.Ascent,
                    Width = 0,
                    AdvanceStart = -1
                });
                index++;
                continue;
            }

            // The piece runs while the style holds and nothing interrupts it, which is exactly the
            // stretch a backend can measure in one call.
            int last = index;
            while (last + 1 < boundaries.Length)
            {
                int nextStart = boundaries[last + 1];
                if (snapshot.GetStyleIndex(nextStart) != styleIndex ||
                    snapshot.TryGetInline(nextStart, out _))
                {
                    break;
                }

                int nextEnd = last + 2 < boundaries.Length ? boundaries[last + 2] : text.Length;
                var nextSpan = text.AsSpan(nextStart, nextEnd - nextStart);
                if (nextSpan is ['\r'] or ['\n'] or ['\r', '\n'] or ['\t'])
                {
                    break;
                }

                last++;
            }

            int pieceEnd = last + 1 < boundaries.Length ? boundaries[last + 1] : text.Length;
            AddTextFragment(fragments, context, snapshot, boundaries, index, last, start, pieceEnd, styleIndex, font);
            index = last + 1;
        }
    }

    private static void AddInlineFragment(
        ManagedTextFragments fragments,
        TextLayoutRequestSnapshot snapshot,
        in InlineRun inline,
        int start,
        int boundaryLength,
        int styleIndex,
        IFont font)
    {
        var metrics = inline.Object.Measure();
        int inlineIndex = Array.IndexOf(snapshot.Inlines, inline);
        fragments.AddFragment(new ManagedTextFragment
        {
            TextStart = start,
            TextLength = Math.Max(boundaryLength, inline.Length),
            StyleIndex = styleIndex,
            Font = font,
            Kind = ManagedTextRunKind.Inline,
            InlineIndex = inlineIndex,
            MeasuredHeight = metrics.Height,
            Baseline = metrics.Baseline,
            // Whole device pixels, as every text advance already is: an object free to report a
            // fractional width would push the rest of the line off the pixel grid.
            Width = LayoutRounding.RoundToPixel(metrics.Width, snapshot.Dpi / 96.0),
            BreaksLine = inline.BreaksLine,
            AdvanceStart = -1
        });
    }

    private void AddTextFragment(
        ManagedTextFragments fragments,
        ITextBackendMeasurementContext context,
        TextLayoutRequestSnapshot snapshot,
        ReadOnlySpan<int> boundaries,
        int firstBoundary,
        int lastBoundary,
        int start,
        int end,
        int styleIndex,
        IFont font)
    {
        int length = end - start;
        var fragment = new ManagedTextFragment
        {
            TextStart = start,
            TextLength = length,
            StyleIndex = styleIndex,
            Font = font,
            Kind = ManagedTextRunKind.Text,
            InlineIndex = -1,
            MeasuredHeight = font.Ascent + font.Descent,
            Baseline = font.Ascent,
            AdvanceStart = fragments.AdvanceCount,
            BoundaryStart = fragments.BoundaryCount,
            BoundaryCount = lastBoundary - firstBoundary + 1
        };

        for (int index = firstBoundary; index <= lastBoundary; index++)
        {
            fragments.AddBoundary(boundaries[index]);
        }

        fragments.EnsureAdvanceCapacity(fragments.AdvanceCount + length);
        var span = snapshot.Text.AsSpan(start, length);
        double letterSpacing = snapshot.Paragraph.LetterSpacing;

        double[]? cumulative = context.SupportsUtf16PrefixAdvances
            ? context.GetUtf16PrefixAdvances(span, font)
            : null;
        if (cumulative is not null)
        {
            var measured = context.Measure(span, font);
            fragment.MeasuredHeight = Math.Max(fragment.MeasuredHeight, measured.Height);
        }

        double cursor = 0;
        double previous = 0;
        int written = fragments.AdvanceCount;
        for (int index = firstBoundary; index <= lastBoundary; index++)
        {
            int graphemeStart = boundaries[index];
            int graphemeEnd = index < lastBoundary ? boundaries[index + 1] : end;
            double width;
            if (cumulative is not null)
            {
                double current = cumulative[graphemeEnd - start - 1];
                width = Math.Max(0, current - previous + letterSpacing);
                previous = current;
            }
            else
            {
                // No advance source: the backend can only measure text it is given, so each grapheme
                // is measured on its own, exactly as the cluster path did.
                var graphemeSize = context.Measure(
                    snapshot.Text.AsSpan(graphemeStart, graphemeEnd - graphemeStart), font);
                width = Math.Max(0, graphemeSize.Width + letterSpacing);
                fragment.MeasuredHeight = Math.Max(fragment.MeasuredHeight, graphemeSize.Height);
            }

            cursor += width;
            // Every code unit of a grapheme carries the grapheme's far edge: there is no column
            // inside one, and an insertion there reads as its end.
            for (int unit = graphemeStart; unit < graphemeEnd; unit++)
            {
                fragments.Advances[written++] = (float)cursor;
            }
        }

        fragments.AdvanceCount = written;
        fragment.Width = cursor;
        fragments.AddFragment(in fragment);
    }
}
