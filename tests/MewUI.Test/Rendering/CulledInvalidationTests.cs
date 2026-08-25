using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A repaint asked for by an element the last render pass culled reaches no pixels, so it must not wake
/// the window. The clock behind such a repaint keeps running: only the invalidation is dropped.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CulledInvalidationTests
{
    private sealed class Context : NoOpGraphicsContext
    {
        public override double DpiScale => 1;
    }

    private sealed class CountingWindow : Window
    {
        public int RenderRequests { get; private set; }

        public override void InvalidateVisual() => RenderRequests++;
    }

    [TestCleanup]
    public void Cleanup() => AnimationManager.Reset();

    /// <summary>Lays out a window whose child sits at <paramref name="childTop"/> and renders one frame.</summary>
    private static (CountingWindow Window, Border Child) RenderedFrame(double childTop)
    {
        var child = new Border { Width = 40, Height = 20 };
        var host = new Canvas();
        host.Add(child);
        Canvas.SetTop(child, childTop);

        var window = new CountingWindow { Content = host };
        host.Measure(new Size(100, 100));
        host.Arrange(new Rect(0, 0, 100, 100));

        var previous = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = new Rect(0, 0, 100, 100);
        try
        {
            host.Render(new Context());
        }
        finally
        {
            UIElement.RenderCullViewport = previous;
        }

        return (window, child);
    }

    [TestMethod]
    public void CulledElement_DoesNotWakeItsWindow()
    {
        var (window, child) = RenderedFrame(childTop: 400);
        int before = window.RenderRequests;

        child.InvalidateVisual();

        Assert.AreEqual(before, window.RenderRequests, "an element outside the viewport paints nothing");
    }

    [TestMethod]
    public void ElementInsideTheViewport_WakesItsWindow()
    {
        var (window, child) = RenderedFrame(childTop: 10);
        int before = window.RenderRequests;

        child.InvalidateVisual();

        Assert.IsGreaterThan(before, window.RenderRequests);
    }

    [TestMethod]
    public void ElementDrawnAgain_WakesItsWindowOnceMore()
    {
        var (window, child) = RenderedFrame(childTop: 400);
        child.InvalidateVisual();
        int afterCulledRepaint = window.RenderRequests;

        // A scroll or a layout pass brings it back: the next frame draws it, which clears the mark.
        Canvas.SetTop(child, 10);
        ((Canvas)child.Parent!).Arrange(new Rect(0, 0, 100, 100));
        var previous = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = new Rect(0, 0, 100, 100);
        try
        {
            ((Canvas)child.Parent!).Render(new Context());
        }
        finally
        {
            UIElement.RenderCullViewport = previous;
        }

        child.InvalidateVisual();

        Assert.IsGreaterThan(afterCulledRepaint, window.RenderRequests);
    }

    [TestMethod]
    public void SkipViewportCullElement_KeepsWakingItsWindow()
    {
        var (window, child) = RenderedFrame(childTop: 400);

        // The property is read live, so an element that opts out after being marked can ask again.
        child.SkipViewportCull = true;
        int before = window.RenderRequests;

        child.InvalidateVisual();

        Assert.IsGreaterThan(before, window.RenderRequests);
    }

    [TestMethod]
    public void HiddenPartOfACachedElement_DoesNotWakeItsWindow()
    {
        // The card straddles the bottom edge, so the card itself is drawn while the child inside its
        // lower half never reaches the screen. A cache snapshot draws the whole card, which must not be
        // read as the child being visible.
        var child = new Border { Width = 40, Height = 20 };
        var inside = new StackPanel();
        inside.Add(new Border { Width = 40, Height = 45 });
        inside.Add(child);

        var card = new Border
        {
            Width = 80,
            Height = 60,
            CacheMode = new BitmapCache(),
            Child = inside,
        };

        var host = new Canvas();
        host.Add(card);
        Canvas.SetTop(card, 70);

        var window = new CountingWindow { Content = host };
        host.Measure(new Size(100, 100));
        host.Arrange(new Rect(0, 0, 100, 100));

        var previous = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = new Rect(0, 0, 100, 100);
        try
        {
            host.Render(new Context());
        }
        finally
        {
            UIElement.RenderCullViewport = previous;
        }

        Assert.IsGreaterThan(100, child.Bounds.Top, "the child has to sit below the viewport for this case");
        int before = window.RenderRequests;

        child.InvalidateVisual();

        Assert.AreEqual(before, window.RenderRequests);
    }

    [TestMethod]
    public void CulledOwner_DoesNotStopItsClock()
    {
        var (_, child) = RenderedFrame(childTop: 400);

        int ticks = 0;
        bool completed = false;
        var clock = new AnimationClock(TimeSpan.FromMilliseconds(10)).AttachTo(child);
        clock.TickCallback = _ =>
        {
            ticks++;
            child.InvalidateVisual();
        };
        clock.CompletedCallback = () => completed = true;

        clock.Start();
        AnimationManager.Instance.UpdateAt(Stopwatch.GetTimestamp());
        AnimationManager.Instance.UpdateAt(Stopwatch.GetTimestamp() + Stopwatch.Frequency);

        Assert.IsGreaterThan(0, ticks, "a hidden element's animation still advances");
        Assert.IsTrue(completed, "a finite animation still reaches its completion callback");
    }
}
