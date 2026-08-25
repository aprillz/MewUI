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
