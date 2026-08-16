using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// A line long enough to be laid out in slices only ever has one of them laid out. The rectangle
/// used to ask for the line by its start and so always got the head slice, which answered for a
/// rectangle standing further along with virtual space instead of the text there.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RectangleSelectionSliceTests
{
    private const int LONG = 4000;

    private static TextEditor CreateEditor(string text)
    {
        var editor = new TextEditor
        {
            Text = text,
            FontFamily = "Consolas",
            FontSize = 13,
            ShowLineNumbers = false,
            WordWrap = false,
            SkipViewportCull = true
        };
        editor.Measure(new Size(400, 200));
        editor.Arrange(new Rect(0, 0, 400, 200));
        return editor;
    }

    [TestMethod]
    public void ARectangleFarAlongALongLineSelectsItsText()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        string line = new string('x', LONG);
        var editor = CreateEditor(line + "\n" + line);
        var extent = ((Aprillz.MewUI.Text.ITextViewHost)editor.Surface).GetLineExtent(0);
        Assert.IsNotNull(extent, "A non-wrapping view must answer with the line's extent.");

        // Two characters at offset 3000, far past anything the viewport laid out.
        var head = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1))!;
        Assert.IsLessThan(LONG, head.DocumentLength, "The line was not sliced, so there is nothing to test.");

        double startX = extent.GetXForOffset(3000);
        double endX = extent.GetXForOffset(3002);
        var selection = new RectangleSelection(
            editor.TextArea,
            new TextViewPosition(1, 3001, -1),
            new TextViewPosition(2, 3003, -1));

        var segments = selection.Segments.ToArray();
        Assert.HasCount(2, segments);
        foreach (var segment in segments)
        {
            Assert.AreEqual(2, segment.Length,
                $"The rectangle covered {segment.Length} characters instead of the two it stands over.");
        }
        Assert.AreEqual("xx\nxx", selection.GetText().ReplaceLineEndings("\n"));
        Assert.IsGreaterThan(0, startX);
        Assert.IsGreaterThan(startX, endX);
    }

    /// <summary>
    /// A rectangle wide enough that its two edges stand in different slices. Each edge is read where
    /// it is, so the span between them is the text it covers rather than what one slice could see.
    /// </summary>
    [TestMethod]
    public void ARectangleWiderThanASliceCoversTheTextBetweenItsEdges()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        string line = new string('x', LONG);
        var editor = CreateEditor(line + "\n" + line);
        var selection = new RectangleSelection(
            editor.TextArea,
            new TextViewPosition(1, 201, -1),
            new TextViewPosition(2, 3501, -1));

        var segments = selection.Segments.ToArray();
        Assert.HasCount(2, segments);
        foreach (var segment in segments)
        {
            Assert.AreEqual(3300, segment.Length,
                $"The rectangle covered {segment.Length} characters instead of the 3300 between its edges.");
        }
    }
}
