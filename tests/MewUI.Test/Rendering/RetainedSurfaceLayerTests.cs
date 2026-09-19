using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Everything a surface shows belongs to one scene in one order: the body, then adorners, popups drawn
/// in the surface, and overlays. Each case changes something above the body while the surface keeps the
/// previous frame, and requires the result to equal a frame drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedSurfaceLayerTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 180;

    private sealed class Tile : Control
    {
        internal Color Fill { get; set; }

        internal double ShadowBlur { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(80, 60);

        protected override void OnRender(IGraphicsContext context)
        {
            if (ShadowBlur > 0)
            {
                // Ink that lies outside the layout bounds.
                context.DrawBoxShadow(Bounds, 4, ShadowBlur, Color.FromArgb(160, 0, 0, 0), 0, 3);
            }

            context.FillRectangle(Bounds, Fill);
        }
    }

    [TestMethod]
    public void SwappingTheOrderOfTwoOverlays_RedrawsThemInTheNewOrder()
    {
        if (!TryCreate(out var factory, out var window, out var surface))
        {
            return;
        }

        using (factory)
        using (surface)
        {
            var lower = Overlay(Color.FromArgb(255, 200, 40, 40), 40, 40);
            var upper = Overlay(Color.FromArgb(255, 40, 40, 200), 80, 70);
            window.OverlayLayer.Add(lower);
            window.OverlayLayer.Add(upper);
            Frames(window, surface, 2);

            // The same two visuals with the same bounds, only in the other order.
            window.OverlayLayer.Remove(lower);
            window.OverlayLayer.Add(lower);
            Frames(window, surface, 1);

            AssertMatchesReference(factory, window, surface, "after swapping the overlays");
        }
    }

    [TestMethod]
    public void MovingAnOverlayWithAShadow_LeavesNoInkBehind()
    {
        if (!TryCreate(out var factory, out var window, out var surface))
        {
            return;
        }

        using (factory)
        using (surface)
        {
            var tile = Overlay(Color.FromArgb(255, 40, 160, 90), 30, 30);
            tile.ShadowBlur = 12;
            window.OverlayLayer.Add(tile);
            Frames(window, surface, 2);

            tile.Margin = new Thickness(110, 80, 0, 0);
            Frames(window, surface, 1);
            AssertMatchesReference(factory, window, surface, "after moving the shadowed overlay");

            window.OverlayLayer.Remove(tile);
            Frames(window, surface, 1);
            AssertMatchesReference(factory, window, surface, "after removing the shadowed overlay");
        }
    }

    [TestMethod]
    public void PopupOpenedMovedAndClosed_LeavesNoTraceOfWhereItWas()
    {
        if (!TryCreate(out var factory, out var window, out var surface))
        {
            return;
        }

        using (factory)
        using (surface)
        {
            Frames(window, surface, 2);

            var popup = new Tile { Fill = Color.FromArgb(255, 230, 200, 40), Width = 90, Height = 60 };
            var owner = (UIElement)window.Content!;
            window.ShowPopup(owner, popup, _ => new Rect(20, 20, 90, 60), staysOpen: true);
            Frames(window, surface, 1);
            AssertMatchesReference(factory, window, surface, "after opening the popup");

            window.UpdatePopup(popup, new Rect(120, 90, 90, 60));
            Frames(window, surface, 1);
            AssertMatchesReference(factory, window, surface, "after moving the popup");

            window.ClosePopup(popup);
            Frames(window, surface, 1);
            AssertMatchesReference(factory, window, surface, "after closing the popup");
        }
    }

    [TestMethod]
    public void BodyChangeUnderAnOverlay_KeepsTheOverlayOnTop()
    {
        if (!TryCreate(out var factory, out var window, out var surface))
        {
            return;
        }

        using (factory)
        using (surface)
        {
            var overlay = Overlay(Color.FromArgb(200, 40, 40, 200), 30, 20);
            window.OverlayLayer.Add(overlay);
            Frames(window, surface, 2);

            // The body tile under the overlay changes; the overlay does not.
            var bodyTile = (Tile)((StackPanel)window.Content!)[0];
            bodyTile.Fill = Color.FromArgb(255, 250, 120, 20);
            bodyTile.InvalidateVisual();
            Frames(window, surface, 1);

            AssertMatchesReference(factory, window, surface, "after the body changed under the overlay");
        }
    }

    private static Tile Overlay(Color fill, double left, double top) => new()
    {
        Fill = fill,
        Width = 80,
        Height = 60,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(left, top, 0, 0),
    };

    private static bool TryCreate(out GdiGraphicsFactory factory, out Window window, out IRenderSurface surface)
    {
        factory = null!;
        window = null!;
        surface = null!;
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return false;
        }

        factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(
            new Tile { Fill = Color.FromArgb(255, 30, 120, 200), Height = 60 },
            new Tile { Fill = Color.FromArgb(255, 200, 120, 30), Height = 60 });
        window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = stack;
        window.PerformLayout();
        surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        return true;
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);

        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        int first = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] ||
                expected[offset + 1] != shown[offset + 1] ||
                expected[offset + 2] != shown[offset + 2])
            {
                differing++;
                if (first < 0)
                {
                    first = offset / 4;
                }
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ, first at ({first % WIDTH}, {first / WIDTH})");
    }
}
