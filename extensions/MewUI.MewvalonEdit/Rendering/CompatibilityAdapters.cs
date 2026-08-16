using System.Collections;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>List that reports mutations so the view can repaint.</summary>
internal sealed class ExtensionList<T>(Action onChanged, TextEditor? editor = null) : IList<T>
{
    private readonly List<T> _items = [];

    public T this[int index]
    {
        get => _items[index];
        set { Disconnect(_items[index]); _items[index] = value; Connect(value); onChanged(); }
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public void Add(T item) { _items.Add(item); Connect(item); onChanged(); }
    public void Insert(int index, T item) { _items.Insert(index, item); Connect(item); onChanged(); }

    public bool Remove(T item)
    {
        bool removed = _items.Remove(item);
        if (removed)
        {
            Disconnect(item);
            onChanged();
        }
        return removed;
    }

    public void RemoveAt(int index) { var item = _items[index]; _items.RemoveAt(index); Disconnect(item); onChanged(); }

    public void Clear()
    {
        foreach (var item in _items)
        {
            Disconnect(item);
        }
        _items.Clear();
        onChanged();
    }

    // An item that needs the view learns of it here rather than being handed it on construction,
    // so a consumer can hold one item across several views. The view is read at the time an item
    // arrives, because the lists are built while the editor still is.
    private void Connect(T item)
    {
        if (editor is not null && item is ITextViewConnect connect)
        {
            connect.AddToTextView(editor.TextArea.TextView);
        }
    }

    private void Disconnect(T item)
    {
        if (editor is not null && item is ITextViewConnect connect)
        {
            connect.RemoveFromTextView(editor.TextArea.TextView);
        }
    }

    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public int IndexOf(T item) => _items.IndexOf(item);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Runs the registered <see cref="IVisualLineTransformer"/>s and translates their element overrides
/// into engine paint spans and geometry runs. Registered as both a classifier and a transformer
/// because colors and fonts travel through different pipeline stages; the per-line result is
/// computed once and shared between the two calls.
/// </summary>
internal sealed class LineTransformerAdapter(TextEditor editor) : ITextClassifier, ITextLineTransformer
{
    private readonly List<VisualLineElement> _elements = [];
    private readonly RunConstructionContext _context = new(editor);
    private long _cachedVersion = -1;
    private int _cachedOffset = -1;
    private int _cachedLength = -1;

    public IList<IVisualLineTransformer> Transformers { get; } =
        new ExtensionList<IVisualLineTransformer>(editor.InvalidateTextView, editor);

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        EnsureComputed(context.LogicalLine);
        foreach (var element in _elements)
        {
            var properties = element.TextRunProperties;
            var foreground = element.Foreground ?? properties.ForegroundBrush;
            var background = element.BackgroundBrush ?? properties.BackgroundBrush;
            if (!foreground.HasValue && !background.HasValue && properties.TextDecorations == TextDecoration.None)
            {
                continue;
            }
            (int start, int length) = Project(element, context.OffsetMap);
            if (length <= 0)
            {
                continue;
            }
            output.Add(new TextPaintSpan(
                new TextRange(start, length),
                foreground,
                background,
                properties.TextDecorations));
        }
    }

    /// <summary>
    /// The element's document range as columns of the laid-out text. A transformer names a document
    /// range, and an element standing more columns in for the text it covers moves the two apart.
    /// </summary>
    private static (int Start, int Length) Project(VisualLineElement element, ITextOffsetMap offsetMap)
    {
        int start = offsetMap.MapFromSource(element.RelativeTextOffset);
        int end = offsetMap.MapFromSource(element.RelativeTextOffset + element.DocumentLength);
        return (start, end - start);
    }

    public void Transform(
        in TextLineTransformContext context,
        IList<GeometryStyleRun> geometryRuns,
        IList<InlineRun> inlines)
    {
        EnsureComputed(context.LogicalLine);
        foreach (var element in _elements)
        {
            var properties = element.TextRunProperties;
            if (!properties.HasFont)
            {
                continue;
            }
            var style = context.DefaultStyle with
            {
                FontFamily = properties.FontFamily ?? context.DefaultStyle.FontFamily,
                FontSize = properties.FontRenderingEmSize ?? context.DefaultStyle.FontSize,
                Weight = properties.FontWeight ?? context.DefaultStyle.Weight,
                Italic = properties.Italic ?? context.DefaultStyle.Italic
            };
            (int start, int length) = Project(element, context.OffsetMap);
            if (length > 0)
            {
                geometryRuns.Add(new GeometryStyleRun(start, length, style));
            }
        }
    }

    private void EnsureComputed(in LogicalTextLine logical)
    {
        long version = editor.Document.CoreDocument.Version;
        if (_cachedVersion == version && _cachedOffset == logical.Offset && _cachedLength == logical.Length)
        {
            return;
        }
        _cachedVersion = version;
        _cachedOffset = logical.Offset;
        _cachedLength = logical.Length;
        _elements.Clear();
        if (Transformers.Count == 0)
        {
            return;
        }

        _context.CurrentDocumentLine = editor.Document.GetLineByOffset(logical.Offset);
        foreach (var transformer in Transformers)
        {
            transformer.Transform(_context, _elements);
        }
    }

}

/// <summary>
/// Runs the registered <see cref="VisualLineElementGenerator"/>s over a line, following AvalonEdit's
/// scan protocol. One cached scan per line serves three consumers: the projection stage replaces the
/// document text of elements whose visual and document lengths differ, the generation stage turns
/// every element into an engine inline run at its projected position, and input routing looks up
/// the element under a document offset.
/// </summary>
internal sealed class ElementGeneratorAdapter(TextEditor editor)
    : ITextElementGenerator, ITextProjection, ITextClassifier
{
    private readonly RunConstructionContext _context = new(editor);
    private readonly Dictionary<int, CachedScan> _scans = [];
    private readonly Dictionary<VisualLineElementGenerator, int> _interests = [];
    private long _scanVersion = -1;
    private int _scanGeneration;
    private int _cachedGeneration;
    private ExtensionList<VisualLineElementGenerator>? _generators;

    public IList<VisualLineElementGenerator> Generators
        => _generators ??= new ExtensionList<VisualLineElementGenerator>(() =>
        {
            InvalidateScans();
            editor.InvalidateTextView();
        }, editor);

    /// <summary>
    /// Drops the scanned elements. The cache keys on the document version, so a change to what the
    /// generators produce, such as an option turning one on, leaves it stale until the next edit.
    /// </summary>
    public void InvalidateScans() => _scanGeneration++;

    public ProjectedText Project(in TextProjectionContext context)
    {
        var identity = new ProjectedText(context.SourceText, IdentityTextOffsetMap.Instance);
        if (Generators.Count == 0)
        {
            return identity;
        }

        var scan = EnsureScanned(context.LogicalLine);
        List<ReplacementProjection.Replacement>? replacements = null;
        foreach (var element in scan.Elements)
        {
            if (element.VisualLength != element.DocumentLength)
            {
                replacements ??= new List<ReplacementProjection.Replacement>(scan.Elements.Count);
                replacements.Add(new ReplacementProjection.Replacement(
                    element.RelativeTextOffset, element.DocumentLength, element.GetVisualText()));
            }
        }

        var projected = replacements is null
            ? identity
            : ReplacementProjection.Build(context.SourceText, replacements);
        // The columns are only knowable once the projection is: an element that stands more columns
        // in for its text pushes every element after it along.
        foreach (var element in scan.Elements)
        {
            element.VisualColumn = projected.OffsetMap.MapFromSource(element.RelativeTextOffset);
        }
        return projected;
    }

    /// <inheritdoc/>
    public int GetFirstInterestedOffset(in TextElementScanContext context, int startOffset)
    {
        if (Generators.Count == 0)
        {
            return -1;
        }
        // The walk restarts at the line start, and the elements it produces replace whatever the
        // previous walk of this line recorded.
        if (startOffset == context.ScanStartOffset)
        {
            BeginLine(context.ScanStartOffset);
        }

        int best = -1;
        _interests.Clear();
        WithGenerators(context.ScanStartOffset, generator =>
        {
            int interested = generator.GetFirstInterestedOffset(startOffset);
            _interests[generator] = interested;
            if (interested >= startOffset && (best < 0 || interested < best))
            {
                best = interested;
            }
        });
        return best;
    }

    /// <inheritdoc/>
    public GeneratedTextElement? ConstructElement(in TextElementScanContext context, int offset)
    {
        if (Generators.Count == 0)
        {
            return null;
        }

        VisualLineElement? built = null;
        WithGenerators(context.ScanStartOffset, generator =>
        {
            // The interest from the preceding query, as the original caches it. Asking again would
            // let a generator that counts the calls see the same offset twice.
            if (built is not null || !_interests.TryGetValue(generator, out int interested) || interested != offset)
            {
                return;
            }
            built = generator.ConstructElement(offset);
        });
        if (built is null)
        {
            return null;
        }

        built.RelativeTextOffset = offset - context.ScanStartOffset;
        Record(context.ScanStartOffset, built);
        // Only the columns the element paints become the object; the rest of its visual text is laid
        // out normally, which is how a tab marker paints a glyph and still reaches its tab stop.
        return new GeneratedTextElement(
            built.DocumentLength,
            built.ReplacesText ? built.PaintedVisualLength : 0,
            built.ReplacesText ? new ElementInline(editor, built) : null,
            built.BreaksLine);
    }

    private void BeginLine(int lineStart)
    {
        ResetScansIfStale();
        _scans[lineStart] = new CachedScan(LineLength(lineStart), []);
    }

    private void Record(int lineStart, VisualLineElement element)
    {
        if (!_scans.TryGetValue(lineStart, out var scan))
        {
            scan = new CachedScan(LineLength(lineStart), []);
        }
        scan.Elements.Add(element);
        // An element standing in for a folded range reaches past the line the scan started on, and
        // the offset lookup has to keep covering it.
        int end = element.RelativeTextOffset + element.DocumentLength;
        _scans[lineStart] = scan.Length >= end ? scan : new CachedScan(end, scan.Elements);
    }

    private int LineLength(int lineStart) => editor.Document.GetLineByOffset(lineStart).Length;

    private void WithGenerators(int lineStart, Action<VisualLineElementGenerator> action)
    {
        _context.CurrentDocumentLine = editor.Document.GetLineByOffset(lineStart);
        foreach (var generator in Generators)
        {
            generator.StartGeneration(_context);
        }
        try
        {
            foreach (var generator in Generators)
            {
                action(generator);
            }
        }
        finally
        {
            foreach (var generator in Generators)
            {
                generator.FinishGeneration();
            }
        }
    }

    /// <summary>Paints the elements that only decorate their range.</summary>
    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        if (Generators.Count == 0)
        {
            return;
        }

        // Not inline runs: one run is one cluster, which would remove every caret position inside.
        var scan = EnsureScanned(context.LogicalLine);
        foreach (var element in scan.Elements)
        {
            if (element.ReplacesText)
            {
                continue;
            }
            element.PrepareForPaint(editor.TextArea.TextView);
            var properties = element.TextRunProperties;
            var foreground = element.Foreground ?? properties.ForegroundBrush;
            var background = element.BackgroundBrush ?? properties.BackgroundBrush;
            if (!foreground.HasValue && !background.HasValue && properties.TextDecorations == TextDecoration.None)
            {
                continue;
            }
            int start = context.OffsetMap.MapFromSource(element.RelativeTextOffset);
            int end = context.OffsetMap.MapFromSource(element.RelativeTextOffset + element.DocumentLength);
            if (end > start)
            {
                output.Add(new TextPaintSpan(
                    new TextRange(start, end - start), foreground, background, properties.TextDecorations));
            }
        }
    }

    /// <summary>Elements of an already scanned line, keyed by its laid-out start offset.</summary>
    public IReadOnlyList<VisualLineElement> GetScannedElements(int lineOffset)
        => editor.Document.CoreDocument.Version == _scanVersion && _scans.TryGetValue(lineOffset, out var scan)
            ? scan.Elements
            : Array.Empty<VisualLineElement>();

    /// <summary>Element covering the document offset on an already scanned line, if any.</summary>
    public VisualLineElement? FindElementAt(int documentOffset)
    {
        if (editor.Document.CoreDocument.Version != _scanVersion)
        {
            return null;
        }
        foreach ((int lineStart, var scan) in _scans)
        {
            if (documentOffset < lineStart || documentOffset >= lineStart + scan.Length)
            {
                continue;
            }
            int relative = documentOffset - lineStart;
            foreach (var element in scan.Elements)
            {
                if (relative >= element.RelativeTextOffset &&
                    relative < element.RelativeTextOffset + Math.Max(1, element.DocumentLength))
                {
                    return element;
                }
            }
            return null;
        }
        return null;
    }

    /// <summary>
    /// Elements the core walk recorded for this line. The walk runs before the text is read, so the
    /// list is already there by the time the projection and classification stages ask for it.
    /// </summary>
    private CachedScan EnsureScanned(in LogicalTextLine logical)
    {
        ResetScansIfStale();
        if (_scans.TryGetValue(logical.Offset, out var cached))
        {
            return cached;
        }

        // The core walk normally fills this before the projection and classification stages run.
        // A caller that reaches these stages on its own still gets the elements.
        var context = new TextElementScanContext(editor.Document.CoreDocument, logical.Offset);
        int end = logical.Offset + logical.Length;
        // The line end is asked about too: an element may stand there rather than over a character,
        // which is where the end-of-line marker lives.
        for (int offset = logical.Offset; offset <= end;)
        {
            int interested = GetFirstInterestedOffset(in context, offset);
            if (interested < offset || interested > end)
            {
                break;
            }
            if (ConstructElement(in context, interested) is not { } element)
            {
                offset = interested + 1;
                continue;
            }
            offset = interested + Math.Max(1, element.DocumentLength);
        }
        return _scans.TryGetValue(logical.Offset, out var filled) ? filled : new CachedScan(0, []);
    }

    private void ResetScansIfStale()
    {
        long version = editor.Document.CoreDocument.Version;
        if (version != _scanVersion || _scanGeneration != _cachedGeneration)
        {
            _scans.Clear();
            _scanVersion = version;
            _cachedGeneration = _scanGeneration;
        }
    }

    private readonly record struct CachedScan(int Length, List<VisualLineElement> Elements);

    // Reads the density on each call rather than storing it on the element, so a DPI change needs
    // no rescan: the scan cache only invalidates on a document change.
    private sealed class ElementInline(TextEditor editor, VisualLineElement element) : IInlineTextObject
    {
        public InlineMetrics Measure() => element.Measure(editor.EditorDpi);

        public void Draw(ITextRenderContext context, Point origin)
        {
            // Every paint, because the scan cache outlives a theme change: an element that took its
            // color from the theme when it was built would keep the old one.
            element.PrepareForPaint(editor.TextArea.TextView);
            element.Draw(context, origin, editor.EditorDpi);
        }
    }

}

/// <summary>
/// What a transformer or generator is told about the line it is running over. Reads through to the
/// editor on each access so a font or document change needs no rebuild of the context itself.
/// </summary>
internal sealed class RunConstructionContext(TextEditor editor) : ITextRunConstructionContext
{
    public TextDocument Document => editor.Document;
    public TextView TextView => editor.TextArea.TextView;
    public DocumentLine CurrentDocumentLine { get; set; } = null!;
    public TextRunStyle DefaultStyle => new(editor.FontFamily, editor.FontSize, editor.FontWeight);
}

/// <summary>
/// Holds the editor's background renderers and draws them once per frame at each known layer, so a
/// renderer computes geometry for the whole viewport exactly once as it does in AvalonEdit.
/// </summary>
internal sealed class BackgroundRendererRegistry(TextEditor editor)
{
    public IList<IBackgroundRenderer> Renderers { get; } =
        new ExtensionList<IBackgroundRenderer>(editor.InvalidateTextView, editor);

    /// <summary>Inserts one layer under each known anchor; each draws the renderers assigned to it.</summary>
    public void RegisterInto(ITextViewHost host)
    {
        foreach (var layer in Enum.GetValues<KnownLayer>())
        {
            // Below the anchor, because an AvalonEdit background renderer paints under the content
            // of the layer it names.
            host.InsertLayer(
                new LayerBridge(editor, layer, Renderers),
                TextView.ToAnchor(layer),
                TextLayerPosition.Below);
        }
    }

    private sealed class LayerBridge(TextEditor editor, KnownLayer layer, IList<IBackgroundRenderer> renderers)
        : ITextViewLayer
    {
        public void Draw(ITextRenderContext context, Rect viewportBounds)
        {
            foreach (var renderer in renderers)
            {
                if (renderer.Layer == layer)
                {
                    renderer.Draw(editor.TextArea.TextView, context.Graphics);
                }
            }
        }
    }
}
