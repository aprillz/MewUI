using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Test;

/// <summary>
/// Pins what the whitespace options paint. Space and tab are stood in for by elements, so a marker
/// that reported its own width would collapse the tab stop; that is the regression these guard.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WhitespaceMarkerTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 80;

    [TestMethod]
    public void ShowSpacesPaintsSomething()
    {
        RequireWindows();
        Assert.IsGreaterThan(0, DifferingPixels("a b c", options => options.ShowSpaces = true),
            "Turning on space markers painted nothing.");
    }

    [TestMethod]
    public void ShowTabsPaintsSomething()
    {
        RequireWindows();
        Assert.IsGreaterThan(0, DifferingPixels("a\tb\tc", options => options.ShowTabs = true),
            "Turning on tab markers painted nothing.");
    }

    [TestMethod]
    public void ShowEndOfLinePaintsSomething()
    {
        RequireWindows();
        Assert.IsGreaterThan(0, DifferingPixels("one\ntwo", options => options.ShowEndOfLine = true),
            "Turning on end-of-line markers painted nothing.");
    }

    /// <summary>
    /// A marked tab still reaches its tab stop. The marker element reports no width and the tab
    /// itself follows it, so substituting a glyph for the tab would be the shrink this catches.
    /// </summary>
    [TestMethod]
    public void MarkingTabsLeavesTheirWidthAlone()
    {
        RequireWindows();
        var plain = Layout("a\tb", static _ => { });
        var marked = Layout("a\tb", static options => options.ShowTabs = true);

        Assert.AreEqual(plain, marked, 0.01, "The tab stop moved when the marker was turned on.");
    }

    /// <summary>Space markers must not change the layout either: one dot replaces one space.</summary>
    [TestMethod]
    public void MarkingSpacesLeavesTheirWidthAlone()
    {
        RequireWindows();
        var plain = Layout("a b", static _ => { });
        var marked = Layout("a b", static options => options.ShowSpaces = true);

        Assert.AreEqual(plain, marked, 0.01, "The line width moved when space markers were turned on.");
    }

    /// <summary>
    /// An element knows where it stands on the visual surface, not where its text starts. A tab
    /// marker stands two columns in for one character, so everything after it sits one column
    /// further along than its offset.
    /// </summary>
    [TestMethod]
    public void AnElementAfterATabMarkerKnowsItsShiftedColumn()
    {
        RequireWindows();
        var editor = CreateEditor("\ta b", static options =>
        {
            options.ShowTabs = true;
            options.ShowSpaces = true;
        });
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(
            editor.Document.GetLineByNumber(1));
        Assert.IsNotNull(visualLine);
        Assert.HasCount(2, visualLine.Elements, "Expected a tab marker and a space marker.");

        Assert.AreEqual(0, visualLine.Elements[0].RelativeTextOffset);
        Assert.AreEqual(0, visualLine.Elements[0].VisualColumn, "The tab marker starts the line.");
        Assert.AreEqual(2, visualLine.Elements[1].RelativeTextOffset);
        Assert.AreEqual(3, visualLine.Elements[1].VisualColumn,
            "The space marker sits one column past its offset, the tab marker holding two columns.");
    }

    /// <summary>
    /// The marker takes the width of the space it stands in for at every density. Measuring its own
    /// glyph instead let the two round apart above 100%, which moved the rest of the line whenever
    /// the markers were turned on.
    /// </summary>
    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(192u)]
    public void ASpaceMarkerIsAsWideAsItsSpace(uint dpi)
    {
        RequireWindows();
        var editor = CreateEditor("a b", static options => options.ShowSpaces = true);
        var style = new TextRunStyle(editor.FontFamily, editor.FontSize, editor.FontWeight);
        var element = new WhitespaceMarkerElement("·", " ", style);

        Assert.AreEqual(
            MarkerLayout.For(" ", style, dpi).MeasuredSize.Width,
            element.Measure(dpi).Width,
            0.001);
    }

    /// <summary>
    /// Toggling the option shows immediately. The scanned elements cache on the document version,
    /// so an option the generators read leaves it stale and the marker waits for the next edit.
    /// </summary>
    [TestMethod]
    public void TogglingSpaceMarkersShowsWithoutAnEdit()
    {
        RequireWindows();
        var editor = CreateEditor("a b c", static _ => { });
        byte[] off = Render(editor);

        editor.Options.ShowSpaces = true;
        byte[] on = Render(editor);

        Assert.IsGreaterThan(0, DifferingPixels(off, on),
            "The space markers only appeared after the next edit.");
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }
    }

    /// <summary>
    /// Where the caret sits after the whitespace character, which is the tab stop a substitution
    /// would collapse. The offset is mapped because a marker element lengthens the laid-out line.
    /// </summary>
    private static double Layout(string text, Action<TextEditorOptions> configure)
    {
        var editor = CreateEditor(text, configure);
        Render(editor);
        var line = editor.Surface.VisibleTextLines[0];
        return line.GetCaretBounds(new CharacterHit(line.MapSourceOffsetToProjected(2), 0)).X;
    }

    private static int DifferingPixels(string text, Action<TextEditorOptions> configure)
        => DifferingPixels(
            Render(CreateEditor(text, static _ => { })),
            Render(CreateEditor(text, configure)));

    private static int DifferingPixels(byte[] off, byte[] on)
    {
        int differing = 0;
        for (int index = 0; index < Math.Min(off.Length, on.Length); index += 4)
        {
            if (off[index] != on[index] || off[index + 1] != on[index + 1] || off[index + 2] != on[index + 2])
            {
                differing++;
            }
        }
        return differing;
    }

    private static TextEditor CreateEditor(string text, Action<TextEditorOptions> configure)
    {
        var editor = new TextEditor
        {
            Text = text,
            ShowLineNumbers = false,
            SkipViewportCull = true
        };
        configure(editor.Options);
        return editor;
    }

    private static byte[] Render(TextEditor editor)
    {
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        var factory = Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            editor.Render(context);
            context.EndFrame();
        }
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }
}
