using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Controls;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The folding gutter's extent line and the outline around a collapsed placeholder are drawn, not
/// laid out, so only a real render shows them. Each case isolates the drawing under test: comparing
/// whole frames would pass on the box or the changed text alone.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FoldingVisualTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 160;
    private const int GUTTER_WIDTH = 20;
    private const string TEXT = "class A\n{\n    void M()\n    {\n        Body();\n    }\n}\n";

    // One brace pair only: a second folding would put another box in the band under test and the
    // comparison would pass on that box instead of on the extent line.
    private const string SINGLE_FOLD_TEXT = "class A\n{\n    int x;\n    int y;\n    int z;\n}\n";

    [TestMethod]
    public void AnExpandedSectionDrawsItsExtentBelowTheBox()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        byte[] withoutFoldings = Render(SINGLE_FOLD_TEXT, addFoldings: false, folded: false);
        byte[] withFoldings = Render(SINGLE_FOLD_TEXT, addFoldings: true, folded: false);

        // The document's only box sits on the second row, so anything differing further down the
        // gutter can only be the line running to the section end.
        double lineHeight = MeasureLineHeight(SINGLE_FOLD_TEXT);
        int bandTop = (int)Math.Ceiling(lineHeight * 3);
        int differing = CountDifferingPixels(
            withoutFoldings, withFoldings, new Rect(0, bandTop, GUTTER_WIDTH, HEIGHT - bandTop));

        Assert.IsGreaterThan(0, differing, "The gutter drew no extent line under the folding box.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MarginsStayLeftOfTheTextWhenOneIsAdded(bool showLineNumbers)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var editor = new TextEditor
        {
            Text = TEXT,
            ShowLineNumbers = showLineNumbers,
            SkipViewportCull = true
        };
        // Adding a margin rebuilds the host grid, which transfers the surface to the new grid; a
        // grid position assigned before that transfer is lost and the margin lands to the right.
        FoldingManager.Install(editor);
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        var margin = editor.TextArea.LeftMargins.OfType<FoldingMargin>().Single();
        Assert.IsLessThan(editor.TextArea.TextView.Host.TextViewportBounds.X, margin.Bounds.X,
            "The folding margin must sit left of the text.");
    }

    [TestMethod]
    public void TheMarkerBoxIsFilledSoTheExtentLineStopsAtIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // Nested sections: the outer one's extent line runs down past the inner one's box, so an
        // unfilled box lets that line show through it. This is why AvalonEdit fills the marker.
        // Counting an exact colour is useless here because the strokes are antialiased; what the
        // fill has to do is change the gutter at all compared with drawing no fill.
        byte[] filled = RenderElement(BuildEditor(TEXT, fill: null));
        byte[] unfilled = RenderElement(BuildEditor(TEXT, fill: Color.Transparent));

        int differing = CountDifferingPixels(filled, unfilled, new Rect(0, 0, GUTTER_WIDTH, HEIGHT));

        Assert.IsGreaterThan(0, differing,
            "The marker box paints no fill by default, so the extent line shows through it.");
    }

    [TestMethod]
    public void FoldingMarkerColoursAreMewPropertiesSoTheyRepaintAndBind()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var editor = BuildEditor(TEXT, fill: null);
        var margin = editor.TextArea.LeftMargins.OfType<FoldingMargin>().Single();

        // Registered with AffectsRender, so assigning repaints without the caller invalidating.
        Assert.IsTrue(FoldingMargin.FoldingMarkerBrushProperty.AffectsRender);
        Assert.IsTrue(FoldingMargin.FoldingMarkerBackgroundBrushProperty.AffectsRender);

        // Palette application: the caller can drive the colours from the active theme and have
        // them reapplied when it changes.
        margin.WithTheme((theme, target) => target.FoldingMarkerBrush = theme.Palette.Accent);

        Assert.IsNotNull(margin.FoldingMarkerBrush);
    }

    [TestMethod]
    public void LineNumbersTakeALocalForegroundSoTheyDoNotInheritTheBodyColour()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var editor = new TextEditor { Text = TEXT, ShowLineNumbers = true, SkipViewportCull = true };
        FoldingManager.Install(editor);
        // Inheritance needs the margins parented, and the template that hosts them builds on the
        // first measure.
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        var lineNumbers = editor.TextArea.LeftMargins
            .OfType<Aprillz.MewUI.MewvalonEdit.Rendering.LineNumberMargin>().Single();
        var folding = editor.TextArea.LeftMargins.OfType<FoldingMargin>().Single();

        editor.Foreground = Color.FromRgb(0x11, 0x22, 0x33);

        // The folding margin takes no local value, so it proves inheritance actually reaches a
        // margin. Without it this test would also pass if the numbers merely fell back to a default.
        Assert.AreEqual(editor.Foreground, folding.Foreground);

        // Foreground inherits, so without a local value the numbers would come out in the editor's
        // body colour and stop reading as a gutter. The editor assigns one from the theme.
        Assert.AreNotEqual(editor.Foreground, lineNumbers.Foreground);

        editor.LineNumbersForeground = Color.FromRgb(0x44, 0x55, 0x66);
        Assert.AreEqual(Color.FromRgb(0x44, 0x55, 0x66), lineNumbers.Foreground);

        // Back to null means back to the theme, not back to inheriting the body colour.
        editor.LineNumbersForeground = null;
        Assert.AreNotEqual(editor.Foreground, lineNumbers.Foreground);
    }

    private static TextEditor BuildEditor(string text, Color? fill)
    {
        var editor = new TextEditor { Text = text, ShowLineNumbers = false, SkipViewportCull = true };
        var manager = FoldingManager.Install(editor);
        if (fill.HasValue)
        {
            editor.TextArea.LeftMargins.OfType<FoldingMargin>().Single().FoldingMarkerBackgroundBrush = fill;
        }
        new BraceFoldingStrategy().UpdateFoldings(manager, editor.Document);
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        return editor;
    }


    private static byte[] RenderElement(TextEditor editor)
    {
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

    private static double MeasureLineHeight(string text)
    {
        var editor = new TextEditor { Text = text, ShowLineNumbers = false, SkipViewportCull = true };
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        return editor.TextArea.TextView.DefaultLineHeight;
    }

    [TestMethod]
    public void CollapsedPlaceholderIsOutlined()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // The placeholder and its box are the only things drawn in the marker colour right of the
        // gutter, so counting that colour isolates them from the rest of the document.
        var marker = new TextEditor().FoldingMarkerColor;
        int boxed = CountPixelsColored(
            Render(TEXT, addFoldings: true, folded: true),
            new Rect(GUTTER_WIDTH, 0, WIDTH - GUTTER_WIDTH, HEIGHT),
            marker);

        // The same glyph, in the same colour, with nothing drawn around it.
        int glyphOnly = CountPixelsColored(
            RenderElement(BuildPlainEditor("...", marker)), new Rect(0, 0, WIDTH, HEIGHT), marker);

        Assert.IsGreaterThan(glyphOnly, boxed, "The collapsed placeholder was not outlined.");
    }

    private static TextEditor BuildPlainEditor(string text, Color foreground)
    {
        var editor = new TextEditor
        {
            Text = text,
            Foreground = foreground,
            ShowLineNumbers = false,
            SkipViewportCull = true
        };
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        return editor;
    }

    /// <summary>
    /// Pixels close to <paramref name="color"/>. Both the glyph and the stroke are antialiased, so
    /// an exact match would count almost nothing.
    /// </summary>
    private static int CountPixelsColored(byte[] frame, Rect region, Color color)
    {
        const int TOLERANCE = 24;
        // The surface stores premultiplied colour, and the marker is not opaque.
        int blue = color.B * color.A / 255;
        int green = color.G * color.A / 255;
        int red = color.R * color.A / 255;
        int matching = 0;
        for (int y = (int)region.Y; y < (int)region.Bottom && y < HEIGHT; y++)
        {
            for (int x = (int)region.X; x < (int)region.Right && x < WIDTH; x++)
            {
                int offset = (y * WIDTH + x) * 4;
                if (offset + 2 >= frame.Length)
                {
                    continue;
                }
                if (frame[offset + 3] != 0 &&
                    Math.Abs(frame[offset] - blue) <= TOLERANCE &&
                    Math.Abs(frame[offset + 1] - green) <= TOLERANCE &&
                    Math.Abs(frame[offset + 2] - red) <= TOLERANCE)
                {
                    matching++;
                }
            }
        }
        return matching;
    }

    private static byte[] Render(string text, bool addFoldings, bool folded)
    {
        var editor = new TextEditor
        {
            Text = text,
            ShowLineNumbers = false,
            SkipViewportCull = true
        };
        var manager = FoldingManager.Install(editor);
        if (addFoldings)
        {
            new BraceFoldingStrategy().UpdateFoldings(manager, editor.Document);
            if (folded)
            {
                var first = manager.AllFoldings.FirstOrDefault();
                Assert.IsNotNull(first, "The brace strategy found no folding to collapse.");
                first.IsFolded = true;
            }
        }
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

    private static int CountDifferingPixels(byte[] left, byte[] right, Rect region)
    {
        int differing = 0;
        for (int y = (int)region.Y; y < (int)region.Bottom && y < HEIGHT; y++)
        {
            for (int x = (int)region.X; x < (int)region.Right && x < WIDTH; x++)
            {
                int offset = (y * WIDTH + x) * 4;
                if (offset + 2 >= Math.Min(left.Length, right.Length))
                {
                    continue;
                }
                if (left[offset] != right[offset] ||
                    left[offset + 1] != right[offset + 1] ||
                    left[offset + 2] != right[offset + 2])
                {
                    differing++;
                }
            }
        }
        return differing;
    }
}
