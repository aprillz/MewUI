using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A text box draws its layers in order: backgrounds, selection, glyphs, caret. A blink or a caret
/// move changes the layers around the glyphs, so the glyphs stay recorded and the box records only
/// the layers that changed.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MultiLineTextBoxLayerTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 240;

    // An edge the repaint pads for antialiasing, in DIPs.
    private const double EDGE_SLACK = 2;

    [TestMethod]
    public void ACaretBlink_RecordsOnlyTheCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var (window, box, surface) = Show(factory);
        using (surface)
        {
            var caret = box.GetCharRectInWindow(box.CaretPosition);
            int boxVersion = box.RenderContentVersion;
            typeof(TextBase).GetMethod("OnCaretBlink", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(box, null);
            Render(window, surface);

            Assert.AreEqual(boxVersion, box.RenderContentVersion, "a caret blink recorded the whole box again");
            var dirty = window.LastRetainedDirtyRect;
            Assert.IsTrue(
                dirty is Rect area && area.Y >= caret.Y - EDGE_SLACK && area.Bottom <= caret.Bottom + EDGE_SLACK &&
                    area.X >= caret.X - EDGE_SLACK && area.Right <= caret.Right + EDGE_SLACK,
                $"the caret at {caret} blinked and the frame repainted {dirty?.ToString() ?? "everything"} ({window.LastWholeFrameReason})");
            AssertMatchesReference(factory, window, surface);
        }
    }

    [TestMethod]
    public void MovingTheCaret_LeavesTheGlyphsRecorded()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var (window, box, surface) = Show(factory);
        using (surface)
        {
            var target = box.GetCharRectInWindow(LineStart(box, 4) + 6);
            var glyphs = LayerVisual(box, TextViewLayerAnchor.Text);
            int glyphsVersion = glyphs.RenderContentVersion;
            window.SendClick(new Point(target.X, target.Y + (target.Height / 2)));
            Render(window, surface);

            // The box itself may record its frame again: the pointer arriving changes the border's colour.
            Assert.AreEqual(LineStart(box, 4) + 6, box.CaretPosition, "the click did not move the caret");
            Assert.AreEqual(glyphsVersion, glyphs.RenderContentVersion, "moving the caret recorded the glyphs again");
            AssertMatchesReference(factory, window, surface);
        }
    }

    private static (Window Window, MultiLineTextBox Box, IRenderSurface Surface) Show(GdiGraphicsFactory factory)
    {
        var box = new MultiLineTextBox
        {
            Text = string.Join("\n", Enumerable.Range(0, 10).Select(line => $"line {line}: the quick brown fox jumps")),
            Wrap = false,
        };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = box;
        window.PerformLayout();
        window.FocusManager.SetFocus(box);
        box.CaretPosition = LineStart(box, 2) + 4;
        var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            Render(window, surface);
        }

        return (window, box, surface);
    }

    private static TextViewLayerVisual LayerVisual(MultiLineTextBox box, TextViewLayerAnchor anchor)
    {
        TextViewLayerVisual? found = null;
        ((IVisualTreeHost)box).VisitChildren(child =>
        {
            if (child is TextViewLayerVisual visual && visual.Anchor == anchor)
            {
                found = visual;
            }

            return true;
        });

        return found ?? throw new AssertFailedException($"the box has no {anchor} layer visual");
    }

    private static int LineStart(MultiLineTextBox box, int line)
    {
        int offset = 0;
        var text = box.Text;
        for (int index = 0; index < line; index++)
        {
            offset = text.IndexOf('\n', offset) + 1;
        }

        return offset;
    }

    private static void Render(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface surface)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        TestBackendSession.AssertSurfacesEqual(reference, surface, WIDTH, 0, "a frame drawn straight from the visuals");
    }
}
