using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A menu row changes on its own: the pointer moves onto it or off it, or its command becomes available.
/// Only that row may be recorded again and repainted, not the menu and not the rows between the old and
/// the new highlight.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedContextMenuRowTests
{
    private const int SIZE = 400;

    // An edge the repaint pads for antialiasing, in DIPs.
    private const double EDGE_SLACK = 2;

    [TestMethod]
    [DataRow(TestBackend.Gdi, 1.0)]
    [DataRow(TestBackend.Gdi, 1.5)]
    [DataRow(TestBackend.Direct2D, 1.0)]
    [DataRow(TestBackend.Direct2D, 1.5)]
    [DataRow(TestBackend.MewVG, 1.0)]
    [DataRow(TestBackend.MewVG, 1.5)]
    public void MovingTheHighlight_RecordsAndRepaintsOnlyTheTwoRows(TestBackend backend, double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var (window, menu, _) = ShowMenu(scale);
        using var surface = CreateSurface(session, scale);

        window.SendMouseMove(RowBounds(menu, 1).Center);
        Render(window, surface);
        Render(window, surface);
        int menuVersion = menu.RenderContentVersion;

        // The disabled row between them fades its icon, which the menu used to draw inside its own content.
        var from = RowBounds(menu, 1);
        var to = RowBounds(menu, 6);
        window.SendMouseMove(to.Center);
        Render(window, surface);

        Assert.AreEqual(menuVersion, menu.RenderContentVersion, "moving the highlight recorded the menu again");
        AssertRepaintedOnly(window, [from, to], "moving the highlight from row 1 to row 6");
        AssertEqualsReference(session, window, surface, scale, "after the highlight moved");
    }

    [TestMethod]
    [DataRow(TestBackend.Gdi, 1.0)]
    [DataRow(TestBackend.Gdi, 1.5)]
    [DataRow(TestBackend.Direct2D, 1.0)]
    [DataRow(TestBackend.Direct2D, 1.5)]
    [DataRow(TestBackend.MewVG, 1.0)]
    [DataRow(TestBackend.MewVG, 1.5)]
    public void ACommandStateChange_RecordsOnlyItsRow(TestBackend backend, double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var (window, menu, canExecute) = ShowMenu(scale);
        using var surface = CreateSurface(session, scale);

        Render(window, surface);
        Render(window, surface);
        int menuVersion = menu.RenderContentVersion;

        canExecute[5] = false;
        window.EvaluateCommandStates();
        Render(window, surface);

        Assert.AreEqual(menuVersion, menu.RenderContentVersion, "a command state change on one row recorded the menu again");
        AssertRepaintedOnly(window, [RowBounds(menu, 5)], "disabling the command of row 5");
        AssertEqualsReference(session, window, surface, scale, "after the command state changed");
    }

    [TestMethod]
    [DataRow(TestBackend.Gdi, 1.0)]
    [DataRow(TestBackend.Gdi, 1.25)]
    [DataRow(TestBackend.Direct2D, 1.25)]
    [DataRow(TestBackend.MewVG, 1.25)]
    public void ScrollingTheMenu_KeepsTheFrameEqualToTheVisuals(TestBackend backend, double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var owner = new Border { Width = 50, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = HeadlessWindow.Create(SIZE, SIZE);
        window.SetDpi((uint)Math.Round(96 * scale));
        window.Content = owner;
        window.PerformLayout();

        var menu = new ContextMenu { MaxMenuHeight = 150 };
        for (int index = 0; index < 20; index++)
        {
            menu.AddItem($"Scrolled item {index}");
        }

        menu.Show(owner, new Point(40, 40));
        using var surface = CreateSurface(session, scale);
        Render(window, surface);
        Render(window, surface);

        var point = RowBounds(menu, 1).Center;
        window.SendMouseMove(point);
        Render(window, surface);

        var inViewBefore = RowsInView(menu, 20);
        window.RetainedStatistics.Reset();
        window.SendMouseWheel(point, -120);
        Render(window, surface);
        var broughtIntoView = RowsInView(menu, 20).Except(inViewBefore).ToList();

        // The rows that stay in view only move. The scroll bar records its moved thumb, and the menu may record its frame.
        Assert.IsNotEmpty(broughtIntoView, "the wheel step did not scroll the menu");
        Assert.IsLessThanOrEqualTo(
            broughtIntoView.Count + 2,
            window.RetainedStatistics.ContentRecordCount,
            $"one wheel step recorded more than the {broughtIntoView.Count} rows it brought into view");
        AssertEqualsReference(session, window, surface, scale, "after one wheel step");
    }

    private static (Window Window, ContextMenu Menu, bool[] CanExecute) ShowMenu(double scale)
    {
        var owner = new Border { Width = 50, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = HeadlessWindow.Create(SIZE, SIZE);
        window.SetDpi((uint)Math.Round(96 * scale));
        window.Content = owner;
        window.PerformLayout();

        var icon = new IconTemplate(static size => new Border { Width = size.Dip, Height = size.Dip, Background = Color.FromRgb(40, 120, 200) });
        var canExecute = new bool[8];
        var menu = new ContextMenu();
        for (int index = 0; index < canExecute.Length; index++)
        {
            int row = index;
            canExecute[row] = true;
            var command = new Command($"probe.row{row}", $"Menu row number {row}", icon);
            owner.Commands.Register(command, static () => { }, () => canExecute[row]);
            var item = new MenuItem(command);
            if (index == 3)
            {
                item.IsEnabled = false;
            }

            menu.AddEntry(item);
        }

        menu.Show(owner, new Point(40, 40));
        window.PerformLayout();
        return (window, menu, canExecute);
    }

    private static IRenderSurface CreateSurface(TestBackendSession session, double scale)
    {
        int pixels = (int)Math.Round(SIZE * scale);
        return session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixels, pixels, scale, hasAlpha: false));
    }

    private static void Render(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static Rect RowBounds(ContextMenu menu, int index)
    {
        var method = typeof(ContextMenu).GetMethod("TryGetEntryRowBounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object?[] args = [index, null];
        Assert.IsTrue((bool)method.Invoke(menu, args)!, $"row {index} is not in the menu");
        return (Rect)args[1]!;
    }

    private static List<int> RowsInView(ContextMenu menu, int count)
    {
        var inView = new List<int>();
        for (int index = 0; index < count; index++)
        {
            var row = RowBounds(menu, index);
            if (row.Bottom > menu.Bounds.Y && row.Y < menu.Bounds.Bottom)
            {
                inView.Add(index);
            }
        }

        return inView;
    }

    private static void AssertRepaintedOnly(Window window, Rect[] rows, string label)
    {
        Assert.IsNotNull(window.LastRetainedDirtyRect, $"{label}: the frame was drawn whole ({window.LastWholeFrameReason})");
        foreach (var dirty in window.LastRetainedDirtyRects)
        {
            bool insideARow = rows.Any(row =>
                dirty.Y >= row.Y - EDGE_SLACK && dirty.Bottom <= row.Bottom + EDGE_SLACK);
            Assert.IsTrue(
                insideARow,
                $"{label}: the repaint {dirty} reaches past the changed rows {string.Join(" ", rows)}; all repaints: {string.Join(" ", window.LastRetainedDirtyRects)}");
        }
    }

    private static void AssertEqualsReference(TestBackendSession session, Window window, IRenderSurface surface, double scale, string label)
    {
        int pixels = (int)Math.Round(SIZE * scale);
        using var reference = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixels, pixels, scale, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        TestBackendSession.AssertSurfacesEqual(reference, surface, pixels, session.ChannelTolerance, label);
    }
}
