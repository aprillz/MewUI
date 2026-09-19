using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A toolbar and a transition host compose visuals of their own kinds: bands of groups of entries, and
/// one content at rest. Each state is held against a frame drawn straight from the visuals, and a
/// change of one entry must repaint that entry, not the control.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedHostControlTests
{
    private const int WIDTH = 480;
    private const int HEIGHT = 200;

    [TestMethod]
    public void ToolBar_MatchesTheReference_AndAHoverStaysLocal()
    {
        using var factory = Start();
        var bands = new[]
        {
            new ToolBarBand(new ToolBarGroup(Item("open"), Item("save"), Item("print")), new ToolBarGroup(Item("cut"), Item("copy"))),
            new ToolBarBand(new ToolBarGroup(Item("zoom"), Item("fit"))),
        };
        var bar = new ToolBar { VerticalAlignment = VerticalAlignment.Top };
        foreach (var band in bands)
        {
            bar.Bands.Add(band);
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = bar;
        window.PerformLayout();
        using var surface = Surface(factory);

        Check(factory, window, surface, "first frames");

        var firstEntry = (UIElement)bar.VisualsInternal[0].Groups[0].Entries[0];
        window.SendMouseMove(firstEntry.CenterOf());
        Check(factory, window, surface, "pointer over an entry");

        var damage = window.LastRetainedDamage;
        window.SendMouseMove(new Point(WIDTH - 4, HEIGHT - 4));
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        damage = window.LastRetainedDamage;
        Assert.IsTrue(
            damage is Rect left && left.Width > 0 && left.Width < bar.Bounds.Width / 2,
            $"leaving one entry repainted {damage} of a {bar.Bounds} toolbar ({window.LastWholeFrameReason})");
        Check(factory, window, surface, "pointer moved away");

        bar.CanReorderGroups = true;
        Check(factory, window, surface, "after grips were shown");
    }

    [TestMethod]
    public void TransitionHostAtRest_MatchesTheReference_AndAChangeInsideStaysLocal()
    {
        using var factory = Start();
        var first = new Button { Content = new TextBlock { Text = "First" }, Width = 100, Height = 28 };
        var second = new Button { Content = new TextBlock { Text = "Second" }, Width = 100, Height = 28 };
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 40 };
        stack.Children(first, second);
        var host = new TransitionContentControl { Transition = ContentTransition.CreateNone(), Content = stack };

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = host;
        window.PerformLayout();
        using var surface = Surface(factory);
        Check(factory, window, surface, "first frames");

        second.Background = Color.FromArgb(255, 200, 60, 60);
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        var damage = window.LastRetainedDamage;
        Assert.IsTrue(
            damage is Rect changed && changed.Height > 0 && changed.Height < 40,
            $"a change of one button repainted {damage} ({window.LastWholeFrameReason})");
        Check(factory, window, surface, "after one button changed");

        host.Content = new TextBlock { Text = "replaced" };
        Check(factory, window, surface, "after the content was replaced");
    }

    [TestMethod]
    public void Calendar_MatchesTheReferenceThroughItsStates()
    {
        using var factory = Start();
        var calendar = new Calendar
        {
            DisplayDate = new DateTime(2026, 3, 15),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var window = HeadlessWindow.Create(WIDTH, HEIGHT + 80);
        window.Content = calendar;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT + 80, 1.0, hasAlpha: false));

        void CheckCalendar(string label)
        {
            for (int index = 0; index < 2; index++)
            {
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
            }

            using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT + 80, 1.0, hasAlpha: false));
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

            Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from a frame drawn straight from the visuals");
        }

        CheckCalendar("first frames");

        window.SendMouseMove(new Point(calendar.Bounds.X + 60, calendar.Bounds.Y + 90));
        CheckCalendar("pointer over a day");

        calendar.SelectedDate = new DateTime(2026, 3, 20);
        CheckCalendar("after a day was selected");

        calendar.DisplayDate = new DateTime(2026, 4, 1);
        CheckCalendar("after the month changed");

        calendar.DisplayMode = CalendarMode.Year;
        CheckCalendar("in the year view");

        calendar.DisplayMode = CalendarMode.Decade;
        CheckCalendar("in the decade view");

        window.SendMouseMove(new Point(WIDTH - 4, HEIGHT + 70));
        CheckCalendar("pointer moved away");
    }

    private static ToolBarItem Item(string id)
        => new(new Command(id, id)) { Presentation = CommandPresentationMode.Text };

    private static GdiGraphicsFactory Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        return factory;
    }

    private static IRenderSurface Surface(GdiGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

    private static void Check(GdiGraphicsFactory factory, Window window, IRenderSurface surface, string label)
    {
        for (int index = 0; index < 2; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        using var reference = Surface(factory);
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

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from a frame drawn straight from the visuals");
    }
}
