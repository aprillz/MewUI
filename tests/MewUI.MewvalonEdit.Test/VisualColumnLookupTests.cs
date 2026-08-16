using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The two point-to-column lookups. One rounds to the nearest boundary, the other floors to the
/// character the point is inside; they used to share their virtual-space branch, which made the
/// floor round as well.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class VisualColumnLookupTests
{
    private static TextEditor CreateEditor(string text)
    {
        var editor = new TextEditor
        {
            Text = text,
            FontFamily = "Consolas",
            FontSize = 13,
            ShowLineNumbers = false,
            SkipViewportCull = true
        };
        editor.Measure(new Size(600, 400));
        editor.Arrange(new Rect(0, 0, 600, 400));
        return editor;
    }

    [TestMethod]
    public void TheFloorDoesNotRoundInsideACharacter()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef");
        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(
            editor.Document.GetLineByNumber(1))!;
        double advance = visualLine.GetVisualXPosition(1) - visualLine.GetVisualXPosition(0);

        // Three quarters into the third character: the nearest boundary is the one after it.
        var point = new Point(visualLine.GetVisualXPosition(2) + (advance * 0.75), 0);

        Assert.AreEqual(3, visualLine.GetVisualColumn(point, allowVirtualSpace: false),
            "The nearest lookup must round up past the halfway mark.");
        Assert.AreEqual(2, visualLine.GetTextViewPositionFloor(point, allowVirtualSpace: false).VisualColumn,
            "The floor must stay on the character the point is inside.");
    }

    [TestMethod]
    public void TheFloorTruncatesInVirtualSpace()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("ab");
        editor.TextArea.Options.EnableVirtualSpace = true;
        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(
            editor.Document.GetLineByNumber(1))!;
        double wideSpace = editor.TextArea.TextView.WideSpaceWidth;

        // Three quarters past the second virtual column.
        var point = new Point(visualLine.TextLines[0].Bounds.Width + (wideSpace * 2.75), 0);

        Assert.AreEqual(5, visualLine.GetVisualColumn(point, allowVirtualSpace: true),
            "The nearest lookup must round the virtual columns.");
        Assert.AreEqual(4, visualLine.GetTextViewPositionFloor(point, allowVirtualSpace: true).VisualColumn,
            "The floor must truncate the virtual columns.");
    }

    [TestMethod]
    public void TheFloorStopsAtTheLineEndWithoutVirtualSpace()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("ab");
        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(
            editor.Document.GetLineByNumber(1))!;
        var point = new Point(visualLine.TextLines[0].Bounds.Width + 100, 0);

        Assert.AreEqual(2, visualLine.GetTextViewPositionFloor(point, allowVirtualSpace: false).VisualColumn,
            "Past the line with nowhere to go, the floor is the line end rather than its last character.");
    }
}
