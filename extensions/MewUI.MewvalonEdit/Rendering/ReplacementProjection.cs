using System.Text;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Builds the projected text of a line whose source ranges are shown as something else: a folded
/// region as its placeholder, a generated element as the text it draws. Both cases are the same
/// transform, so they share this builder and the offset map it produces.
/// </summary>
internal static class ReplacementProjection
{
    /// <summary>One source range and the text shown in its place. Empty text hides the range.</summary>
    internal readonly record struct Replacement(int SourceStart, int SourceLength, string Text);

    /// <summary>
    /// Applies <paramref name="replacements"/>, which must be sorted by start offset and must not
    /// overlap; anything that does is skipped rather than corrupting the offsets that follow.
    /// </summary>
    public static ProjectedText Build(ReadOnlyMemory<char> source, List<Replacement> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count == 0)
        {
            return new ProjectedText(source, IdentityTextOffsetMap.Instance);
        }

        var span = source.Span;
        var builder = new StringBuilder(span.Length);
        var segments = new List<ReplacementOffsetMap.Segment>(replacements.Count);
        int consumed = 0;
        foreach (var replacement in replacements)
        {
            int start = replacement.SourceStart;
            if (start < consumed || start + replacement.SourceLength > span.Length)
            {
                continue;
            }
            builder.Append(span[consumed..start]);
            segments.Add(new ReplacementOffsetMap.Segment(
                start, replacement.SourceLength, builder.Length, replacement.Text.Length));
            builder.Append(replacement.Text);
            consumed = start + replacement.SourceLength;
        }
        if (segments.Count == 0)
        {
            return new ProjectedText(source, IdentityTextOffsetMap.Instance);
        }
        builder.Append(span[consumed..]);
        return new ProjectedText(builder.ToString().AsMemory(), new ReplacementOffsetMap([.. segments]));
    }
}

/// <summary>
/// Line-relative offset map for ranges whose projected text has a different length than the
/// document text. Offsets inside a replaced range collapse to its start on both axes, which is
/// what places the caret before a folded region rather than inside it.
/// </summary>
internal sealed class ReplacementOffsetMap(ReplacementOffsetMap.Segment[] segments) : ITextOffsetMap
{
    internal readonly record struct Segment(int SourceStart, int SourceLength, int ProjectedStart, int ProjectedLength);

    public int MapFromSource(int sourceOffset)
    {
        int delta = 0;
        foreach (var segment in segments)
        {
            if (sourceOffset < segment.SourceStart)
            {
                break;
            }
            if (sourceOffset < segment.SourceStart + segment.SourceLength)
            {
                return segment.ProjectedStart;
            }
            // Text standing at a source position rather than over one leaves that position in front
            // of it, so the caret at the end of a line stays before an end-of-line marker.
            if (segment.SourceLength == 0 && sourceOffset == segment.SourceStart)
            {
                return segment.ProjectedStart;
            }
            delta += segment.ProjectedLength - segment.SourceLength;
        }
        return sourceOffset + delta;
    }

    public int MapToSource(int projectedOffset)
    {
        int delta = 0;
        foreach (var segment in segments)
        {
            if (projectedOffset < segment.ProjectedStart)
            {
                break;
            }
            if (projectedOffset < segment.ProjectedStart + segment.ProjectedLength)
            {
                return segment.SourceStart;
            }
            delta += segment.SourceLength - segment.ProjectedLength;
        }
        return projectedOffset + delta;
    }
}
