namespace Aprillz.MewUI.Text;

/// <summary>Classification input. <see cref="Text"/> is the projected display text; <see cref="OffsetMap"/> converts between its offsets and source document offsets.</summary>
public readonly record struct TextClassificationContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text,
    ITextOffsetMap OffsetMap);

public interface ITextClassifier
{
    void Classify(in TextClassificationContext context, IList<TextPaintSpan> output);
}

/// <summary>Transform input. <see cref="Text"/> is the projected display text; <see cref="OffsetMap"/> converts between its offsets and source document offsets.</summary>
public readonly record struct TextLineTransformContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text,
    TextRunStyle DefaultStyle,
    ITextOffsetMap OffsetMap);

public interface ITextLineTransformer
{
    void Transform(
        in TextLineTransformContext context,
        IList<GeometryStyleRun> geometryRuns,
        IList<InlineRun> inlines);
}

/// <summary>
/// Element scan input. Offsets are document offsets, before any projection.
/// <see cref="ScanStartOffset"/> is where this walk begins, which is the line's start except on a
/// line long enough to be laid out one piece at a time.
/// </summary>
public readonly record struct TextElementScanContext(IReadOnlyTextDocument Document, int ScanStartOffset);

/// <summary>
/// An element standing in for a document range. <see cref="VisualLength"/> is how many columns it
/// occupies on the visual surface; <see cref="Object"/> paints them, or null to leave the range as
/// ordinary text that the element only decorates. <see cref="BreaksLine"/> declares that a line may
/// break after it, which an element standing in for whitespace has to say.
/// </summary>
public readonly record struct GeneratedTextElement(
    int DocumentLength,
    int VisualLength,
    IInlineTextObject? Object,
    bool BreaksLine = false);

public interface ITextElementGenerator
{
    /// <summary>
    /// First document offset at or after <paramref name="startOffset"/> this generator wants an
    /// element at, or -1 for none. Must not return an offset before <paramref name="startOffset"/>.
    /// </summary>
    int GetFirstInterestedOffset(in TextElementScanContext context, int startOffset);

    /// <summary>
    /// The element at <paramref name="offset"/>, or null to decline. A
    /// <see cref="GeneratedTextElement.DocumentLength"/> reaching past the line's end makes the
    /// line cover the logical lines up to it; the lines it swallows must then be collapsed through
    /// an <see cref="ITextLineCollapser"/>, or they are laid out a second time on their own.
    /// </summary>
    GeneratedTextElement? ConstructElement(in TextElementScanContext context, int offset);
}

/// <summary>
/// Built-in position an inserted layer is placed against. A layer inserted below an anchor paints
/// under that anchor's own content, so an anchor names what a layer sits beneath, not what it covers.
/// </summary>
public enum TextViewLayerAnchor
{
    /// <summary>The line backgrounds, the bottom of the stack.</summary>
    Background,

    /// <summary>The selection highlight.</summary>
    Selection,

    /// <summary>The glyphs.</summary>
    Text,

    /// <summary>The caret.</summary>
    Caret
}

public interface ITextOffsetMap
{
    int MapToSource(int projectedOffset);

    /// <summary>
    /// Projected offset a source offset sits at. Where the projection stands text at that offset
    /// rather than over it, the answer is in front of that text.
    /// </summary>
    int MapFromSource(int sourceOffset);

    /// <summary>
    /// Projected range a source range covers. The start binds in front of text the projection
    /// stands at that offset and the end binds behind it, which is the only way a range of no
    /// source length can name the text projected at its position. The default answers from the two
    /// endpoints, which is exact for a map that stands text over source text only.
    /// </summary>
    TextRange MapRangeFromSource(int sourceOffset, int sourceLength)
    {
        int start = MapFromSource(sourceOffset);
        return new TextRange(start, Math.Max(0, MapFromSource(sourceOffset + sourceLength) - start));
    }
}

public sealed class IdentityTextOffsetMap : ITextOffsetMap
{
    public static IdentityTextOffsetMap Instance { get; } = new();

    private IdentityTextOffsetMap() { }

    public int MapToSource(int projectedOffset) => projectedOffset;
    public int MapFromSource(int sourceOffset) => sourceOffset;
}

internal sealed class ComposedTextOffsetMap(ITextOffsetMap sourceMap, ITextOffsetMap projectedMap) : ITextOffsetMap
{
    public int MapToSource(int projectedOffset)
        => sourceMap.MapToSource(projectedMap.MapToSource(projectedOffset));

    public int MapFromSource(int sourceOffset)
        => projectedMap.MapFromSource(sourceMap.MapFromSource(sourceOffset));

    public TextRange MapRangeFromSource(int sourceOffset, int sourceLength)
    {
        var inner = sourceMap.MapRangeFromSource(sourceOffset, sourceLength);
        return projectedMap.MapRangeFromSource(inner.Start, inner.Length);
    }
}

public readonly record struct TextProjectionContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> SourceText);

public readonly record struct ProjectedText(ReadOnlyMemory<char> Text, ITextOffsetMap OffsetMap);

public interface ITextProjection
{
    ProjectedText Project(in TextProjectionContext context);
}

/// <summary>Removes complete logical lines from the visual text surface.</summary>
public interface ITextLineCollapser
{
    bool IsCollapsed(LogicalTextLine line);
}

public sealed class TextViewExtensionPipeline
{
    public long Revision { get; set; }
    /// <summary>Run in registration order; where paint spans overlap, the later registration wins.</summary>
    public IList<ITextClassifier> Classifiers { get; } = new List<ITextClassifier>();
    public IList<ITextLineTransformer> Transformers { get; } = new List<ITextLineTransformer>();
    public IList<ITextElementGenerator> ElementGenerators { get; } = new List<ITextElementGenerator>();
    public IList<ITextProjection> Projections { get; } = new List<ITextProjection>();
    public IList<ITextLineCollapser> LineCollapsers { get; } = new List<ITextLineCollapser>();
}
