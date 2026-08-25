using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class MultiLineTextBoxDocumentTests
{
    private sealed class RecordingClassifier : ITextClassifier
    {
        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
        }
    }

    [TestMethod]
    public void ReplacingTheDocumentKeepsExtensionRegistrations()
    {
        var box = new MultiLineTextBox { Text = "old" };
        var classifier = new RecordingClassifier();
        box.Extensions.Classifiers.Add(classifier);
        var pipeline = box.Extensions;

        box.Document = new EditableTextDocument("new");

        Assert.AreSame(pipeline, box.Extensions);
        Assert.Contains(classifier, box.Extensions.Classifiers);
    }

    [TestMethod]
    public void ReplacingTheDocumentResetsEditingStateAndSyncsText()
    {
        var box = new MultiLineTextBox { Text = "first document" };
        box.Select(0, 5);
        box.ReplaceSelection("x");
        Assert.IsTrue(box.CanUndo);

        box.Document = new EditableTextDocument("second");

        Assert.AreEqual("second", box.Text);
        Assert.AreEqual(0, box.CaretPosition);
        Assert.AreEqual(0, box.SelectionLength);
        Assert.IsFalse(box.CanUndo);
    }

    [TestMethod]
    public void ReplacingTheDocumentRedirectsChangeNotifications()
    {
        var box = new MultiLineTextBox { Text = "old" };
        var oldDocument = box.Document;
        var newDocument = new EditableTextDocument("new");
        int raised = 0;
        ((ITextViewHost)box).DocumentChanged += _ => raised++;

        box.Document = newDocument;
        Assert.AreEqual(1, raised);

        oldDocument.Insert(0, "ignored");
        Assert.AreEqual(1, raised);
        Assert.AreEqual("new", box.Text);

        newDocument.Insert(0, "a");
        Assert.AreEqual(2, raised);
        Assert.AreEqual("anew", box.Text);
    }

    [TestMethod]
    public void AssigningTheSameDocumentIsANoOp()
    {
        var box = new MultiLineTextBox { Text = "keep" };
        box.Select(1, 2);
        int raised = 0;
        ((ITextViewHost)box).DocumentChanged += _ => raised++;

        box.Document = box.Document;

        Assert.AreEqual(0, raised);
        Assert.AreEqual(2, box.SelectionLength);
    }
}
