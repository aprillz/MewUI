using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.Rendering;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// A click on the next line moves the current-line highlight and the caret together, one under the
/// glyphs and one over them. Only those two lines are repainted, not every line the glyphs cover
/// between the highlight and the caret.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CurrentLineClickTests
{
    private const int WIDTH = 520;
    private const int HEIGHT = 360;

    // An edge the repaint pads for antialiasing, in DIPs.
    private const double EDGE_SLACK = 2;

    [TestMethod]
    public void ClickingTheNextLine_RepaintsTheTwoLinesAndTheCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var editor = new TextEditor
        {
            ShowLineNumbers = true,
            WordWrap = false,
            Text = string.Join("\n", Enumerable.Range(0, 16).Select(line => $"public static int Value{line}(string name) => name.Length + {line};")),
            SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#"),
        };
        editor.Options.HighlightCurrentLine = true;
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = editor;
        window.PerformLayout();

        var factory = Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        double lineHeight = editor.TextArea.TextView.DefaultLineHeight;
        var text = editor.Surface.TextViewportBounds;
        Point LineCenter(int line) => new(text.X + 120, text.Y + (lineHeight * (line + 0.5)));

        window.SendClick(LineCenter(2));
        for (int warm = 0; warm < 3; warm++)
        {
            Render(window, surface);
        }

        window.SendClick(LineCenter(3));
        Render(window, surface);

        Assert.AreEqual(4, editor.TextArea.Caret.Line, "the click did not reach the fourth line");
        Assert.IsNotNull(window.LastRetainedDirtyRect, $"the frame was drawn whole ({window.LastWholeFrameReason})");
        double top = text.Y + (lineHeight * 2) - EDGE_SLACK;
        double bottom = text.Y + (lineHeight * 4) + EDGE_SLACK;
        foreach (var dirty in window.LastRetainedDirtyRects)
        {
            Assert.IsTrue(
                dirty.Y >= top && dirty.Bottom <= bottom,
                $"the repaint {dirty} reaches past the third and fourth lines ({top}..{bottom}); all repaints: {string.Join(" ", window.LastRetainedDirtyRects)}");
        }

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{differing} pixels differ from a frame drawn straight from the visuals");
    }

    private static void Render(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }
}
