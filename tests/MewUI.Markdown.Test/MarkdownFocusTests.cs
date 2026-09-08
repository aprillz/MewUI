using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace Aprillz.MewUI.Markdown.Test;

/// <summary>Focus, Tab traversal, element recycling, asynchronous image growth and DPI changes under virtualization.</summary>
[TestClass]
[DoNotParallelize]
public sealed class MarkdownFocusTests
{
    private const double WIDTH = 420;
    private const double HEIGHT = 300;

    [TestMethod]
    public void FocusedBlockStaysRealizedWhenScrolledOutOfTheWindow()
    {
        EnsureGdi();
        var (window, viewer, host, scroll) = Host(MarkdownVirtualizationTests.VariedDocument(600));
        var paragraph = LinkParagraphs(host).First();
        int topIndex = viewer.Document.TextUnits[paragraph.TextUnit].TopIndex;
        window.FocusManager.SetFocus(paragraph);
        Assert.AreSame(paragraph, window.FocusManager.FocusedElement);

        scroll.SetScrollOffsets(0, host.GetBlockTop(400));
        Relayout(window, host);

        Assert.IsNull(host.GetRealized(0), "blocks near the top must have been unrealized");
        Assert.AreSame(paragraph, host.GetRealized(topIndex), "the focused block must survive outside the realized window");
        Assert.AreSame(paragraph, window.FocusManager.FocusedElement);
    }

    [TestMethod]
    public void TabMovesToTheNextLinkBlockEvenWhenItIsNotRealized()
    {
        EnsureGdi();
        var (window, viewer, host, scroll) = Host(MarkdownVirtualizationTests.VariedDocument(600));
        var last = LinkParagraphs(host).Last();
        window.FocusManager.SetFocus(last);
        int expectedUnit = Enumerable.Range(last.TextUnit + 1, viewer.Document.TextUnits.Count - last.TextUnit - 1)
            .First(unit => viewer.Document.TextUnits[unit].HasLinks);
        Assert.IsFalse(host.GetRealized(viewer.Document.TextUnits[expectedUnit].TopIndex) != null, "the target must start unrealized for this test to mean anything");

        WindowInputRouter.KeyDown(window, new KeyEventArgs(Key.Tab, 0));
        Relayout(window, host);
        Relayout(window, host);

        var focused = window.FocusManager.FocusedElement as MarkdownParagraph;
        Assert.IsNotNull(focused);
        Assert.AreEqual(expectedUnit, focused.TextUnit);
        Assert.AreEqual(0, focused.FocusedLink);
        Assert.IsTrue(scroll.VerticalOffset > 0, "the viewer must have scrolled to the target block");
    }

    [TestMethod]
    public void ShiftTabEntersThePreviousBlockAtItsLastLink()
    {
        EnsureGdi();
        var (window, viewer, host, _) = Host("[first](a) and [second](b)\n\nplain\n\n[third](c)");
        var paragraphs = LinkParagraphs(host);
        window.FocusManager.SetFocus(paragraphs[1]);

        WindowInputRouter.KeyDown(window, new KeyEventArgs(Key.Tab, 0, ModifierKeys.Shift));

        Assert.AreSame(paragraphs[0], window.FocusManager.FocusedElement);
        Assert.AreEqual(1, paragraphs[0].FocusedLink, "backward traversal lands on the last link of the previous block");
    }

    [TestMethod]
    public void ScrollingBackReusesTheCachedElement()
    {
        EnsureGdi();
        var (window, _, host, scroll) = Host(MarkdownVirtualizationTests.VariedDocument(600));
        var first = host.GetRealized(0);
        Assert.IsNotNull(first);

        scroll.SetScrollOffsets(0, host.GetBlockTop(300));
        Relayout(window, host);
        Assert.IsNull(host.GetRealized(0));
        Assert.IsTrue(host.CachedCount > 0);

        scroll.SetScrollOffsets(0, 0);
        Relayout(window, host);

        Assert.AreSame(first, host.GetRealized(0), "the element that left the window must come back from the cache");
    }

    [TestMethod]
    public void ImageLoadingAboveTheViewportKeepsTheAnchorBlockInPlace()
    {
        EnsureGdi();
        var pending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new PendingResolver(pending);
        var synchronization = new QueuedSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(synchronization);
        try
        {
            var document = string.Concat(Enumerable.Range(0, 60).Select(index =>
                index >= 20 && index < 30 ? $"![picture {index}](picture{index}.png)\n\n" : $"Text block {index} with a few words.\n\n"));
            var viewer = new MarkdownViewer { Markdown = document, ImageResolver = resolver };
            var window = HeadlessWindow.Create(WIDTH, HEIGHT);
            window.Content = viewer;
            window.PerformLayout();
            var host = viewer.BlockHost!;
            var scroll = (ScrollViewer)viewer.DocumentRoot!;

            scroll.SetScrollOffsets(0, host.GetBlockTop(33));
            Relayout(window, host);
            int anchorIndex = host.IndexAt(scroll.VerticalOffset);
            var anchor = host.GetRealized(anchorIndex)!;
            double anchorTop = anchor.Bounds.Y;
            Assert.IsTrue(host.GetRealized(29) != null, "an image block above the anchor must be realized so its growth exercises the correction");
            double imageHeightBefore = host.GetRealized(29)!.DesiredSize.Height;

            pending.SetResult(new MarkdownImageLease(new LargeImageSource(), null));
            SpinWait.SpinUntil(() => synchronization.Count > 0, TimeSpan.FromSeconds(2));
            synchronization.RunAll();
            window.PerformLayout();
            Relayout(window, host);

            Assert.IsTrue(host.GetRealized(29)!.DesiredSize.Height > imageHeightBefore + 50, "the image block must have grown");
            Assert.AreEqual(anchorTop, host.GetRealized(anchorIndex)!.Bounds.Y, 1.0, "the block at the viewport top must not move when blocks above it grow");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [TestMethod]
    public void DpiChangeKeepsTheLayoutConsistent()
    {
        EnsureGdi();
        var (window, _, host, scroll) = Host(MarkdownVirtualizationTests.VariedDocument(600));
        scroll.SetScrollOffsets(0, host.GetBlockTop(300));
        Relayout(window, host);
        int anchor = host.IndexAt(scroll.VerticalOffset);

        window.SetDpi(144);
        window.PerformLayout();
        Relayout(window, host);

        Assert.IsTrue(Math.Abs(host.IndexAt(scroll.VerticalOffset) - anchor) <= 1, "the same block stays at the viewport top across the DPI change");
        AssertCovered(scroll, host, "after DPI change");
    }

    [TestMethod]
    public void WheelInputRoutedThroughTheWindowRealizesTheBlocksItReveals()
    {
        EnsureGdi();
        var (window, _, host, scroll) = Host(MarkdownVirtualizationTests.VariedDocument(600));
        var inside = new Point(scroll.Bounds.X + 100, scroll.Bounds.Y + 100);
        for (int notch = 0; notch < 40; notch++)
        {
            WindowInputRouter.MouseWheel(window, inside, inside, new Vector(0, -120));
            window.PerformLayout();
            Relayout(window, host);
        }

        Assert.IsTrue(scroll.VerticalOffset > 1000, $"offset={scroll.VerticalOffset}");
        Assert.IsNull(host.GetRealized(0), "the first block must have left the realized window");
        AssertCovered(scroll, host, "after wheel input");
    }

    [TestMethod]
    public void ClickingALinkAfterScrollingRaisesTheRequestForThatLink()
    {
        EnsureGdi();
        var (window, viewer, host, scroll) = Host(MarkdownVirtualizationTests.VariedDocument(600));
        scroll.SetScrollOffsets(0, host.GetBlockTop(300));
        Relayout(window, host);
        string? requested = null;
        viewer.LinkRequested += args => requested = args.Url;
        var paragraph = LinkParagraphs(host).First(candidate => candidate.Bounds.Y >= scroll.Bounds.Y && candidate.Bounds.Bottom <= scroll.Bounds.Bottom);
        var layout = paragraph.GetLayout(paragraph.Bounds.Width);
        int linkStart = paragraph.Text.IndexOf("links", StringComparison.Ordinal);
        var glyphs = new List<Rect>();
        layout.GetRangeBounds(linkStart, 2, glyphs);
        var point = new Point(paragraph.Bounds.X + glyphs[0].X + 2, paragraph.Bounds.Y + glyphs[0].Y + glyphs[0].Height / 2);

        WindowInputRouter.MouseButton(window, point, point, MouseButton.Left, isDown: true, leftDown: true, rightDown: false, middleDown: false, clickCount: 1, ModifierKeys.None, PointerType.Mouse);
        WindowInputRouter.MouseButton(window, point, point, MouseButton.Left, isDown: false, leftDown: false, rightDown: false, middleDown: false, clickCount: 1, ModifierKeys.None, PointerType.Mouse);

        int blockIndex = int.Parse(paragraph.Text.Split(' ')[1]);
        Assert.AreEqual($"https://example.com/{blockIndex}", requested, "the click must resolve to the link of the block under the pointer after virtualization moved it");
        Assert.IsFalse(viewer.HasSelection, "a link click must not start a text selection");
    }

    private static (Window Window, MarkdownViewer Viewer, MarkdownBlockHost Host, ScrollViewer Scroll) Host(string markdown)
    {
        var viewer = new MarkdownViewer { Markdown = markdown };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = viewer;
        window.PerformLayout();
        return (window, viewer, viewer.BlockHost!, (ScrollViewer)viewer.DocumentRoot!);
    }

    // Stands in for the measure pass the host requests through the dispatcher in a running app.
    private static void Relayout(Window window, MarkdownBlockHost host)
    {
        host.InvalidateMeasure();
        window.PerformLayout();
    }

    private static MarkdownParagraph[] LinkParagraphs(MarkdownBlockHost host) =>
        Descendants(host).OfType<MarkdownParagraph>().Where(paragraph => paragraph.TextUnit >= 0 && paragraph.LinkCount > 0)
            .OrderBy(paragraph => paragraph.TextUnit).ToArray();

    private static void AssertCovered(ScrollViewer scroll, MarkdownBlockHost host, string phase)
    {
        double viewportTop = scroll.Bounds.Y + scroll.Padding.Top;
        double viewportBottom = viewportTop + scroll.ViewportHeight;
        double coveredTop = double.PositiveInfinity, coveredBottom = double.NegativeInfinity;
        for (int index = 0; index < host.BlockCount; index++)
        {
            var element = host.GetRealized(index);
            if (element == null)
            {
                continue;
            }
            coveredTop = Math.Min(coveredTop, element.Bounds.Y);
            coveredBottom = Math.Max(coveredBottom, element.Bounds.Bottom);
        }
        Assert.IsTrue(coveredTop <= viewportTop + 1 && coveredBottom >= viewportBottom - 1, $"{phase}: covered [{coveredTop - viewportTop:F1}, {coveredBottom - viewportTop:F1}] of viewport [0, {scroll.ViewportHeight:F0}]");
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        yield return root;
        if (root is IVisualTreeHost host)
        {
            var children = new List<Element>();
            host.VisitChildren(child => { children.Add(child); return true; });
            foreach (var child in children)
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
    }

    private sealed class PendingResolver(TaskCompletionSource<MarkdownImageLease?> pending) : IMarkdownImageResolver
    {
        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken) => new(pending.Task);
    }

    private sealed class LargeImageSource : IImageSource
    {
        public IImage CreateImage(IGraphicsFactory factory) => new LargeImage();
    }

    private sealed class LargeImage : IImage
    {
        public int PixelWidth => 300;
        public int PixelHeight => 150;
        public void Dispose() { }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly object _gate = new();

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_gate)
            {
                _queue.Enqueue((callback, state));
            }
        }

        public void RunAll()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }
                    item = _queue.Dequeue();
                }
                item.Callback(item.State);
            }
        }
    }
}
