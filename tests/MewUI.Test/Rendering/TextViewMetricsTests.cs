using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Metrics a margin lines its rows up against. They describe the view's own style, so document
/// content must not move them, and the y conversions must round-trip against them.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextViewMetricsTests
{
    [TestMethod]
    public void DefaultLineHeightIgnoresDocumentContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var plain = CreateView("aaa\nbbb");
        using var tall = CreateView("aaa\n一丁丂\nbbb");

        Assert.AreEqual(plain.DefaultLineHeight, tall.DefaultLineHeight, 0.01,
            "Wider glyphs in the document moved the default line height.");
        Assert.IsGreaterThan(0, plain.DefaultLineHeight);
        Assert.IsGreaterThan(0, plain.DefaultBaseline);
        Assert.IsLessThanOrEqualTo(plain.DefaultLineHeight, plain.DefaultBaseline);
    }

    [TestMethod]
    public void VisualTopAndLineNumberRoundTrip()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var view = CreateView("zero\none\ntwo\nthree\nfour");

        for (int line = 0; line < 5; line++)
        {
            double top = view.GetLineY(line);
            Assert.AreEqual(line, view.FindLineByY(top + 1),
                $"Line {line} at y={top} resolved to a different line.");
        }

        Assert.IsGreaterThan(view.GetLineY(4), view.ExtentHeight,
            "The document height does not cover the last line.");
    }

    [TestMethod]
    public void UniformLinesKeepTheDocumentHeightWhileScrolling()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var view = CreateView(string.Concat(Enumerable.Repeat("x := 2;\n", 300)));
        double initial = view.ExtentHeight;

        view.SetViewport(new TextViewport(400, 200, 0, initial - 200));
        double atEnd = view.ExtentHeight;
        view.SetViewport(new TextViewport(400, 200, 0, 0));

        Assert.AreEqual(initial, atEnd, 0.01, "Measuring the lines at the end changed the document height.");
        Assert.AreEqual(initial, view.ExtentHeight, 0.01, "Scrolling back changed the document height.");
        Assert.AreEqual(301 * view.DefaultLineHeight, initial, 0.01, "Unmeasured lines are not estimated at the real row height.");
    }

    private static TextViewLayout CreateView(string text)
    {
        var factory = new GdiGraphicsFactory();
        var view = new TextViewLayout(
            factory.TextEngine,
            new StringTextDocument(text),
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            new TextViewExtensionPipeline(),
            dpi: 96);
        view.SetViewport(new TextViewport(400, 200));
        return view;
    }
}
