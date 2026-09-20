using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Retained;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// What changed about a visual is said where it changes, and it is queued on the surface that draws the
/// visual. The scroll case carries over the first retained implementation's criterion: scrolled content
/// reports that it moved and does not report that its drawing changed.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedDirtyOwnershipTests
{
    private sealed class Leaf : Control
    {
        protected override Size MeasureContent(Size availableSize) => new(40, 24);

        protected override void OnRender(IGraphicsContext context)
            => context.FillRectangle(Bounds, Color.FromArgb(255, 200, 60, 60));
    }

    [TestMethod]
    public void InvalidateVisual_QueuesContentOnTheVisualAlone()
    {
        var (window, stack, leaves) = CreateWindow();

        leaves[1].InvalidateVisual();

        var queue = window.RenderDirtyQueue;
        Assert.IsTrue(queue.Has(leaves[1], RenderDirtyKind.Content), "the visual that asked is not queued");
        Assert.IsFalse(queue.Has(stack, RenderDirtyKind.Content), "the parent was queued as if its own drawing changed");
        Assert.IsFalse(queue.Has(leaves[0], RenderDirtyKind.Content), "a sibling was queued");
    }

    [TestMethod]
    public void OpacityChange_QueuesStateAndNoContent()
    {
        var (window, _, leaves) = CreateWindow();

        leaves[0].Opacity = 0.4;

        var queue = window.RenderDirtyQueue;
        Assert.IsTrue(queue.Has(leaves[0], RenderDirtyKind.State), "the opacity change is not queued as state");
        Assert.IsFalse(queue.Has(leaves[0], RenderDirtyKind.Content), "an opacity change claimed the drawing changed");
    }

    [TestMethod]
    public void ChildAdded_QueuesCompositionOnTheParent()
    {
        var (window, stack, _) = CreateWindow();

        stack.Children(new Leaf { Height = 24 });

        Assert.IsTrue(
            window.RenderDirtyQueue.Has(stack, RenderDirtyKind.Composition),
            "adding a child did not queue the parent's composition");
    }

    [TestMethod]
    public void Scroll_QueuesPlacementOnTheContentAndNoContent()
    {
        var leaves = new Leaf[8];
        var content = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < leaves.Length; index++)
        {
            leaves[index] = new Leaf { Height = 30 };
            content.Children(leaves[index]);
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = content };
        var window = HeadlessWindow.Create(120, 96);
        window.Content = scroll;
        Settle(window);

        scroll.SetScrollOffsets(0, 20);
        window.PerformLayout();

        var queue = window.RenderDirtyQueue;
        Assert.IsTrue(queue.Has(content, RenderDirtyKind.Placement), "the scrolled content did not report that it moved");
        Assert.IsFalse(queue.Has(content, RenderDirtyKind.Content), "the scrolled content claimed its drawing changed");
        Assert.IsFalse(queue.Has(scroll, RenderDirtyKind.Content), "the scroll viewer claimed its own drawing changed");
        Assert.IsFalse(queue.Has(leaves[3], RenderDirtyKind.Content), "a scrolled leaf claimed its drawing changed");
    }

    [TestMethod]
    public void PixelsOfABitmapChanged_QueuesTheImageThatShowsIt()
    {
        // A resource that changes under a visual is reported by the visual that draws with it, as a change
        // of its own drawing: nothing else in the tree knows the bitmap.
        var bitmap = new WriteableBitmap(24, 16, clear: true, hasAlpha: false);
        var image = new Image { Source = bitmap, Width = 24, Height = 16 };
        var other = new Leaf { Height = 24 };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(image, other);
        var window = HeadlessWindow.Create(160, 120);
        window.Content = stack;
        Settle(window);

        bitmap.Clear(Color.FromArgb(255, 220, 60, 40));

        var queue = window.RenderDirtyQueue;
        Assert.IsTrue(queue.Has(image, RenderDirtyKind.Content), "the image showing the bitmap is not queued");
        Assert.IsFalse(queue.Has(other, RenderDirtyKind.Content), "a visual that does not show the bitmap was queued");
        Assert.IsFalse(queue.Has(stack, RenderDirtyKind.Content), "the parent was queued as if its own drawing changed");
    }

    /// <summary>Runs the passes a new window needs before it is still, then empties the queue.</summary>
    private static void Settle(Window window)
    {
        // Styles and visual states are applied by the first update pass and ask for a repaint of their own.
        window.PerformLayout();
        window.PerformLayout();
        window.RenderDirtyQueue.Clear();
    }

    private static (Window Window, StackPanel Stack, Leaf[] Leaves) CreateWindow()
    {
        var leaves = new[] { new Leaf { Height = 24 }, new Leaf { Height = 24 }, new Leaf { Height = 24 } };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(leaves);
        var window = HeadlessWindow.Create(160, 120);
        window.Content = stack;
        Settle(window);
        return (window, stack, leaves);
    }
}
