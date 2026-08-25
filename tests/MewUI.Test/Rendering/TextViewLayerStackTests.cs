using Aprillz.MewUI;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Anchors are positions, not slots that own their content: the host's own drawing is an ordinary
/// entry, a layer inserted against one draws beside it, and replacing one drops the host's entry.
/// </summary>
[TestClass]
public sealed class TextViewLayerStackTests
{
    private static TextViewLayerStack CreateStack(List<string> order)
        => new(anchor => new RecordingLayer(anchor.ToString(), order));

    private static string Draw(TextViewLayerStack stack, List<string> order)
    {
        stack.Draw(NullRenderContext.Instance, default);
        return string.Join(',', order);
    }

    [TestMethod]
    public void BuiltInAnchorsDrawInOrder()
    {
        var order = new List<string>();
        var stack = CreateStack(order);

        Assert.AreEqual("Background,Selection,Text,Caret", Draw(stack, order));
    }

    [TestMethod]
    public void InsertedLayersSitBesideTheirAnchor()
    {
        var order = new List<string>();
        var stack = CreateStack(order);
        stack.Insert(new RecordingLayer("under", order), TextViewLayerAnchor.Text, TextLayerPosition.Below);
        stack.Insert(new RecordingLayer("over", order), TextViewLayerAnchor.Text, TextLayerPosition.Above);

        Assert.AreEqual("Background,Selection,under,Text,over,Caret", Draw(stack, order));
    }

    [TestMethod]
    public void ReplacingAnAnchorTakesOverItsDrawing()
    {
        var order = new List<string>();
        var stack = CreateStack(order);
        stack.Insert(new RecordingLayer("mine", order), TextViewLayerAnchor.Selection, TextLayerPosition.Replace);

        // The host's own selection pass is gone, which is what makes replacement meaningful.
        Assert.AreEqual("Background,mine,Text,Caret", Draw(stack, order));
    }

    [TestMethod]
    public void AReplacedAnchorStillAcceptsNeighbours()
    {
        var order = new List<string>();
        var stack = CreateStack(order);
        stack.Insert(new RecordingLayer("mine", order), TextViewLayerAnchor.Selection, TextLayerPosition.Replace);
        stack.Insert(new RecordingLayer("under", order), TextViewLayerAnchor.Selection, TextLayerPosition.Below);

        Assert.AreEqual("Background,under,mine,Text,Caret", Draw(stack, order));
    }

    [TestMethod]
    public void LayersListCarriesTheBuiltInsAndTheInsertions()
    {
        var order = new List<string>();
        var stack = CreateStack(order);
        stack.Insert(new RecordingLayer("extra", order), TextViewLayerAnchor.Text, TextLayerPosition.Above);

        Assert.HasCount(5, stack.Layers);
    }

    private sealed class RecordingLayer(string name, List<string> order) : ITextViewLayer
    {
        public void Draw(ITextRenderContext context, Rect viewportBounds) => order.Add(name);
    }

    /// <summary>The stack only forwards the context, so the layers under test never touch it.</summary>
    private sealed class NullRenderContext : ITextRenderContext
    {
        public static NullRenderContext Instance { get; } = new();

        public Aprillz.MewUI.Rendering.IGraphicsContext Graphics => throw new NotSupportedException();

        public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options) { }

        public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options) { }

        public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options) { }
    }
}
