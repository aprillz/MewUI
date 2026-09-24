using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A text view records its layers apart and repaints only the ones a change reaches. Whatever the edit,
/// the frame that results has to be the one drawn straight from the visuals: a layer left recorded
/// while it should have changed shows as a difference here.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MultiLineTextBoxLayerParityTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 200;

    public enum Edit
    {
        Typing,
        DragSelectionRecolored,
        Composition,
        WheelScroll,
        PlaceholderFocus,
        ForegroundChange,
        LayerInsertedAfterRender,
        SyntaxViewerSelection,
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void EveryEdit_LeavesTheFrameEqualToTheVisuals(Edit edit, TestBackend backend, double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        int pixelWidth = (int)Math.Round(WIDTH * scale);
        int pixelHeight = (int)Math.Round(HEIGHT * scale);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.SetDpi((uint)Math.Round(96 * scale));

        string text = string.Join("\n", Enumerable.Range(0, 30).Select(line => $"line {line}: the quick brown fox jumps"));
        UIElement content;
        MultiLineTextBox? box = null;
        SyntaxViewer? viewer = null;
        if (edit == Edit.SyntaxViewerSelection)
        {
            viewer = new SyntaxViewer { Text = text };
            content = viewer;
        }
        else
        {
            box = new MultiLineTextBox
            {
                Text = edit == Edit.PlaceholderFocus ? string.Empty : text,
                Placeholder = "Type here",
                Wrap = false,
                SelectionForeground = edit == Edit.DragSelectionRecolored ? Color.FromArgb(255, 200, 30, 30) : null,
            };
            content = box;
        }

        var other = new Button { Content = new TextBlock { Text = "other" } };
        var panel = new DockPanel();
        panel.Children(other.DockBottom(), content);
        window.Content = panel;
        window.PerformLayout();
        if (box != null && edit != Edit.PlaceholderFocus)
        {
            window.FocusManager.SetFocus(box);
            box.CaretPosition = 12;
        }

        using var surface = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, scale, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            Frame(window, surface);
        }

        var viewport = box?.TextViewportBounds ?? viewer!.Bounds;
        switch (edit)
        {
            case Edit.Typing:
                ((ITextInputClient)box!).HandleTextInput(new TextInputEventArgs("typed"));
                Frame(window, surface);
                window.SendKeyPress(Key.Enter);
                Frame(window, surface);
                window.SendKeyPress(Key.Backspace);
                break;
            case Edit.DragSelectionRecolored:
                var from = new Point(viewport.X + 20, viewport.Y + 8);
                window.SendMouseMove(from);
                window.SendMouseDown(from);
                Frame(window, surface);
                window.SendMouseDrag(new Point(viewport.X + 140, viewport.Y + 50));
                Frame(window, surface);
                window.SendMouseDrag(new Point(viewport.X + 90, viewport.Y + 30));
                Frame(window, surface);
                window.SendMouseUp(new Point(viewport.X + 90, viewport.Y + 30));
                break;
            case Edit.Composition:
                var client = (ITextCompositionClient)box!;
                client.HandleTextCompositionStart(new TextCompositionEventArgs());
                client.HandleTextCompositionUpdate(new TextCompositionEventArgs("ㅎ", [CompositionAttr.Input]));
                Frame(window, surface);
                AssertEqualsReference(session, window, surface, pixelWidth, scale, "while composing");
                client.HandleTextCompositionUpdate(new TextCompositionEventArgs("한", [CompositionAttr.Converted]));
                Frame(window, surface);
                AssertEqualsReference(session, window, surface, pixelWidth, scale, "after the composition changed");
                client.HandleTextCompositionEnd(new TextCompositionEventArgs());
                break;
            case Edit.WheelScroll:
                window.SendMouseWheel(new Point(viewport.X + 40, viewport.Y + 40), -3);
                break;
            case Edit.PlaceholderFocus:
                window.FocusManager.SetFocus(box!);
                Frame(window, surface);
                AssertEqualsReference(session, window, surface, pixelWidth, scale, "after the focus came in");
                ((ITextInputClient)box!).HandleTextInput(new TextInputEventArgs("x"));
                Frame(window, surface);
                AssertEqualsReference(session, window, surface, pixelWidth, scale, "after the first character");
                box!.Text = string.Empty;
                window.FocusManager.SetFocus(other);
                break;
            case Edit.ForegroundChange:
                box!.Foreground = Color.FromArgb(255, 30, 120, 60);
                break;
            case Edit.LayerInsertedAfterRender:
                box!.InsertLayer(new Band(), TextViewLayerAnchor.Text, TextLayerPosition.Below);
                break;
            case Edit.SyntaxViewerSelection:
                viewer!.Select(10, 40);
                Frame(window, surface);
                AssertEqualsReference(session, window, surface, pixelWidth, scale, "after the first selection");
                viewer.Select(60, 5);
                break;
        }

        Frame(window, surface);
        AssertEqualsReference(session, window, surface, pixelWidth, scale, $"after {edit}");
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var edit in Enum.GetValues<Edit>())
        {
            foreach (var backend in Enum.GetValues<TestBackend>())
            {
                yield return [edit, backend, 1.0];
                yield return [edit, backend, 1.5];
            }
        }
    }

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static void AssertEqualsReference(TestBackendSession session, Window window, IRenderSurface surface, int pixelWidth, double scale, string label)
    {
        int pixelHeight = (int)Math.Round(HEIGHT * scale);
        using var reference = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, scale, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        TestBackendSession.AssertSurfacesEqual(reference, surface, pixelWidth, session.ChannelTolerance, label);
    }

    /// <summary>A stripe under the glyphs, the kind of layer an extension inserts.</summary>
    private sealed class Band : ITextViewLayer
    {
        public void Draw(ITextRenderContext context, Rect viewportBounds)
            => context.Graphics.FillRectangle(new Rect(viewportBounds.X, viewportBounds.Y + 20, viewportBounds.Width, 14), Color.FromArgb(255, 250, 230, 160));
    }
}
