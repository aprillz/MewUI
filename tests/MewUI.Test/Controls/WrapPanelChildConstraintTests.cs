using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Pins the WrapPanel child constraint against WPF, whose numbers were measured at 150% with the
/// same host: a vertically scrolling viewer over a stack of panels, matching the gallery card grid.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WrapPanelChildConstraintTests
{
    private const string LONG_TEXT =
        "The Slider writes the number back. The other targets only render it, " +
        "and the TextBlock uses a converter.";

    private const int CARD_WIDTH = 120;
    private const int CARD_HEIGHT = 80;

    [TestMethod]
    public void AutoSizedImageTakesTheOfferedLineWidth()
    {
        RunInGalleryHost(320, 400, panel =>
        {
            var bitmap = new WriteableBitmap(CARD_WIDTH, CARD_HEIGHT);
            var cards = new[]
            {
                new Image().Source(bitmap).StretchMode(Stretch.Uniform),
                new Image().Source(bitmap).StretchMode(Stretch.Uniform),
            };
            panel.Children(cards);
            // WPF reports 286.7 here for a 120x80 image, and splits the two across rows. Content
            // that scales to the offered size has to be given an explicit size by the caller.
            return () =>
            {
                Assert.AreEqual(panel.Bounds.Width, cards[0].Bounds.Width, 1.0,
                    $"The image reported {cards[0].Bounds.Width} instead of the offered line width.");
                Assert.AreNotEqual(cards[0].Bounds.Y, cards[1].Bounds.Y,
                    "Two full-width images were placed on the same row.");
            };
        });
    }

    [TestMethod]
    public void ExplicitlySizedImageCardsShareOneRow()
    {
        RunInGalleryHost(320, 400, panel =>
        {
            var bitmap = new WriteableBitmap(CARD_WIDTH, CARD_HEIGHT);
            var cards = new[]
            {
                new Image().Source(bitmap).StretchMode(Stretch.Uniform).Width(CARD_WIDTH).Height(CARD_HEIGHT),
                new Image().Source(bitmap).StretchMode(Stretch.Uniform).Width(CARD_WIDTH).Height(CARD_HEIGHT),
            };
            panel.Children(cards);
            return () =>
            {
                foreach (var card in cards)
                {
                    Assert.AreEqual(CARD_WIDTH, card.Bounds.Width, 0.5,
                        $"An explicitly sized card measured {card.Bounds.Width}.");
                }
                Assert.AreEqual(cards[0].Bounds.Y, cards[1].Bounds.Y, 0.5,
                    "Two cards that fit the viewport width were split across rows.");
            };
        });
    }

    [TestMethod]
    public void WrappingTextStaysInsideTheViewportWidth()
    {
        RunInGalleryHost(320, 400, panel =>
        {
            var wrapped = new TextBlock().Text(LONG_TEXT).TextWrapping(TextWrapping.Wrap);
            var singleLine = new TextBlock().Text("One line.");
            panel.Children(wrapped, singleLine);
            return () =>
            {
                Assert.IsLessThanOrEqualTo(panel.Bounds.Width, wrapped.Bounds.Width,
                    $"The block was arranged {wrapped.Bounds.Width} wide inside a {panel.Bounds.Width} panel.");
                Assert.IsGreaterThan(singleLine.Bounds.Height * 1.5, wrapped.Bounds.Height,
                    $"Wrapped text kept a single-line box (wrapped={wrapped.Bounds.Height}, single={singleLine.Bounds.Height}).");
            };
        });
    }

    [TestMethod]
    public void WrappingTextKeepsOneLineWhenHorizontalScrollIsEnabled()
    {
        RunInGalleryHost(320, 400, panel =>
        {
            var wrapped = new TextBlock().Text(LONG_TEXT).TextWrapping(TextWrapping.Wrap);
            var singleLine = new TextBlock().Text("One line.");
            panel.Children(wrapped, singleLine);
            // An unbounded main axis is the caller asking for natural width, so nothing may clamp it.
            return () => Assert.AreEqual(singleLine.Bounds.Height, wrapped.Bounds.Height, 0.5,
                $"A horizontally scrollable viewer clamped the block to {wrapped.Bounds.Width}.");
        }, horizontalScroll: ScrollMode.Auto);
    }

    /// <summary>Builds ScrollViewer over StackPanel over WrapPanel, the gallery card grid host.</summary>
    private static void RunInGalleryHost(
        double windowWidth,
        double windowHeight,
        Func<WrapPanel, Action> build,
        ScrollMode horizontalScroll = ScrollMode.Disabled)
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
            var panel = new WrapPanel().Orientation(Orientation.Horizontal).Spacing(8);
            var assert = build(panel);

            using var window = HeadlessWindow.Create(windowWidth, windowHeight);
            // The WPF baseline was captured at 150%, so the numbers only line up at that scale.
            window.SetDpi(144);
            window.Content = new ScrollViewer()
                .VerticalScroll(ScrollMode.Auto)
                .HorizontalScroll(horizontalScroll)
                .Padding(8)
                .Content(new StackPanel().Vertical().Spacing(16).Children(panel));
            window.PerformLayout();

            assert();
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
