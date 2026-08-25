using Aprillz.MewUI.Text.Editing;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class LineEndingTests
{
    [TestMethod]
    public void NormalizingDocumentRewritesEveryTerminator()
    {
        var document = new EditableTextDocument("a\r\nb\rc\nd");

        Assert.AreEqual("a\nb\nc\nd", document.ToString());
        Assert.AreEqual(1, document.GetLineByNumber(0).TotalLength - document.GetLineByNumber(0).Length);
    }

    /// <summary>A file read into a preserving document has to come back out unchanged.</summary>
    [TestMethod]
    public void PreservingDocumentKeepsTerminatorsAndReportsThem()
    {
        const string SOURCE = "a\r\nb\nc";
        var document = EditableTextDocument.CreatePreservingLineEndings(SOURCE);

        Assert.AreEqual(SOURCE, document.ToString());
        Assert.AreEqual(3, document.LineCount);
        Assert.AreEqual("\r\n", document.GetLineByNumber(0).Delimiter);
        Assert.AreEqual("\n", document.GetLineByNumber(1).Delimiter);
        Assert.AreEqual(string.Empty, document.GetLineByNumber(2).Delimiter);
        Assert.AreEqual(1, document.GetLineByNumber(0).Length, "The carriage return belongs to the terminator, not the line.");
    }

    /// <summary>
    /// A lone carriage return terminates a line in both modes, as it does in AvalonEdit and the
    /// Visual Studio text model. Only the stored characters differ between the modes.
    /// </summary>
    [TestMethod]
    public void BothModesBreakLinesAtTheSamePlaces()
    {
        const string SOURCE = "a\r\nb\rc\nd";
        var normalizing = new EditableTextDocument(SOURCE);
        var preserving = EditableTextDocument.CreatePreservingLineEndings(SOURCE);

        Assert.AreEqual(4, normalizing.LineCount);
        Assert.AreEqual(4, preserving.LineCount);
        Assert.AreEqual("\r", preserving.GetLineByNumber(1).Delimiter);
        Assert.AreEqual("\n", normalizing.GetLineByNumber(1).Delimiter);
        Assert.AreEqual("b", preserving.GetText(preserving.GetLineByNumber(1).Offset, 1));
    }

    [TestMethod]
    public void PreservingDocumentTracksTerminatorsThroughEdits()
    {
        var document = EditableTextDocument.CreatePreservingLineEndings("one\r\ntwo");

        document.Insert(3, "X");
        Assert.AreEqual("oneX\r\ntwo", document.ToString());
        Assert.AreEqual("\r\n", document.GetLineByNumber(0).Delimiter);

        document.Insert(document.TextLength, "\r\nthree");
        Assert.AreEqual(3, document.LineCount);
        Assert.AreEqual("\r\n", document.GetLineByNumber(1).Delimiter);
        Assert.AreEqual("three", document.GetText(document.GetLineByNumber(2).Offset, 5));
    }

    /// <summary>Splitting a two-character terminator has to be seen as a line-structure change.</summary>
    [TestMethod]
    public void InsertingACarriageReturnBeforeALineFeedFormsOneTerminator()
    {
        var document = EditableTextDocument.CreatePreservingLineEndings("one\ntwo");

        document.Insert(3, "\r");

        Assert.AreEqual(2, document.LineCount);
        Assert.AreEqual("\r\n", document.GetLineByNumber(0).Delimiter);
        Assert.AreEqual(3, document.GetLineByNumber(0).Length);
        Assert.AreEqual(5, document.GetLineByNumber(1).Offset);
    }
}
