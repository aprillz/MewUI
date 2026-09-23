using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A scroll inside a window moves every visual it carries, so every container sees its children's
/// bounds change. Those children kept their places inside it, which is not a new layout of the
/// container, and nothing that was already drawn is drawn again.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedScrollInWindowTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 180;
    private const int CARD_COUNT = 8;

    private sealed class Card : ContentControl
    {
        internal int RenderCount { get; private set; }

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            context.FillRectangle(Bounds, Color.FromArgb(255, 220, 230, 245));
        }
    }

    [TestMethod]
    public void AScroll_DoesNotDrawAgainTheContainersItCarries()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var cards = new Card[CARD_COUNT];
        var content = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < cards.Length; index++)
        {
            cards[index] = new Card
            {
                Height = 40,
                Padding = new Thickness(6),
                Content = new TextBlock { Text = "Card " + index },
            };
            content.Children(cards[index]);
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = content };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = scroll;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frame(window, surface);

        foreach (double offset in new[] { 3.38, 9.71, 17.05, 30.4, 42 })
        {
            int[] before = cards.Select(card => card.RenderCount).ToArray();
            scroll.SetScrollOffsets(0, offset);
            Frame(window, surface);

            int redrawn = 0;
            for (int index = 0; index < cards.Length; index++)
            {
                if (before[index] > 0 && cards[index].RenderCount > before[index])
                {
                    redrawn++;
                }
            }

            Assert.AreEqual(0, redrawn, $"scrolling to {offset} drew {redrawn} cards again that had been drawn already");
            AssertMatchesReference(factory, window, surface, $"the frame scrolled to {offset}");
        }
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface surface, string what)
    {
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

        Assert.AreEqual(0, differing, $"{what} differs from a frame drawn straight from the visuals at {differing} pixels");
    }

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }
}
