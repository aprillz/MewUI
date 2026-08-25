using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextViewPipelineContractTests
{
    [TestMethod]
    public void OverlappingPaintSpans_LaterRegistrationWins()
    {
        var early = Color.FromHex("#FF0000");
        var late = Color.FromHex("#0000FF");
        ReadOnlySpan<TextPaintSpan> spans =
        [
            new TextPaintSpan(new TextRange(0, 6), early),
            new TextPaintSpan(new TextRange(3, 6), late),
            new TextPaintSpan(new TextRange(4, 2), Background: Color.FromHex("#00FF00"))
        ];

        Assert.AreEqual(early, ManagedTextRenderContext.GetSpanForeground(spans, 1));
        Assert.AreEqual(late, ManagedTextRenderContext.GetSpanForeground(spans, 4));
        Assert.AreEqual(late, ManagedTextRenderContext.GetSpanForeground(spans, 8));
        Assert.IsNull(ManagedTextRenderContext.GetSpanForeground(spans, 9));
    }

    [TestMethod]
    public void PipelineContexts_ReceiveTheComposedOffsetMap()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var document = new StringTextDocument("0123456789");
        var extensions = new TextViewExtensionPipeline();
        var projection = new SkipPrefixProjection(skipCount: 4);
        var recorder = new OffsetMapRecorder();
        extensions.Projections.Add(projection);
        extensions.Classifiers.Add(recorder);
        using var view = new TextViewLayout(
            factory.TextEngine,
            document,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);

        view.SetViewport(new TextViewport(200, 50));

        Assert.IsNotNull(recorder.ClassifiedMap);
        Assert.AreEqual("456789", recorder.ClassifiedText);
        Assert.AreEqual(4, recorder.ClassifiedMap.MapToSource(0));
        Assert.AreEqual(9, recorder.ClassifiedMap.MapToSource(5));
        Assert.AreEqual(1, recorder.ClassifiedMap.MapFromSource(5));
    }

    /// <summary>Drops the first N characters so projected offsets shift against the source.</summary>
    private sealed class SkipPrefixProjection(int skipCount) : ITextProjection
    {
        public ProjectedText Project(in TextProjectionContext context)
            => new(context.SourceText[skipCount..], new ShiftedOffsetMap(skipCount));
    }

    private sealed class ShiftedOffsetMap(int shift) : ITextOffsetMap
    {
        public int MapToSource(int projectedOffset) => projectedOffset + shift;
        public int MapFromSource(int sourceOffset) => Math.Max(0, sourceOffset - shift);
    }

    private sealed class OffsetMapRecorder : ITextClassifier
    {
        public ITextOffsetMap? ClassifiedMap { get; private set; }
        public string? ClassifiedText { get; private set; }

        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
            ClassifiedMap = context.OffsetMap;
            ClassifiedText = context.Text.ToString();
        }
    }
}
