namespace Aprillz.MewUI.Text;

/// <summary>Contract of a control that presents a text document through the extension pipeline.</summary>
/// <summary>
/// A whole document line's coordinates, answered from the line's own estimate without laying it
/// out. Speaks source offsets and x only: columns belong to a laid-out slice, and a line long
/// enough to be sliced has no columns of its own until one is built.
/// </summary>
/// <remarks>
/// The mapping keeps the width it was built with for as long as the line lives, so a slice laid out
/// later does not move sideways as the view refines its estimates. <see cref="Width"/> can therefore
/// differ from <see cref="ITextViewHost.ExtentWidth"/>. A document change invalidates the extent, so
/// callers ask for it again rather than holding one.
/// </remarks>
public interface ITextLineExtent
{
    /// <summary>Document offset the line starts at.</summary>
    int SourceOffset { get; }

    /// <summary>Length of the line's text, excluding its delimiter.</summary>
    int SourceLength { get; }

    /// <summary>Width of the whole line, equal to <see cref="GetXForOffset"/> at its end.</summary>
    double Width { get; }

    /// <summary>
    /// Whether the mappings are measured rather than estimated, which they are only where every
    /// character of the line advances the same amount.
    /// </summary>
    bool IsExact { get; }

    /// <summary>
    /// Distance from the line's left edge to a line-relative source offset, which is clamped into
    /// the line.
    /// </summary>
    double GetXForOffset(int sourceOffset);

    /// <summary>
    /// Line-relative source offset at a distance from the line's left edge, rounded down and
    /// clamped into the line.
    /// </summary>
    int GetOffsetForX(double x);
}

public interface ITextViewHost
{
    /// <summary>Document whose text the view presents.</summary>
    IReadOnlyTextDocument Document { get; }

    /// <summary>Extension pipeline applied when visible lines are laid out.</summary>
    TextViewExtensionPipeline Extensions { get; }

    /// <summary>Raised after the document content changed or the document was replaced.</summary>
    event Action<ITextViewHost>? DocumentChanged;

    /// <summary>Re-runs registered classifiers, generators, projections, and layers.</summary>
    void InvalidateTextView();

    /// <summary>
    /// Rebuilds only the lines overlapping the document range, leaving every other cached line in
    /// place. Safe to call while lines are being built; the rebuild then runs once that finishes.
    /// </summary>
    void InvalidateTextRange(int offset, int length);

    /// <summary>
    /// Raised before the visible lines are built, carrying the first line number. Extensions that
    /// carry state across lines check it here, before any line is reused.
    /// </summary>
    event Action<ITextViewHost, int>? LineConstructionStarting;

    /// <summary>Raised after the visible lines were built.</summary>
    event Action<ITextViewHost>? LinesChanged;

    /// <summary>Lines currently laid out, in document order, with their document-space positions.</summary>
    IReadOnlyList<TextLineLayout> VisibleTextLines { get; }

    /// <summary>
    /// The laid-out line holding the offset, laying it out when it is outside the viewport. Null
    /// when there is no line to lay out. A line long enough to be cut into slices answers with the
    /// slice the offset falls in. The layout follows the current viewport, so a wrapping view
    /// answers meaningfully only once it has been given a width.
    /// </summary>
    TextLineLayout? GetLineLayout(int documentOffset);

    /// <summary>
    /// Coordinates of the whole line holding the offset, answered without laying it out. Null where
    /// there is no line, or where the view wraps and so measures a line in rows rather than in x.
    /// </summary>
    ITextLineExtent? GetLineExtent(int documentOffset);

    /// <summary>Area the text is drawn into, excluding chrome.</summary>
    Rect TextViewportBounds { get; }

    /// <summary>Height of the whole document in view coordinates.</summary>
    double ExtentHeight { get; }

    /// <summary>
    /// Width of the widest line laid out so far, in view coordinates. Lines that have not been laid
    /// out yet count as empty, so the value grows as more of the document is reached.
    /// </summary>
    double ExtentWidth { get; }

    /// <summary>Height of a line holding one character in the view's own style, independent of content.</summary>
    double DefaultLineHeight { get; }

    /// <summary>Baseline of a line holding one character in the view's own style.</summary>
    double DefaultBaseline { get; }

    /// <summary>Line number whose row contains the document-space <paramref name="documentY"/>.</summary>
    int FindLineByY(double documentY);

    /// <summary>Document-space top of <paramref name="lineNumber"/>.</summary>
    double GetLineY(int lineNumber);

    /// <summary>Scroll offset of the view in document coordinates.</summary>
    Point ScrollOffset { get; }

    /// <summary>Raised after <see cref="ScrollOffset"/> changed.</summary>
    event Action<ITextViewHost>? ScrollOffsetChanged;

    /// <summary>
    /// Scrolls the smallest amount that brings the document-space rectangle into view. A rectangle
    /// taller or wider than the viewport is centred on that axis instead.
    /// </summary>
    void MakeVisible(Rect documentRect);

    /// <summary>Draw order of the view. Replacing a built-in anchor hands its painting to the caller.</summary>
    TextViewLayerStack Layers { get; }

    /// <summary>Inserts a layer relative to a built-in anchor.</summary>
    void InsertLayer(ITextViewLayer layer, TextViewLayerAnchor anchor, TextLayerPosition position);

    /// <summary>
    /// Discards what the anchor group drew and repaints it, leaving every line layout in place. A
    /// host that does not cache rendered layers may repaint the whole stack instead.
    /// </summary>
    void InvalidateLayer(TextViewLayerAnchor anchor);
}
