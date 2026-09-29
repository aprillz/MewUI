using System.Reflection;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Popups inside popups on native surfaces, driven by the real cursor. An overflow entry is hovered on the
/// overflow popup's own window, so its tooltip must be placed from the pointer that window saw, stack above
/// that window and stay put while the pointer moves over the entry. A drop-down opened from the overflow
/// takes the dismiss watch over, and a press outside must still close both.
/// </summary>
[TestClass]
public sealed class PopupNestingTests
{
    private const int SETTLE_MS = 400;

    [TestMethod]
    public async Task ToolTipOnAnOverflowEntry_OpensBesideTheCursorAboveTheOverflowAndHolds()
    {
        if (Skip(out var monitor)) return;

        await RealAppSession.RunAsync(async () =>
        {
            var first = TippedButton("First");
            var second = TippedButton("Second");
            var third = TippedButton("Third");
            var (window, group) = Host(monitor, first, second, third);

            Input.GetCursorPos(out var restore);
            try
            {
                await OpenOverflowAsync(window, group);
                Assert.Contains(second, group.OverflowContent.Items, "the cut entry is not in the popup");

                var centre = ToScreen(window, second.Bounds.X + second.Bounds.Width / 2, second.Bounds.Y + second.Bounds.Height / 2);
                Input.SetCursorPos(centre.X, centre.Y);
                await Task.Delay(SETTLE_MS);

                var tip = FindToolTip(window);
                Assert.IsNotNull(tip, "the borrowed entry showed no tooltip");

                var cursor = CursorInClient(window);
                Assert.AreEqual(cursor.X + 12, tip.Element.Bounds.X, 2.0, "the tooltip is not beside the cursor");
                Assert.AreEqual(cursor.Y + 18, tip.Element.Bounds.Y, 2.0, "the tooltip is not below the cursor");

                var overflowSurface = SurfaceOf(window, group.OverflowContent.Items[0]);
                Assert.IsTrue(IsAbove(tip.Handle, overflowSurface), "the tooltip window stacks under the overflow window");

                // Small moves over the entry must neither close nor replace the tooltip surface.
                nint handle = tip.Handle;
                int changes = 0;
                for (int step = 1; step <= 6; step++)
                {
                    Input.SetCursorPos(centre.X + (step % 2 == 0 ? 2 : -2), centre.Y + (step % 3) - 1);
                    await Task.Delay(60);
                    if (FindToolTip(window)?.Handle != handle)
                    {
                        changes++;
                    }
                }

                Assert.AreEqual(0, changes, "the tooltip closed or was replaced while the pointer stayed on the entry");
            }
            finally
            {
                Input.SetCursorPos(restore.X, restore.Y);
                window.Close();
            }
        });
    }

    [TestMethod]
    public async Task EnteringAnOverflowEntryWithTheButtonHeld_ShowsNoToolTipUntilAFreshEnter()
    {
        if (Skip(out var monitor)) return;

        await RealAppSession.RunAsync(async () =>
        {
            // Borders take no capture, so hover keeps following the pointer during the press.
            var first = TippedBorder("First");
            var second = TippedBorder("Second");
            var third = TippedBorder("Third");
            var (window, group) = Host(monitor, first, second, third);

            Input.GetCursorPos(out var restore);
            try
            {
                await OpenOverflowAsync(window, group);
                Assert.Contains(second, group.OverflowContent.Items, "the second entry is not in the popup");
                Assert.Contains(third, group.OverflowContent.Items, "the third entry is not in the popup");

                var from = ToScreen(window, second.Bounds.X + second.Bounds.Width / 2, second.Bounds.Y + second.Bounds.Height / 2);
                var to = ToScreen(window, third.Bounds.X + third.Bounds.Width / 2, third.Bounds.Y + third.Bounds.Height / 2);

                Input.SetCursorPos(from.X, from.Y);
                await Task.Delay(150);
                Input.mouse_event(Input.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                await Task.Delay(60);
                Input.SetCursorPos(to.X, to.Y);
                await Task.Delay(SETTLE_MS);
                bool tooltipWhileHeld = FindToolTip(window) is not null;

                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                await Task.Delay(150);
                bool overflowStayed = group.OverflowContent.IsOpen;

                Input.SetCursorPos(from.X, from.Y);
                await Task.Delay(150);
                Input.SetCursorPos(to.X, to.Y);
                await Task.Delay(SETTLE_MS);
                bool tooltipAfterFreshEnter = FindToolTip(window) is not null;

                Assert.IsFalse(tooltipWhileHeld, "the tooltip opened for a pointer that entered with the button held");
                Assert.IsTrue(overflowStayed, "a press inside the overflow popup closed it");
                Assert.IsTrue(tooltipAfterFreshEnter, "the tooltip never opened after a fresh enter with the button up");
            }
            finally
            {
                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                Input.SetCursorPos(restore.X, restore.Y);
                window.Close();
            }
        });
    }

    [TestMethod]
    public async Task OutsidePress_ClosesTheOverflowAndTheDropDownItOpened()
    {
        if (Skip(out var monitor)) return;

        await RealAppSession.RunAsync(async () =>
        {
            var first = TippedButton("First");
            var second = TippedButton("Second");
            var dropDown = new DropDownButton { Width = 60, Height = 24, Content = new TextBlock { Text = "More" }, DropDownMenu = new Menu().Item("A").Item("B") };
            var under = new Border { Width = 200, Height = 60 };
            var (window, group) = Host(monitor, [first, second, dropDown], under);

            Input.GetCursorPos(out var restore);
            try
            {
                await OpenOverflowAsync(window, group);
                Assert.Contains(dropDown, group.OverflowContent.Items, "the drop-down is not in the popup");

                var dropDownCentre = ToScreen(window, dropDown.Bounds.X + dropDown.Bounds.Width / 2, dropDown.Bounds.Y + dropDown.Bounds.Height / 2);
                await ClickAsync(dropDownCentre);
                await Task.Delay(SETTLE_MS);
                Assert.IsTrue(dropDown.IsDropDownOpen, "the drop-down did not open from the overflow popup");
                Assert.IsTrue(group.OverflowContent.IsOpen, "opening the drop-down closed the overflow popup");

                var outside = ToScreen(window, under.Bounds.X + under.Bounds.Width / 2, under.Bounds.Y + under.Bounds.Height / 2);
                await ClickAsync(outside);
                await Task.Delay(SETTLE_MS);

                Assert.IsFalse(dropDown.IsDropDownOpen, "the drop-down stayed open after an outside press");
                Assert.IsFalse(group.OverflowContent.IsOpen, "the overflow popup stayed open after an outside press");
                Assert.AreEqual(0, Manager(window).Count, "a popup stayed registered after an outside press");
            }
            finally
            {
                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                Input.SetCursorPos(restore.X, restore.Y);
                window.Close();
            }
        });
    }

    private static bool Skip(out MonitorProbe monitor)
    {
        monitor = null!;
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return true;
        }

        var first = MonitorMatrix.Monitors.FirstOrDefault();
        if (first is null)
        {
            Assert.Inconclusive("Needs a monitor to place the window on.");
            return true;
        }

        monitor = first;
        return false;
    }

    private static Button TippedButton(string text)
        => new() { Width = 60, Height = 24, Content = new TextBlock { Text = text }, ToolTip = new TextBlock { Text = text + " tip" } };

    private static Border TippedBorder(string text)
        => new() { Width = 60, Height = 24, Background = Color.Gray, ToolTip = new TextBlock { Text = text + " tip" } };

    private static (Window window, ToolBar.GroupVisual group) Host(MonitorProbe monitor, params Element[] items)
        => Host(monitor, items, below: null);

    /// <summary>A toolbar cut after its first entry, with <paramref name="below"/> under it, shown on <paramref name="monitor"/>.</summary>
    private static (Window window, ToolBar.GroupVisual group) Host(MonitorProbe monitor, Element[] items, UIElement? below)
    {
        var bar = new ToolBar { Width = 400, HorizontalAlignment = HorizontalAlignment.Left };
        bar.Bands.Add(new ToolBarBand(new ToolBarGroup().Items(items)));
        var root = new StackPanel();
        root.Add(bar);
        if (below != null)
        {
            root.Add(new Border { Height = 120 });
            root.Add(below);
        }

        var window = new Window
        {
            Title = "PopupNesting",
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(480, 320),
            Content = root,
        };

        window.Show();
        MonitorProbe.SetWindowPos(window.Handle, 0,
            monitor.PixelBounds.Left + 80, monitor.PixelBounds.Top + 80, 0, 0, MonitorProbe.MOVE_ONLY);
        Input.SetForegroundWindow(window.Handle);
        window.PerformLayout();

        var group = bar.VisualsInternal[0].Groups[0];
        bar.Width = ((UIElement)group.Entries[1]).Bounds.X - bar.Bounds.X;
        window.PerformLayout();
        Assert.IsTrue(group.IsTruncated, "the band cut no entries");
        return (window, group);
    }

    private static async Task OpenOverflowAsync(Window window, ToolBar.GroupVisual group)
    {
        await Task.Delay(SETTLE_MS);
        var button = group.OverflowButton;
        await ClickAsync(ToScreen(window, button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2));
        await Task.Delay(SETTLE_MS);
        Assert.IsTrue(group.OverflowContent.IsOpen, "the overflow popup did not open");
    }

    private static async Task ClickAsync(Input.POINT point)
    {
        Input.SetCursorPos(point.X, point.Y);
        await Task.Delay(120);
        Input.mouse_event(Input.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        await Task.Delay(40);
        Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    private static PopupManager Manager(Window window)
        => (PopupManager)typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;

    private static List<PopupEntry> Entries(Window window)
        => (List<PopupEntry>)typeof(PopupManager).GetField("_popups", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Manager(window))!;

    private sealed record ToolTipSurface(UIElement Element, nint Handle);

    private static ToolTipSurface? FindToolTip(Window window)
    {
        foreach (var entry in Entries(window))
        {
            if (entry.Element is ToolTip && entry.NativeWindow is Window surface && Input.IsWindowVisible(surface.Handle))
            {
                return new ToolTipSurface(entry.Element, surface.Handle);
            }
        }

        return null;
    }

    /// <summary>The native popup window hosting <paramref name="element"/>.</summary>
    private static nint SurfaceOf(Window window, Element element)
    {
        foreach (var entry in Entries(window))
        {
            for (Element? current = element; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, entry.Element) && entry.NativeWindow is Window surface)
                {
                    return surface.Handle;
                }
            }
        }

        Assert.Fail("the element is not hosted in a native popup window");
        return 0;
    }

    /// <summary>Whether <paramref name="upper"/> comes before <paramref name="lower"/> in the z order.</summary>
    private static bool IsAbove(nint upper, nint lower)
    {
        for (nint current = Input.GetWindow(upper, Input.GW_HWNDNEXT); current != 0; current = Input.GetWindow(current, Input.GW_HWNDNEXT))
        {
            if (current == lower)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The cursor in the window's client DIPs, the space popup content is arranged in.</summary>
    private static Point CursorInClient(Window window)
    {
        Input.GetCursorPos(out var cursor);
        var origin = new Input.POINT();
        Input.ClientToScreen(window.Handle, ref origin);
        return new Point((cursor.X - origin.X) / window.DpiScale, (cursor.Y - origin.Y) / window.DpiScale);
    }

    /// <summary>Screen pixel position of a client-area point given in DIPs.</summary>
    private static Input.POINT ToScreen(Window window, double dipX, double dipY)
    {
        var origin = new Input.POINT();
        Input.ClientToScreen(window.Handle, ref origin);
        return new Input.POINT
        {
            X = origin.X + (int)Math.Round(dipX * window.DpiScale),
            Y = origin.Y + (int)Math.Round(dipY * window.DpiScale),
        };
    }

    private static class Input
    {
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint GW_HWNDNEXT = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(nint hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nint extraInfo);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll")]
        public static extern nint GetWindow(nint hWnd, uint command);    }
}
