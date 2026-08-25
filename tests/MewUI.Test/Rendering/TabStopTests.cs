using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TabStopTests
{
    [TestMethod]
    public void TabSizeScalesTheRepeatingStop()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            double spaceWidth = MeasureCaretX(factory, " a", tabSize: 4) ;
            double fourColumns = MeasureCaretX(factory, "\ta", tabSize: 4);
            double eightColumns = MeasureCaretX(factory, "\ta", tabSize: 8);
            double twoColumns = MeasureCaretX(factory, "\ta", tabSize: 2);

            Assert.AreEqual(spaceWidth * 4, fourColumns, 0.5, "Default tab did not reach the fourth column.");
            Assert.AreEqual(spaceWidth * 8, eightColumns, 0.5, "TabSize 8 did not reach the eighth column.");
            Assert.AreEqual(spaceWidth * 2, twoColumns, 0.5, "TabSize 2 did not reach the second column.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void ExplicitTabStopsWinOverTabSize()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = "\ta".AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle("Consolas", 12),
                Paragraph = new TextParagraphStyle
                {
                    Wrapping = TextWrapping.NoWrap,
                    MaxWidth = double.PositiveInfinity,
                    TabStops = [50.0],
                    TabSize = 8
                }
            });

            Assert.AreEqual(50.0, layout.GetCaretBounds(new CharacterHit(1, 0)).X, 0.5);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    // Caret X after the first character, which is the width the tab (or space) advanced to.
    private static double MeasureCaretX(IGraphicsFactory factory, string text, int tabSize)
    {
        var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Consolas", 12),
            Paragraph = new TextParagraphStyle
            {
                Wrapping = TextWrapping.NoWrap,
                MaxWidth = double.PositiveInfinity,
                TabSize = tabSize
            }
        });
        return layout.GetCaretBounds(new CharacterHit(1, 0)).X;
    }
}
