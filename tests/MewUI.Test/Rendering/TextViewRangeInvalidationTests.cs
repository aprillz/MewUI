using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Range invalidation rebuilds the lines it names and leaves the rest cached, and survives being
/// called from a classifier while the lines it would rebuild are still being built.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextViewRangeInvalidationTests
{
    private const string DOCUMENT = "line zero\nline one\nline two\nline three\nline four";

    [TestMethod]
    public void RangeInvalidationReclassifiesOnlyTheLinesItNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var classified = new List<int>();
        using var host = CreateView(new RecordingClassifier(classified), out var document);
        classified.Clear();

        var secondLine = document.GetLineByNumber(2);
        host.InvalidateRange(secondLine.Offset, secondLine.Length);

        CollectionAssert.AreEqual(new[] { 2 }, classified,
            $"Lines reclassified: {string.Join(',', classified)}.");
    }

    [TestMethod]
    public void InvalidatingFromInsideLineConstructionRunsAfterIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var classified = new List<int>();
        TextViewLayout? view = null;
        var reentrant = new ReentrantClassifier(classified, () => view);
        using var host = CreateView(reentrant, out var document);
        view = host;

        classified.Clear();
        reentrant.TargetLine = 4;
        var third = document.GetLineByNumber(3);

        // The classifier invalidates line 4 while line 3 is being built; without the deferral the
        // rebuild would run inside the loop that is still filling the materialized list.
        host.InvalidateRange(third.Offset, third.Length);

        Assert.Contains(4, classified, $"Lines reclassified: {string.Join(',', classified)}.");
        Assert.IsGreaterThan(0, host.MaterializedLines.Count, "The view lost its materialized lines.");
    }

    [TestMethod]
    public void ConstructionEventsBracketTheLineLoop()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var order = new List<string>();
        using var host = CreateView(new RecordingClassifier([]), out _);
        host.LineConstructionStarting += (_, first) => order.Add($"start:{first}");
        host.LinesChanged += _ => order.Add("changed");

        host.InvalidateRange(0, 1);

        Assert.AreEqual("start:0,changed", string.Join(',', order));
    }

    private static TextViewLayout CreateView(ITextClassifier classifier, out IReadOnlyTextDocument document)
    {
        var extensions = new TextViewExtensionPipeline();
        extensions.Classifiers.Add(classifier);
        var source = new StringTextDocument(DOCUMENT);
        document = source;
        var factory = new GdiGraphicsFactory();
        var view = new TextViewLayout(
            factory.TextEngine,
            source,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(400, 200));
        return view;
    }

    private sealed class RecordingClassifier(List<int> classified) : ITextClassifier
    {
        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
            => classified.Add(context.LogicalLine.LineNumber);
    }

    private sealed class ReentrantClassifier(List<int> classified, Func<TextViewLayout?> view) : ITextClassifier
    {
        public int TargetLine { get; set; } = -1;

        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
            classified.Add(context.LogicalLine.LineNumber);
            if (TargetLine < 0 || context.LogicalLine.LineNumber != TargetLine - 1)
            {
                return;
            }

            int target = TargetLine;
            TargetLine = -1;
            view()?.InvalidateRange(context.LogicalLine.Offset + context.LogicalLine.Length + 1, 1);
            _ = target;
        }
    }
}
