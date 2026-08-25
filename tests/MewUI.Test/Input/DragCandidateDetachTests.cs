using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Input;

/// <summary>
/// A drag candidate recorded on mouse-down outlives the element when a layout rebuild detaches it,
/// and the mouse-move that crosses the gesture threshold must drop it rather than measure against
/// a detached element (issue #214).
/// </summary>
[TestClass]
public sealed class DragCandidateDetachTests
{
    [TestMethod]
    public void MouseMove_AfterTheDragSourceIsDetached_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var panel = new StackPanel();
        var source = new Border { Width = 200, Height = 100, CanDrag = true };
        panel.Add(source);
        window.Content = panel;
        window.PerformLayout();

        var inside = new Point(50, 20);
        window.SendMouseMove(inside);
        window.SendMouseDown(inside);

        // What a dock layout rebuild does between the press and the move.
        panel.Clear();
        window.PerformLayout();

        // Past DragGestureThresholdDip (4), so the move promotes the recorded candidate.
        window.SendMouseDrag(new Point(inside.X + 40, inside.Y + 40));
    }

    [TestMethod]
    public void MouseMove_WithTheDragSourceStillAttached_StartsTheDrag()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var panel = new StackPanel();
        var source = new Border { Width = 200, Height = 100, CanDrag = true };
        panel.Add(source);
        window.Content = panel;
        window.PerformLayout();

        int dragStarting = 0;
        source.DragStarting += _ => dragStarting++;

        var inside = new Point(50, 20);
        window.SendMouseMove(inside);
        window.SendMouseDown(inside);
        window.SendMouseDrag(new Point(inside.X + 40, inside.Y + 40));

        Assert.AreEqual(1, dragStarting, "the guard swallowed a candidate whose source was still attached");
    }

    [TestMethod]
    public void MouseMove_AfterTheDragSourceMovesToAnotherWindow_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var other = HeadlessWindow.Create();
        var panel = new StackPanel();
        var source = new Border { Width = 200, Height = 100, CanDrag = true };
        panel.Add(source);
        window.Content = panel;
        window.PerformLayout();

        var inside = new Point(50, 20);
        window.SendMouseMove(inside);
        window.SendMouseDown(inside);

        // Reparenting keeps the element alive but roots it in a different visual tree.
        panel.Clear();
        other.Content = source;
        window.PerformLayout();
        other.PerformLayout();

        window.SendMouseDrag(new Point(inside.X + 40, inside.Y + 40));
    }
}
