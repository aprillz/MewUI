using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The arrows of a drop-down and of a numeric box are small visuals inside a control whose state
/// changes often. Every state is held against a frame drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedGlyphTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 200;

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    public void DropDownAndNumericArrows_MatchTheReferenceThroughStateChanges(double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var combo = new ComboBox().Items(new[] { "Alpha", "Beta", "Gamma" }).SelectedIndex(0);
        combo.Width = 160;
        var numeric = new NumericUpDown { Width = 160, Value = 5 };
        var disabledCombo = new ComboBox { Width = 160, IsEnabled = false };
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Margin = new Thickness(12) };
        stack.Children(combo, numeric, disabledCombo);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.SetDpi((uint)Math.Round(96 * scale));
        window.Content = stack;
        window.PerformLayout();

        int pixelWidth = (int)Math.Ceiling(WIDTH * scale);
        int pixelHeight = (int)Math.Ceiling(HEIGHT * scale);
        IRenderSurface Surface() => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, scale, hasAlpha: false));

        using var surface = Surface();
        void Check(string label)
        {
            for (int index = 0; index < 2; index++)
            {
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
            }

            using var reference = Surface();
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
                    int pixelX = (offset / 4) % pixelWidth;
                    int pixelY = (offset / 4) / pixelWidth;
                    minX = Math.Min(minX, pixelX); maxX = Math.Max(maxX, pixelX);
                    minY = Math.Min(minY, pixelY); maxY = Math.Max(maxY, pixelY);
                }
            }

            Assert.AreEqual(0, differing,
                $"{label} at scale {scale}: {differing} pixels differ from a frame drawn straight from the visuals, inside pixels ({minX},{minY})-({maxX},{maxY}); " +
                $"dirty rects {string.Join(" ", window.LastRetainedDirtyRects)}; numeric {numeric.Bounds}; combo {combo.Bounds}");
        }

        Check("first frames");

        // Pointer states repaint only part of the frame, which is where an arrow can be cut or left stale.
        var comboArrow = new Point(combo.Bounds.Right - 10, combo.Bounds.Y + combo.Bounds.Height / 2);
        var upArrow = new Point(numeric.Bounds.Right - 8, numeric.Bounds.Y + numeric.Bounds.Height * 0.25);
        var downArrow = new Point(numeric.Bounds.Right - 8, numeric.Bounds.Y + numeric.Bounds.Height * 0.75);
        var away = new Point(WIDTH - 4, HEIGHT - 4);

        window.SendMouseMove(comboArrow);
        Check("pointer over the drop-down arrow");
        window.SendMouseMove(upArrow);
        Check("pointer over the up arrow");
        window.SendMouseDown(upArrow);
        Check("up arrow pressed");
        window.SendMouseUp(upArrow);
        Check("up arrow released");
        window.SendMouseMove(downArrow);
        Check("pointer over the down arrow");
        window.SendMouseDown(downArrow);
        window.SendMouseUp(downArrow);
        Check("down arrow clicked");
        window.SendMouseMove(away);
        Check("pointer moved away");

        numeric.Value = 6;
        Check("after the value changed");

        combo.SelectedIndex = 2;
        Check("after the selection changed");

        combo.IsEnabled = false;
        numeric.IsEnabled = false;
        Check("after both were disabled");

        combo.IsEnabled = true;
        numeric.IsEnabled = true;
        combo.Width = 200;
        numeric.Width = 120;
        Check("after both were enabled and resized");

        stack.Margin = new Thickness(12, 30.5, 12, 12);
        Check("after both were moved by a fraction");
    }
    private sealed record Row(string Name, int Role);

    [TestMethod]
    public void DropDownAndNumericArrowsInsideAGridView_MatchTheReference()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var rows = new[] { new Row("Ann", 0), new Row("Bob", 1), new Row("Cy", 2) };
        var grid = new GridView()
            .ItemsSource(rows)
            .Columns(
                new GridViewColumn<Row>().Header("Name").Width(70).Text(row => row.Name),
                new GridViewColumn<Row>().Header("Role").Width(110).Template(
                    build: _ => new ComboBox().Items(new[] { "User", "Admin", "Guest" }).CenterVertical(),
                    bind: (view, row) => view.SelectedIndex = row.Role),
                new GridViewColumn<Row>().Header("Amount").Width(110).Template(
                    build: _ => new NumericUpDown().CenterVertical(),
                    bind: (view, row) => view.Value = row.Role));

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int index = 0; index < 3; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

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

        Assert.AreEqual(0, differing, $"{differing} pixels differ from a frame drawn straight from the visuals");
    }
}
