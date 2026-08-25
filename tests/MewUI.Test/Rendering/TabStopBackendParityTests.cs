using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TabStopBackendParityTests
{
    [TestMethod]
    public void TabStopLandsOnFourRealSpaceAdvances()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows backends only.");
            return;
        }

        using var gdi = new GdiGraphicsFactory();
        using var d2d = new Direct2DGraphicsFactory();
        foreach ((string name, IGraphicsFactory factory) in new (string, IGraphicsFactory)[] { ("GDI", gdi), ("D2D", d2d) })
        {
            using var context = ((ITextBackendFactory)factory).CreateTextMeasurementContext(96);
            using var font = factory.CreateFont("Consolas", 14, 96);
            double spaceAdvance = context.GetUtf16PrefixAdvances(" ", font)![0];

            var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = "\tX".AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle("Consolas", 14),
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap, TabSize = 4 }
            });
            double tab = layout.GetCaretBounds(new CharacterHit(1, 0)).X;

            // Space-indented lines draw at real advances, so the tab stop must not
            // derive from MeasureText, whose width includes the sizing padding.
            Assert.AreEqual(spaceAdvance * 4, tab, 0.01,
                $"{name}: a tab with TabSize 4 must match four space glyph advances.");
        }
    }
}
