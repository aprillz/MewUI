using System.Globalization;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

/// <summary>
/// How a layout is retained: keyed by its content, keyed by an owner object that replaces its
/// previous layout, or not retained at all (for text that changes every frame).
/// </summary>
public enum TextLayoutCachePolicy { Content, Owner, None }

public enum TextFidelity { RunWidth, ClusterAdvance, Shaped }

public enum TextFlowDirection { LeftToRight, RightToLeft }

public enum LogicalDirection { Backward, Forward }

public enum VisualDirection { Left, Right, Up, Down }

public enum CaretMode { CodeUnit, TextElement }

[Flags]
public enum TextDecoration
{
    None = 0,
    Underline = 1,
    Strikethrough = 2
}

/// <summary>
/// Trims the layout's outer line boxes to font metrics: ink such as accents or descenders may
/// paint outside the reported bounds, and interior line spacing is unaffected.
/// </summary>
public enum LineBoxTrim
{
    /// <summary>No trimming; the line boxes keep their full ascent and descent.</summary>
    None,

    /// <summary>Trims the first line's top to the cap height.</summary>
    Cap,

    /// <summary>Also trims the last line's bottom to the baseline.</summary>
    CapAndBaseline
}

public readonly record struct CharacterHit(int FirstCharacterIndex, int TrailingLength)
{
    public int InsertionIndex => checked(FirstCharacterIndex + TrailingLength);
}

public readonly record struct TextRange(int Start, int Length)
{
    public int End => checked(Start + Length);
}

public readonly record struct TextRunStyle(
    string FontFamily,
    double FontSize,
    FontWeight Weight = FontWeight.Normal,
    bool Italic = false,
    TextDecoration Decoration = TextDecoration.None,
    CultureInfo? Culture = null,
    string? Language = null)
{
    public static TextRunStyle Default { get; } = new("Segoe UI", 12);
}

public sealed record TextParagraphStyle
{
    public double MaxWidth { get; init; } = double.PositiveInfinity;
    public double MaxHeight { get; init; } = double.PositiveInfinity;
    public TextWrapping Wrapping { get; init; } = TextWrapping.NoWrap;
    public TextTrimming Trimming { get; init; } = TextTrimming.None;
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public TextFlowDirection FlowDirection { get; init; } = TextFlowDirection.LeftToRight;
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentUICulture;
    public string? Language { get; init; }
    public IReadOnlyList<double> TabStops { get; init; } = [];

    /// <summary>Tab width in space characters, used where <see cref="TabStops"/> defines no stop ahead.</summary>
    public int TabSize { get; init; } = 4;
    public double? LineHeight { get; init; }

    /// <summary>Extra gap between line boxes (in DIPs); negative tightens, but a line never starts above the previous one.</summary>
    public double LineSpacing { get; init; }
    public double LetterSpacing { get; init; }

    /// <summary>Cap-height trimming of the outer line boxes; see <see cref="Text.LineBoxTrim"/>.</summary>
    public LineBoxTrim LineBoxTrim { get; init; } = LineBoxTrim.None;
}

public readonly record struct GeometryStyleRun(int Start, int Length, TextRunStyle Style)
{
    public int End => checked(Start + Length);
}

public readonly record struct InlineMetrics(double Width, double Height, double Baseline);

public interface IInlineTextObject
{
    InlineMetrics Measure();
    void Draw(ITextRenderContext context, Point origin);
}

/// <summary>
/// An object occupying columns of the laid-out text. <see cref="BreaksLine"/> declares that a line
/// may break after it, which an object standing in for whitespace has to say: the columns it covers
/// are no longer text, so the breaker cannot see the space it replaced.
/// </summary>
public readonly record struct InlineRun(
    int Position,
    int Length,
    IInlineTextObject Object,
    bool BreaksLine = false);

public sealed record TextLayoutRequest
{
    public required ReadOnlyMemory<char> Text { get; init; }
    public uint Dpi { get; init; } = 96;
    public TextParagraphStyle Paragraph { get; init; } = new();
    public TextRunStyle DefaultStyle { get; init; } = TextRunStyle.Default;
    public IReadOnlyList<GeometryStyleRun> Runs { get; init; } = [];
    public IReadOnlyList<InlineRun> Inlines { get; init; } = [];
    public TextFidelity Fidelity { get; init; } = TextFidelity.ClusterAdvance;
    public long Revision { get; init; }
    public bool Transient { get; init; }
}

/// <summary>
/// One laid-out line. The two trailing-whitespace values describe the same run in different units,
/// and both are zero on a line no wrap or break ended.
/// </summary>
public readonly record struct TextLayoutLineMetrics(
    int TextStart,
    int TextLength,
    int NewLineLength,
    Rect Bounds,
    double Baseline,
    double TrailingWhitespaceWidth = 0,
    int TrailingWhitespaceLength = 0)
{
    public int TextEnd => checked(TextStart + TextLength);

    /// <summary>Line width without the whitespace a wrap or a break left at its end.</summary>
    public double VisibleWidth => Math.Max(0, Bounds.Width - TrailingWhitespaceWidth);

    /// <summary>Text length without the whitespace a wrap or a break left at its end.</summary>
    public int VisibleLength => Math.Max(0, TextLength - TrailingWhitespaceLength);
}

public interface ITextLayout
{
    Size MeasuredSize { get; }
    double ContentHeight { get; }
    IReadOnlyList<TextLayoutLineMetrics> Lines { get; }
    CharacterHit HitTestPoint(Point point);
    Rect GetCaretBounds(CharacterHit hit);
    CharacterHit GetNextLogicalCaret(CharacterHit from, LogicalDirection direction, CaretMode mode);
    CharacterHit GetNextVisualCaret(CharacterHit from, VisualDirection direction, CaretMode mode);
    void GetRangeBounds(int start, int length, IList<Rect> output);
}

public interface ITextLayoutCache
{
    int Count { get; }
    void ReleaseOwner(object owner);
    void Trim();
}

public interface ITextEngine
{
    ITextLayout CreateLayout(TextLayoutRequest request);
    ITextLayout GetOrCreateLayout(TextLayoutRequest request, TextLayoutCachePolicy cachePolicy, object? owner = null);
    ITextLayoutCache ManagedCache { get; }
}

public readonly record struct TextPaintSpan(
    TextRange Range,
    Color? Foreground = null,
    Color? Background = null,
    TextDecoration Decoration = TextDecoration.None);

public readonly record struct TextOverlay(TextRange Range, Color Color);

/// <param name="Transient">
/// The text changes every frame: nothing drawn for it is kept in the run or backend text caches,
/// and the backend reuses per-frame scratch textures instead of filling its cache with one-off entries.
/// </param>
public readonly record struct TextDrawOptions(
    Color Foreground,
    ReadOnlyMemory<TextPaintSpan> PaintSpans = default,
    ReadOnlyMemory<TextOverlay> Overlays = default,
    object? Owner = null,
    bool Transient = false);

public interface ITextRenderContext
{
    /// <summary>Surface this context draws into, for layers that paint shapes rather than text.</summary>
    IGraphicsContext Graphics { get; }

    void Draw(ITextLayout layout, Point origin, in TextDrawOptions options);

    /// <summary>Paints only the paint-span backgrounds and overlays, for callers that insert content between them and the glyphs.</summary>
    void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options);

    /// <summary>Paints glyphs and decorations, assuming <see cref="DrawBackground"/> already ran for the same layout.</summary>
    void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options);
}
