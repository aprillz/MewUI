using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class LineBoxTrimTests
{
    [TestMethod]
    public void FastPath_CapTrim_RemovesAscentAboveCapOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = Layout(factory, "Buttons", LineBoxTrim.None);
        var trimmed = Layout(factory, "Buttons", LineBoxTrim.Cap);

        Assert.IsTrue(((ManagedTextLayout)trimmed).IsFastPath);
        double topTrim = plain.ContentHeight - trimmed.ContentHeight;
        Assert.IsGreaterThan(0, topTrim, "Cap trim removed nothing.");
        Assert.AreEqual(
            plain.Lines[0].Baseline - trimmed.Lines[0].Baseline, topTrim, 0.001,
            "The top trim must come off the baseline offset alone, keeping the descent.");
    }

    [TestMethod]
    public void FastPath_CapAndBaseline_EndsTheBoxAtTheBaseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var trimmed = Layout(factory, "Buttons", LineBoxTrim.CapAndBaseline);

        Assert.AreEqual(trimmed.Lines[0].Baseline, trimmed.ContentHeight, 0.001,
            "With a baseline trim the box bottom must sit on the baseline.");
        Assert.AreEqual(trimmed.Lines[0].Baseline, trimmed.Lines[0].Bounds.Height, 0.001);
    }

    [TestMethod]
    public void ClusterPath_MultiLine_TrimsOuterEdgesOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = Layout(factory, "One\nTwo\nThree", LineBoxTrim.None);
        var trimmed = Layout(factory, "One\nTwo\nThree", LineBoxTrim.CapAndBaseline);

        Assert.IsFalse(((ManagedTextLayout)trimmed).IsFastPath);
        Assert.HasCount(3, trimmed.Lines);
        double topTrim = plain.Lines[0].Baseline - trimmed.Lines[0].Baseline;
        Assert.IsGreaterThan(0, topTrim);

        // Interior geometry only shifts up by the first line's trim; gaps stay identical.
        for (int index = 1; index < trimmed.Lines.Count; index++)
        {
            Assert.AreEqual(
                plain.Lines[index].Bounds.Y - topTrim, trimmed.Lines[index].Bounds.Y, 0.001);
        }
        Assert.AreEqual(plain.Lines[1].Bounds.Height, trimmed.Lines[1].Bounds.Height, 0.001,
            "An interior line must keep its full box.");

        var last = trimmed.Lines[^1];
        Assert.AreEqual(last.Bounds.Y + last.Baseline, trimmed.ContentHeight, 0.001,
            "The trimmed content must end at the last line's baseline.");
    }

    [TestMethod]
    public void NoneIsDefault_AndLeavesGeometryUntouched()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var implicitDefault = factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = "Buttons".AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 18)
        });
        var explicitNone = Layout(factory, "Buttons", LineBoxTrim.None);

        Assert.AreEqual(implicitDefault.ContentHeight, explicitNone.ContentHeight, 0.001);
        Assert.AreEqual(implicitDefault.Lines[0].Baseline, explicitNone.Lines[0].Baseline, 0.001);
    }

    private static ITextLayout Layout(GdiGraphicsFactory factory, string text, LineBoxTrim trim)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 18),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap,
                LineBoxTrim = trim
            }
        });
}
