using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The content of a scroll viewer is a frame of the scene: a scroll by whole pixels moves the frame, and the
/// update looks only into what comes into view instead of into every node the scroll carried. What the frame
/// shows has to stay what a frame drawn straight from the visuals shows, through every move and every change
/// made under a moved frame.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedFrameScrollTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 180;
    private const int CARD_COUNT = 200;
    private const double CARD_HEIGHT = 40;

    // A card and its text are the two nodes a row costs; the viewer, its bar and the content are the rest.
    private const int NODES_PER_CARD = 2;
    private const int NODES_AROUND = 8;

    private sealed class Card : ContentControl
    {
        private Color _fill = Color.FromArgb(255, 220, 230, 245);

        internal int RenderCount { get; private set; }

        internal Color Fill
        {
            get => _fill;
            set
            {
                _fill = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            context.FillRectangle(Bounds, _fill);
        }
    }

    [TestMethod]
    public void AWholePixelScroll_VisitsOnlyWhatComesIntoView()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var (window, scroll, cards) = CreateList();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frame(window, surface);

        var statistics = window.RetainedStatistics!;
        foreach (double offset in new[] { 17.0, 230.0, 1013.0, 1016.0, 40.0 })
        {
            int[] drawnBefore = cards.Select(card => card.RenderCount).ToArray();
            scroll.SetScrollOffsets(0, offset);
            window.PerformLayout();
            int visitedBefore = statistics.CapturedNodeCount;
            int carriedBefore = statistics.CarriedNodeCount;
            int movesBefore = statistics.FrameMoveCount;
            window.RenderFrameToSurface(surface);

            int visited = statistics.CapturedNodeCount - visitedBefore;
            int shown = cards.Count(card => card.Bounds.Bottom > 0 && card.Bounds.Y < HEIGHT);
            int allowed = shown * NODES_PER_CARD + NODES_AROUND;
            Console.Error.WriteLine($"scrolled to {offset}: looked into {visited} visuals, carried {statistics.CarriedNodeCount - carriedBefore}, {shown} of {CARD_COUNT} cards in view");
            Assert.IsTrue(visited <= allowed, $"scrolling to {offset} looked into {visited} visuals with {shown} cards in view (at most {allowed} expected)");
            Assert.AreEqual(1, statistics.FrameMoveCount - movesBefore, $"scrolling to {offset} did not move the content frame once");
            Assert.AreEqual(0, cards.Where((card, index) => card.RenderCount != drawnBefore[index]).Count(), $"scrolling to {offset} drew cards again");
            AssertMatchesReference(factory, window, surface, $"the frame scrolled to {offset}");
        }
    }

    [TestMethod]
    public void AChangeUnderAMovedFrame_RepaintsWhereTheVisualStandsNow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var (window, scroll, cards) = CreateList();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frame(window, surface);

        scroll.SetScrollOffsets(0, 230);
        Frame(window, surface);

        // A card the scroll carried into view changes: the repaint lands where the card stands now, not where
        // it was staged, and the card is drawn where the reference draws it.
        var shownCard = cards.First(card => card.Bounds.Y >= 0 && card.Bounds.Bottom <= HEIGHT);
        shownCard.Fill = Color.FromArgb(255, 240, 120, 60);
        Frame(window, surface);
        var dirty = window.LastRetainedDirtyRect;
        Assert.IsNotNull(dirty, "a change under a scrolled frame repainted the whole window");
        Assert.IsTrue(dirty.Value.Contains(shownCard.Bounds), $"the repaint {dirty} misses the card at {shownCard.Bounds}");
        Assert.IsTrue(dirty.Value.Height < HEIGHT, $"the repaint {dirty} covers the whole viewport for one card");
        AssertMatchesReference(factory, window, surface, "a card changed after a scroll");

        // A card scrolled out of view changes while hidden, comes back with the next scroll and changes again.
        var hiddenCard = cards[0];
        Assert.IsTrue(hiddenCard.Bounds.Bottom <= 0, "the first card is still in view");
        hiddenCard.Fill = Color.FromArgb(255, 60, 160, 90);
        Frame(window, surface);
        AssertMatchesReference(factory, window, surface, "a hidden card changed");

        scroll.SetScrollOffsets(0, 0);
        Frame(window, surface);
        AssertMatchesReference(factory, window, surface, "the changed card scrolled back into view");

        hiddenCard.Fill = Color.FromArgb(255, 90, 60, 160);
        Frame(window, surface);
        AssertMatchesReference(factory, window, surface, "the card changed again once back in view");
    }

    [TestMethod]
    public void AFractionalScroll_StillMatchesTheReference()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var (window, scroll, _) = CreateList();
        window.UseLayoutRounding = false;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frame(window, surface);

        // Without layout rounding the content lands between pixels, so the frame cannot be moved and every
        // node under it is visited, as before frames; the result is the same frame either way.
        foreach (double offset in new[] { 3.37, 9.71, 230.4 })
        {
            scroll.SetScrollOffsets(0, offset);
            Frame(window, surface);
            AssertMatchesReference(factory, window, surface, $"the frame scrolled to {offset} without layout rounding");
        }
    }

    [TestMethod]
    public void AScrollViewerInsideAScrolledFrame_MovesWithItAndScrollsOnItsOwn()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var innerCards = new Card[12];
        var innerContent = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < innerCards.Length; index++)
        {
            innerCards[index] = new Card { Height = 24, Padding = new Thickness(4), Content = new TextBlock { Text = "Inner " + index } };
            innerContent.Children(innerCards[index]);
        }

        var inner = new ScrollViewer { Height = 70, VerticalScroll = ScrollMode.Visible, Content = innerContent };
        var outerContent = new StackPanel { Orientation = Orientation.Vertical };
        var outerCards = new Card[10];
        for (int index = 0; index < outerCards.Length; index++)
        {
            outerCards[index] = new Card { Height = CARD_HEIGHT, Padding = new Thickness(6), Content = new TextBlock { Text = "Outer " + index } };
            outerContent.Children(outerCards[index]);
            if (index == 2)
            {
                outerContent.Children(inner);
            }
        }

        var outer = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = outerContent };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = outer;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frame(window, surface);

        var cards = outerCards.Concat(innerCards).ToArray();
        foreach (var (outerOffset, innerOffset) in new[] { (30.0, 0.0), (30.0, 25.0), (75.0, 25.0), (75.0, 100.0), (0.0, 100.0), (120.0, 0.0) })
        {
            int[] drawnBefore = cards.Select(card => card.RenderCount).ToArray();
            outer.SetScrollOffsets(0, outerOffset);
            inner.SetScrollOffsets(0, innerOffset);
            Frame(window, surface);
            Assert.AreEqual(0, cards.Where((card, index) => card.RenderCount != drawnBefore[index]).Count(), $"scrolling to {outerOffset}/{innerOffset} drew cards again");
            AssertMatchesReference(factory, window, surface, $"the frame scrolled to {outerOffset} outside and {innerOffset} inside");
        }

        // A change in the inner list after both scrolls is drawn where the inner list stands now.
        var shownInner = innerCards.First(card => card.Bounds.Y >= inner.Bounds.Y && card.Bounds.Bottom <= inner.Bounds.Bottom && card.Bounds.Y >= 0 && card.Bounds.Bottom <= HEIGHT);
        shownInner.Fill = Color.FromArgb(255, 240, 120, 60);
        Frame(window, surface);
        AssertMatchesReference(factory, window, surface, "an inner card changed after both scrolls");
    }

    private static (Window Window, ScrollViewer Scroll, Card[] Cards) CreateList()
    {
        var cards = new Card[CARD_COUNT];
        var content = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < cards.Length; index++)
        {
            cards[index] = new Card
            {
                Height = CARD_HEIGHT,
                Padding = new Thickness(6),
                Content = new TextBlock { Text = "Card " + index },
            };
            content.Children(cards[index]);
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = content };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = scroll;
        return (window, scroll, cards);
    }

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface surface, string what)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int differing = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
                int pixel = offset / 4;
                minX = Math.Min(minX, pixel % WIDTH);
                maxX = Math.Max(maxX, pixel % WIDTH);
                minY = Math.Min(minY, pixel / WIDTH);
                maxY = Math.Max(maxY, pixel / WIDTH);
            }
        }

        Assert.AreEqual(
            0,
            differing,
            $"{what}: {differing} pixels differ from a frame drawn straight from the visuals, inside ({minX},{minY})-({maxX},{maxY}); dirty region {string.Join(" ", window.LastRetainedDirtyRects)}");
    }
}
