using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Text laid out in the width it just measured must not wrap. Backends report advances as
/// single-precision floats, so the measured width and the width accumulated by the wrap decision
/// differ by a float epsilon; without slack a control that sizes itself to its text wraps as soon
/// as it is arranged, which is what made a message box grow a second line at 150%.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextWrapToleranceTests
{
    private const string MESSAGE = "This is a Warning message box sample.";

    [TestMethod]
    [DataRow(96u)]
    [DataRow(144u)]
    public void Direct2D_TextFitsTheWidthItMeasured(uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertFitsItsOwnWidth(factory, dpi);
    }

    [TestMethod]
    [DataRow(96u)]
    [DataRow(144u)]
    public void Gdi_TextFitsTheWidthItMeasured(uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertFitsItsOwnWidth(factory, dpi);
    }

    private static void AssertFitsItsOwnWidth(IGraphicsFactory factory, uint dpi)
    {
        double measured = Layout(factory, dpi, 1_000_000).MeasuredSize.Width;

        Assert.AreEqual(1, Layout(factory, dpi, measured).Lines.Count,
            "The text wrapped in exactly the width it measured.");
        Assert.AreEqual(1, Layout(factory, dpi, measured - (measured * 1e-7)).Lines.Count,
            "A deficit within float precision counted as overflow.");
        Assert.IsGreaterThan(1, Layout(factory, dpi, measured * 0.6).Lines.Count,
            "The slack swallowed a real overflow.");
    }

    private static ITextLayout Layout(IGraphicsFactory factory, uint dpi, double maxWidth)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = MESSAGE.AsMemory(),
            Dpi = dpi,
            DefaultStyle = new TextRunStyle("Segoe UI", 12),
            Paragraph = new TextParagraphStyle { MaxWidth = maxWidth, Wrapping = TextWrapping.Wrap }
        });
}
