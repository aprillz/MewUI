using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;
using System.Diagnostics;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextViewLayoutTests
{
    [TestMethod]
    public void SetViewport_MaterializesOnlyVisibleLogicalLines()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        string text = string.Join('\n', Enumerable.Range(0, 1000).Select(i => $"line {i}"));
        var document = new TestReadOnlyDocument(text);
        using var factory = new GdiGraphicsFactory();
        using var view = CreateView(factory, document, width: 240);

        view.SetViewport(new TextViewport(240, 100));

        Assert.IsNotEmpty(view.MaterializedLines);
        Assert.IsLessThan(20, view.MaterializedLines.Count);
        Assert.AreEqual(0, view.MaterializedLines[0].LogicalLine.LineNumber);

        view.SetViewport(new TextViewport(240, 100, VerticalOffset: view.ExtentHeight * 0.5));

        Assert.IsNotEmpty(view.MaterializedLines);
        Assert.IsLessThan(20, view.MaterializedLines.Count);
        Assert.IsGreaterThan(0, view.MaterializedLines[0].LogicalLine.LineNumber);

        Assert.IsGreaterThanOrEqualTo(1, factory.TextEngine.ManagedCache.Count);
        view.Dispose();
        Assert.AreEqual(0, factory.TextEngine.ManagedCache.Count);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void RepeatedBottomViewportLookupDoesNotScanEveryLogicalLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        string text = string.Join('\n', Enumerable.Range(0, 100_000).Select(static index => $"line {index}"));
        var document = new EditableTextDocument(text);
        using var factory = new GdiGraphicsFactory();
        using var view = CreateView(factory, document, width: 240);
        double bottom = Math.Max(0, view.ExtentHeight - 100);
        view.SetViewport(new TextViewport(240, 100, VerticalOffset: bottom));

        var stopwatch = Stopwatch.StartNew();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            view.SetViewport(new TextViewport(240, 100, VerticalOffset: bottom));
        }

        Assert.IsLessThan(500L, stopwatch.ElapsedMilliseconds,
            $"Repeated viewport lookup took {stopwatch.ElapsedMilliseconds}ms, indicating linear line scans.");
        Assert.IsGreaterThan(99_000, view.MaterializedLines[0].LogicalLine.LineNumber);
    }

    [TestMethod]
    public void LogicalLine_WrapsIntoVisualLines_AndHitMapsToDocumentOffset()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("abcdefghijklmno\nsecond");
        using var factory = new GdiGraphicsFactory();
        using var view = CreateView(factory, document, width: 45);
        view.SetViewport(new TextViewport(45, 200));

        var first = view.MaterializedLines.Single(line => line.LogicalLine.LineNumber == 0);
        Assert.IsGreaterThanOrEqualTo(2, first.VisualLines.Count);

        var caret = first.GetCaretBounds(new CharacterHit(3, 0));
        var hit = view.HitTest(new Point(caret.X, first.VisualLines[0].Bounds.Y + caret.Height * 0.5));

        Assert.AreEqual(0, hit.LineNumber);
        Assert.AreEqual(3, hit.DocumentOffset);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void TenMegabyteWrappedLogicalLine_MaterializesOnlyViewportSlice()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument(new string('x', 10_000_000));
        using var factory = new GdiGraphicsFactory();
        using var view = CreateView(factory, document, width: 320);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        view.SetViewport(new TextViewport(320, 80));
        long initialMilliseconds = stopwatch.ElapsedMilliseconds;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.HasCount(1, view.MaterializedLines);
        Assert.IsLessThan(64 * 1024, view.MaterializedLines[0].LogicalLine.Length,
            "The wrapped view materialized the complete logical line instead of a viewport slice.");
        Assert.IsLessThan(64 * 1024, document.MaxRequestedLength,
            "The view requested the complete 10MB logical line from the document.");
        Assert.IsGreaterThan(80, view.ExtentHeight);
        Assert.IsLessThan(750L, initialMilliseconds,
            $"10MB wrapped viewport initialization regressed to {initialMilliseconds}ms.");
        Assert.IsLessThan(64L * 1024 * 1024, allocatedBytes,
            $"10MB wrapped viewport allocated {allocatedBytes:N0} bytes, indicating whole-line materialization.");

        double middleY = view.ExtentHeight * 0.5;
        view.SetViewport(new TextViewport(320, 80, VerticalOffset: middleY));
        var middle = view.MaterializedLines[0];
        Assert.IsGreaterThan(1_000_000, middle.LogicalLine.Offset,
            "Scrolling did not move the materialized slice into the logical line.");
        var hit = view.HitTest(new Point(20, 40));
        Assert.IsGreaterThan(1_000_000, hit.DocumentOffset);

        stopwatch.Restart();
        Rect endCaret = view.GetCaretBounds(document.TextLength);
        Assert.IsLessThan(100L, stopwatch.ElapsedMilliseconds,
            "End-caret lookup scanned the complete wrapped logical line.");
        Assert.IsGreaterThan(middleY, endCaret.Y);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void TenMegabyteNoWrapLogicalLine_MaterializesOnlyHorizontalViewportSlice()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument(new string('x', 10_000_000));
        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap });

        view.SetViewport(new TextViewport(320, 80));

        Assert.HasCount(1, view.MaterializedLines);
        Assert.IsLessThan(4 * 1024, view.MaterializedLines[0].LogicalLine.Length,
            "The no-wrap view materialized the complete logical line instead of a horizontal slice.");
        Assert.IsLessThan(64 * 1024, document.MaxRequestedLength,
            "The no-wrap view requested the complete 10MB logical line from the document.");
        Assert.IsGreaterThan(320, view.ExtentWidth);

        double middleX = view.ExtentWidth * 0.5;
        view.SetViewport(new TextViewport(320, 80, HorizontalOffset: middleX));
        var middle = view.MaterializedLines[0];
        Assert.IsGreaterThan(1_000_000, middle.LogicalLine.Offset,
            "Horizontal scrolling did not move the materialized slice into the logical line.");
        Assert.IsLessThan(4 * 1024, middle.LogicalLine.Length);
        var hit = view.HitTest(new Point(20, 20));
        Assert.IsGreaterThan(1_000_000, hit.DocumentOffset);

        Rect endCaret = view.GetCaretBounds(document.TextLength);
        Assert.IsGreaterThan(middleX, endCaret.X);
        Assert.IsLessThan(4 * 1024, view.MaterializedLines[0].LogicalLine.Length);
    }

    [TestMethod]
    public void Invalidate_RebuildsChangedLineAndFollowingHeightMap()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("short\ntail");
        using var factory = new GdiGraphicsFactory();
        using var view = CreateView(factory, document, width: 60);
        view.SetViewport(new TextViewport(60, 300));
        int beforeRows = view.MaterializedLines[0].VisualLines.Count;

        document.SetText("a much longer first logical line that wraps\ntail");
        view.Invalidate(new TextChange(0, 5, 41));

        Assert.IsGreaterThan(beforeRows, view.MaterializedLines[0].VisualLines.Count);
        Assert.AreEqual(1, document.Version);
        Assert.AreEqual(document.GetLineByNumber(1).Offset, view.MaterializedLines[1].LogicalLine.Offset);
    }

    [TestMethod]
    public void ExtensionPipeline_ProjectsClassifiesTransformsAndGenerates()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("abcXYZdef");
        var extensions = new TextViewExtensionPipeline { Revision = 1 };
        extensions.Projections.Add(new FoldingProjection());
        extensions.Classifiers.Add(new PrefixClassifier());
        extensions.Transformers.Add(new SuffixTransformer());
        extensions.ElementGenerators.Add(new EllipsisInlineGenerator());

        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(300, 100));

        var line = view.MaterializedLines[0];
        Assert.HasCount(1, line.PaintSpans);

        var projectedCaret = line.GetCaretBounds(new CharacterHit(4, 0));
        var hit = view.HitTest(new Point(projectedCaret.X, projectedCaret.Y + projectedCaret.Height * 0.5));
        Assert.AreEqual(6, hit.DocumentOffset,
            "The projected offset after the folding placeholder must map back to the source suffix.");
    }

    [TestMethod]
    public void ExtensionPipeline_CollapsesCompleteLogicalLinesAndMapsHiddenCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("first\nhidden one\nhidden two\nlast");
        var extensions = new TextViewExtensionPipeline();
        extensions.LineCollapsers.Add(new MiddleLineCollapser());
        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions);

        view.SetViewport(new TextViewport(300, 200));

        CollectionAssert.AreEqual(
            new[] { 0, 3 },
            view.MaterializedLines.Select(line => line.LogicalLine.LineNumber).ToArray());
        Rect hiddenCaret = view.GetCaretBounds(document.GetLineByNumber(2).Offset + 2);
        Rect firstLineEnd = view.GetCaretBounds(document.GetLineByNumber(0).Offset + document.GetLineByNumber(0).Length);
        Assert.AreEqual(firstLineEnd.Y, hiddenCaret.Y, 0.01);
    }

    private static TextViewLayout CreateView(GdiGraphicsFactory factory, IReadOnlyTextDocument document, double width)
        => new(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { MaxWidth = width, Wrapping = TextWrapping.Wrap },
            dpi: 96);

    /// <summary>
    /// A visual line is not bound one to one to a logical line: an element standing in for a range
    /// that reaches past its line makes the line's source reach that far too, which is the reverse
    /// of wrapping.
    /// </summary>
    [TestMethod]
    public void AnElementReachingPastItsLineExtendsTheLineSource()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("first\nsecond");
        var extensions = new TextViewExtensionPipeline { Revision = 1 };
        // Covers "rst\nsec": from offset 2 into the middle of the second line.
        extensions.ElementGenerators.Add(new SpanningElementGenerator(startOffset: 2, documentLength: 7));

        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(300, 100));

        var first = view.MaterializedLines[0].LogicalLine;
        Assert.AreEqual(0, first.Offset);
        Assert.AreEqual(document.TextLength, first.Length,
            "The line's source reaches the end of the line the element lands in.");
    }

    /// <summary>
    /// A caret offset inside the range a spanning element swallowed resolves against the line that
    /// covers it, which needs the swallowed line collapsed so it is not laid out a second time.
    /// </summary>
    [TestMethod]
    public void ASwallowedLineResolvesAgainstTheLineCoveringIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("first\nsecond");
        var extensions = new TextViewExtensionPipeline { Revision = 1 };
        extensions.ElementGenerators.Add(new SpanningElementGenerator(startOffset: 2, documentLength: 7));
        extensions.LineCollapsers.Add(new CollapseLine(1));

        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(300, 100));

        Assert.HasCount(1, view.MaterializedLines);
        var bounds = view.GetCaretBounds(document.TextLength);
        Assert.IsGreaterThan(0, bounds.Width + bounds.Height,
            "An offset on the swallowed line still resolves to a caret rectangle.");
    }

    /// <summary>
    /// The range a line reaches is not the range that identifies its cached layout, so a line the
    /// scan extended still answers from the cache instead of being rebuilt on every pass.
    /// </summary>
    [TestMethod]
    public void AnExtendedLineKeepsItsCachedLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("first\nsecond");
        var extensions = new TextViewExtensionPipeline { Revision = 1 };
        var generator = new SpanningElementGenerator(startOffset: 2, documentLength: 7);
        extensions.ElementGenerators.Add(generator);
        extensions.LineCollapsers.Add(new CollapseLine(1));

        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(300, 100));

        int afterFirstPass = generator.Constructions;
        view.SetViewport(new TextViewport(300, 100));
        view.GetLineLayout(0);

        Assert.AreEqual(afterFirstPass, generator.Constructions,
            "A line whose element reached past its end was rebuilt again.");
    }

    /// <summary>
    /// Invalidating a line another line swallowed reaches the line that draws it, which is the only
    /// one that will be laid out again.
    /// </summary>
    [TestMethod]
    public void InvalidatingASwallowedLineRebuildsTheLineCoveringIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var document = new TestReadOnlyDocument("first\nsecond");
        var extensions = new TextViewExtensionPipeline { Revision = 1 };
        var generator = new SpanningElementGenerator(startOffset: 2, documentLength: 7);
        extensions.ElementGenerators.Add(generator);
        extensions.LineCollapsers.Add(new CollapseLine(1));

        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(300, 100));

        int afterFirstPass = generator.Constructions;
        view.InvalidateRange(document.GetLineByNumber(1).Offset, 1);

        Assert.IsGreaterThan(afterFirstPass, generator.Constructions,
            "The covering line kept a layout built from the text that was invalidated.");
    }

    private sealed class CollapseLine(int lineNumber) : ITextLineCollapser
    {
        public bool IsCollapsed(LogicalTextLine line) => line.LineNumber == lineNumber;
    }

    /// <summary>Stands in for one fixed document range, wherever it falls.</summary>
    private sealed class SpanningElementGenerator(int startOffset, int documentLength) : ITextElementGenerator
    {
        public int Constructions { get; private set; }

        public int GetFirstInterestedOffset(in TextElementScanContext context, int scanFrom)
            => scanFrom <= startOffset ? startOffset : -1;

        public GeneratedTextElement? ConstructElement(in TextElementScanContext context, int offset)
        {
            Constructions++;
            return new GeneratedTextElement(documentLength, 1, new FixedInline());
        }
    }

    private sealed class TestReadOnlyDocument : IReadOnlyTextDocument
    {
        private string _text;
        private List<Line> _lines;

        public TestReadOnlyDocument(string text)
        {
            _text = text;
            _lines = ParseLines(text);
        }

        public int TextLength => _text.Length;
        public long Version { get; private set; }
        public int LineCount => _lines.Count;
        public int MaxRequestedLength { get; private set; }

        public char GetCharAt(int offset) => _text[offset];

        public string GetText(int offset, int length)
        {
            MaxRequestedLength = Math.Max(MaxRequestedLength, length);
            return _text.Substring(offset, length);
        }

        public IReadOnlyDocumentLine GetLineByNumber(int lineNumber) => _lines[lineNumber];

        public IReadOnlyDocumentLine GetLineByOffset(int offset)
        {
            offset = Math.Clamp(offset, 0, _text.Length);
            foreach (var line in _lines)
            {
                if (offset < line.Offset + line.TotalLength || line.LineNumber == _lines.Count - 1)
                {
                    return line;
                }
            }
            return _lines[^1];
        }

        public int GetOffset(int line, int column)
        {
            var source = _lines[line];
            return source.Offset + Math.Clamp(column, 0, source.Length);
        }

        public TextLocation GetLocation(int offset)
        {
            var line = (Line)GetLineByOffset(offset);
            return new TextLocation(line.LineNumber, Math.Clamp(offset - line.Offset, 0, line.Length));
        }

        public void SetText(string text)
        {
            _text = text;
            _lines = ParseLines(text);
            Version++;
        }

        private static List<Line> ParseLines(string text)
        {
            var lines = new List<Line>();
            int lineStart = 0;
            int number = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] is not ('\r' or '\n'))
                {
                    continue;
                }
                int delimiterLength = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                lines.Add(new Line(number++, lineStart, i - lineStart, delimiterLength, text.Substring(i, delimiterLength)));
                i += delimiterLength - 1;
                lineStart = i + 1;
            }
            lines.Add(new Line(number, lineStart, text.Length - lineStart, 0, string.Empty));
            return lines;
        }

        private sealed record Line(int LineNumber, int Offset, int Length, int NewLineLength, string Delimiter)
            : IReadOnlyDocumentLine
        {
            public int TotalLength => Length + NewLineLength;
        }
    }

    private sealed class PrefixClassifier : ITextClassifier
    {
        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
            => output.Add(new TextPaintSpan(new TextRange(0, 3), Foreground: Color.Red));
    }

    private sealed class SuffixTransformer : ITextLineTransformer
    {
        public void Transform(
            in TextLineTransformContext context,
            IList<GeometryStyleRun> geometryRuns,
            IList<InlineRun> inlines)
            => geometryRuns.Add(new GeometryStyleRun(4, 3, new TextRunStyle("Segoe UI", 18, FontWeight.Bold)));
    }

    private sealed class EllipsisInlineGenerator : ITextElementGenerator
    {
        public int GetFirstInterestedOffset(in TextElementScanContext context, int startOffset)
            => startOffset <= context.ScanStartOffset + 3 ? context.ScanStartOffset + 3 : -1;

        public GeneratedTextElement? ConstructElement(in TextElementScanContext context, int offset)
            => new GeneratedTextElement(1, 1, new FixedInline());
    }

    private sealed class FixedInline : IInlineTextObject
    {
        public InlineMetrics Measure() => new(20, 16, 12);
        public void Draw(ITextRenderContext context, Point origin) { }
    }

    private sealed class FoldingProjection : ITextProjection
    {
        public ProjectedText Project(in TextProjectionContext context)
            => new("abc?쫉ef".AsMemory(), new FoldingOffsetMap());
    }

    private sealed class FoldingOffsetMap : ITextOffsetMap
    {
        public int MapToSource(int projectedOffset)
            => projectedOffset <= 3 ? projectedOffset : projectedOffset + 2;

        public int MapFromSource(int sourceOffset)
            => sourceOffset <= 3 ? sourceOffset : sourceOffset < 6 ? 3 : sourceOffset - 2;
    }

    private sealed class MiddleLineCollapser : ITextLineCollapser
    {
        public bool IsCollapsed(LogicalTextLine line) => line.LineNumber is 1 or 2;
    }
}
