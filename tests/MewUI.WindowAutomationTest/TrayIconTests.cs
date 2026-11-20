using System.Reflection;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A tray icon reaches the platform's notification area or status bar, comes back after the shell restarts, reports
/// clicks, and opens its menu in the platform's way with commands resolved in the icon's scope.
/// </summary>
[TestClass]
public sealed class TrayIconTests
{
    private const uint WM_TRAY_CALLBACK = 0x8000 + 0x0101;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint NIN_SELECT = 0x0400;
    private const uint ICON_ID = 1;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_DOWN = 0x28;

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_TheIconStaysInTheNotificationArea_UntilHidden()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var tray = new TrayIcon().ToolTipText("MewUI tray test").IsVisible();
            try
            {
                await Task.Delay(300);
                nint callback = BackendField<nint>(tray, "_window");
                Assert.IsTrue(Win32.IsPlaced(callback), "the shell did not place the icon");

                tray.IsVisible = false;
                Assert.IsFalse(Win32.IsPlaced(callback), "hiding the icon left it in the notification area");
            }
            finally
            {
                tray.Dispose();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_AnExplorerRestart_BringsTheIconBack()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var tray = new TrayIcon().IsVisible();
            try
            {
                await Task.Delay(300);
                nint callback = BackendField<nint>(tray, "_window");

                // What a restarting Explorer does: the icon is gone, then the new taskbar announces itself.
                Win32.Delete(callback);
                Assert.IsFalse(Win32.IsPlaced(callback));
                Win32.SendMessageW(callback, Win32.RegisterWindowMessageW("TaskbarCreated"), 0, 0);

                Assert.IsTrue(Win32.IsPlaced(callback), "the icon did not come back after TaskbarCreated");
            }
            finally
            {
                tray.Dispose();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_ClicksReachClickTheCommandAndDoubleClick()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var command = new Command("tray.test.click");
            var events = new List<string>();
            var tray = new TrayIcon().Command(command).IsVisible();
            tray.Commands.Register(command, () => events.Add("command"));
            tray.Click += args => events.Add($"click {args.ScreenPosition.X},{args.ScreenPosition.Y}");
            tray.DoubleClick += _ => events.Add("double");
            try
            {
                await Task.Delay(300);
                nint callback = BackendField<nint>(tray, "_window");

                Win32.SendMessageW(callback, WM_TRAY_CALLBACK, Win32.MakeLong(300, 400), Win32.MakeLong((int)NIN_SELECT, (int)ICON_ID));
                Win32.SendMessageW(callback, WM_TRAY_CALLBACK, Win32.MakeLong(300, 400), Win32.MakeLong((int)WM_LBUTTONDBLCLK, (int)ICON_ID));

                CollectionAssert.AreEqual(new[] { "click 300,400", "command", "double" }, events);
            }
            finally
            {
                tray.Dispose();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_TheMenuTakesTheForeground_AndClosesTheWaysAMenuDoes()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var keeper = new Window
            {
                Title = "TrayKeeper",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(320, 200),
                Content = new TextBlock { Text = "keeper" },
            };
            var chosen = new Command("tray.test.menu");
            int ran = 0;
            var tray = new TrayIcon()
                .ContextMenu(new ContextMenu().Item(chosen).Separator().Item("Other"))
                .IsVisible();
            tray.Commands.Register(chosen, () => ran++);
            try
            {
                keeper.Show();
                keeper.MoveTo(120, 120);
                // The shell lets the icon's process take the foreground; a test stands in by holding it already.
                await RealInput.Current.ActivateAsync(keeper);
                await Task.Delay(300);
                nint callback = BackendField<nint>(tray, "_window");

                var host = await OpenMenuAsync(callback);
                Assert.AreEqual(host.Handle, Win32.GetForegroundWindow(), "the menu host did not take the foreground");

                int ticks = 0;
                using (var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30)))
                {
                    timer.Tick += () => ticks++;
                    timer.Start();
                    await Task.Delay(400);
                }
                Assert.IsGreaterThan(3, ticks, "the UI loop stopped while the menu was open");

                Win32.Key(VK_DOWN);
                Win32.Key(VK_RETURN);
                await Task.Delay(400);
                Assert.AreEqual(1, ran, "choosing the first item from the keyboard did not run its command");
                Assert.AreEqual(0, host.OpenPopupCount);
                Assert.IsFalse(Win32.IsWindowVisible(host.Handle), "the menu host stayed shown after the menu closed");

                host = await OpenMenuAsync(callback);
                Win32.Key(VK_ESCAPE);
                await Task.Delay(300);
                Assert.AreEqual(0, host.OpenPopupCount, "Escape did not close the menu");

                host = await OpenMenuAsync(callback);
                await RealInput.Current.ClickForeignWindowAsync();
                await Task.Delay(300);
                Assert.AreEqual(0, host.OpenPopupCount, "a click outside did not close the menu");
                Assert.AreEqual(1, ran);
            }
            finally
            {
                tray.Dispose();
                keeper.Close();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_RealClicksOnTheIcon_RunTheCommandAndOpenTheMenu()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var command = new Command("tray.test.realClick");
            int ran = 0;
            var tray = new TrayIcon()
                .Command(command)
                .ContextMenu(new ContextMenu().Item("First").Item("Second"))
                .IsVisible();
            tray.Commands.Register(command, () => ran++);
            object? promotion = null;
            try
            {
                await Task.Delay(400);
                nint callback = BackendField<nint>(tray, "_window");

                // Windows 11 puts a new icon in the overflow flyout, where the rect it reports is the flyout button's;
                // the user's "show in taskbar" setting is what brings it out.
                promotion = Win32.PromoteIcon();
                if (promotion is not System.Collections.ICollection promoted || promoted.Count == 0)
                {
                    Assert.Inconclusive("The shell has no taskbar setting for this process's icon to bring it out of the overflow flyout.");
                }

                await Task.Delay(1000);
                if (!Win32.TryGetIconCenter(callback, out int centerX, out int centerY))
                {
                    Assert.Inconclusive("The shell placed the icon where it cannot be clicked (the overflow area).");
                }

                Win32.Click(centerX, centerY, rightButton: false);
                await Task.Delay(500);
                Assert.AreEqual(1, ran, "a left click on the icon did not run its command");

                Win32.Click(centerX, centerY, rightButton: true);
                await Task.Delay(500);
                var host = Application.Current.SnapshotAllWindows().Single(static window => window.Kind == WindowKind.MenuHost);
                Assert.AreEqual(1, host.OpenPopupCount, "a right click on the icon did not open the menu");
                Assert.AreEqual(host.Handle, Win32.GetForegroundWindow(), "the menu host did not take the foreground");

                Win32.Key(VK_ESCAPE);
                await Task.Delay(300);
                Assert.AreEqual(0, host.OpenPopupCount);
            }
            finally
            {
                tray.Dispose();
                Win32.RestorePromotion(promotion);
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    public async Task OnMacOS_TheStatusMenuShowsTheModel_AndRunsAChosenItem()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var open = new Command("tray.test.open", "_Open");
            var unhandled = new Command("tray.test.unhandled");
            int ran = 0;
            var glyph = new IconTemplate(static _ => new Border { Background = Color.Black });
            var tray = new TrayIcon()
                .ContextMenu(new ContextMenu()
                    .Add(new MenuItem(open).Icon(glyph))
                    .Item(unhandled)
                    .Separator()
                    .Add(new MenuItem("Wrap").IsCheckable().IsChecked())
                    .SubMenu("More", new ContextMenu().Item("Inner")))
                .IsVisible();
            tray.Commands.Register(open, () => ran++);
            try
            {
                await Task.Delay(400);
                nint statusItem = BackendField<nint>(tray, "_statusItem");
                nint menu = BackendField<nint>(tray, "_menu");
                nint target = BackendField<nint>(tray, "_target");

                nint button = ObjC.Send(statusItem, "button");
                Assert.AreNotEqual((nint)0, ObjC.Send(button, "window"), "the status item did not reach the menu bar");

                // What AppKit asks just before the menu opens.
                ObjC.SendVoid(target, "menuNeedsUpdate:", menu);

                Assert.AreEqual(5, (int)ObjC.Send(menu, "numberOfItems"));
                nint first = ObjC.Send(menu, "itemAtIndex:", 0);
                Assert.AreEqual("Open", ObjC.Title(first), "a menu bar menu has no access keys");
                Assert.IsTrue(ObjC.SendBool(first, "isEnabled"));
                nint image = ObjC.Send(first, "image");
                Assert.AreNotEqual((nint)0, image, "the item's icon is missing");
                Assert.IsTrue(ObjC.SendBool(image, "isTemplate"), "a glyph icon should be a template image");

                Assert.IsFalse(ObjC.SendBool(ObjC.Send(menu, "itemAtIndex:", 1), "isEnabled"), "a command without a handler was enabled");
                Assert.IsTrue(ObjC.SendBool(ObjC.Send(menu, "itemAtIndex:", 2), "isSeparatorItem"));
                Assert.AreEqual((nint)1, ObjC.Send(ObjC.Send(menu, "itemAtIndex:", 3), "state"), "the checked item is not on");
                nint subMenu = ObjC.Send(ObjC.Send(menu, "itemAtIndex:", 4), "submenu");
                Assert.AreEqual("Inner", ObjC.Title(ObjC.Send(subMenu, "itemAtIndex:", 0)));

                ObjC.SendVoid(menu, "performActionForItemAtIndex:", 0);
                await Task.Delay(300);
                Assert.AreEqual(1, ran, "choosing the item did not run its command");
            }
            finally
            {
                tray.Dispose();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    public async Task OnMacOS_WithoutAMenu_AClickReachesClick()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            int clicks = 0;
            var tray = new TrayIcon().IsVisible();
            tray.Click += _ => clicks++;
            try
            {
                await Task.Delay(400);
                nint button = ObjC.Send(BackendField<nint>(tray, "_statusItem"), "button");
                ObjC.SendVoid(button, "performClick:", 0);
                await Task.Delay(200);
                Assert.AreEqual(1, clicks);
            }
            finally
            {
                tray.Dispose();
            }
        });
    }

    private static async Task<Window> OpenMenuAsync(nint callback)
    {
        Win32.SendMessageW(callback, WM_TRAY_CALLBACK, Win32.MakeLong(400, 400), Win32.MakeLong((int)WM_CONTEXTMENU, (int)ICON_ID));
        await Task.Delay(400);
        var host = Application.Current.SnapshotAllWindows().Single(static window => window.Kind == WindowKind.MenuHost);
        Assert.AreEqual(1, host.OpenPopupCount, "the menu did not open");
        return host;
    }

    private static T BackendField<T>(TrayIcon tray, string name)
    {
        object backend = typeof(TrayIcon).GetField("_backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)
            ?? throw new AssertFailedException("the icon has no platform side");
        return (T)backend.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
    }

    private static class Win32
    {
        private const uint NIM_DELETE = 2;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public static nint MakeLong(int low, int high) => (nint)((high << 16) | (low & 0xFFFF));

        public static bool IsPlaced(nint window)
        {
            var identifier = new NOTIFYICONIDENTIFIER { Size = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), Window = window, Id = ICON_ID };
            return Shell_NotifyIconGetRect(ref identifier, out _) == 0;
        }

        public static void Delete(nint window)
        {
            var data = new NOTIFYICONDATAW { Size = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(), Window = window, Id = ICON_ID };
            _ = Shell_NotifyIconW(NIM_DELETE, ref data);
        }

        public static void Key(byte virtualKey)
        {
            keybd_event(virtualKey, 0, 0, 0);
            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, 0);
        }

        /// <summary>False when the icon has no place on the screen, as in the Windows 11 overflow flyout while it is closed.</summary>
        public static bool TryGetIconCenter(nint window, out int centerX, out int centerY)
        {
            var identifier = new NOTIFYICONIDENTIFIER { Size = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), Window = window, Id = ICON_ID };
            centerX = 0;
            centerY = 0;
            if (Shell_NotifyIconGetRect(ref identifier, out var bounds) != 0 || bounds.Right <= bounds.Left)
            {
                return false;
            }

            centerX = (bounds.Left + bounds.Right) / 2;
            centerY = (bounds.Top + bounds.Bottom) / 2;
            // The icon can be clicked only where the taskbar itself lies under its center.
            const uint GA_ROOT = 2;
            nint root = GetAncestor(WindowFromPoint(new POINT { X = centerX, Y = centerY }), GA_ROOT);
            var className = new System.Text.StringBuilder(64);
            _ = GetClassNameW(root, className, className.Capacity);
            return className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        }

        /// <summary>
        /// Sets the Windows 11 "show in taskbar" setting of this process's icons and returns what to restore, or null
        /// where the setting does not exist.
        /// </summary>
        public static object? PromoteIcon()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (settings == null)
            {
                return null;
            }

            var previous = new List<(string Key, object? Value)>();
            foreach (var name in settings.GetSubKeyNames())
            {
                using var icon = settings.OpenSubKey(name, writable: true);
                if (icon != null && string.Equals(icon.GetValue("ExecutablePath") as string, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    previous.Add((name, icon.GetValue("IsPromoted")));
                    icon.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }

            return previous;
        }

        public static void RestorePromotion(object? promotion)
        {
            if (!OperatingSystem.IsWindows() || promotion is not List<(string Key, object? Value)> previous)
            {
                return;
            }

            using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            foreach (var (key, value) in previous)
            {
                using var icon = settings?.OpenSubKey(key, writable: true);
                if (icon == null)
                {
                    continue;
                }

                if (value == null)
                {
                    icon.DeleteValue("IsPromoted", throwOnMissingValue: false);
                }
                else
                {
                    icon.SetValue("IsPromoted", value, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
        }

        public static void Click(int x, int y, bool rightButton)
        {
            const uint LEFT_DOWN = 0x0002;
            const uint LEFT_UP = 0x0004;
            const uint RIGHT_DOWN = 0x0008;
            const uint RIGHT_UP = 0x0010;
            _ = SetCursorPos(x, y);
            Thread.Sleep(120);
            mouse_event(rightButton ? RIGHT_DOWN : LEFT_DOWN, 0, 0, 0, 0);
            mouse_event(rightButton ? RIGHT_UP : LEFT_UP, 0, 0, 0, 0);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern nint WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern nint GetAncestor(nint window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(nint window, System.Text.StringBuilder className, int capacity);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, int data, nuint extraInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public uint Size;
            public nint Window;
            public uint Id;
            public Guid GuidItem;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public uint Size;
            public nint Window;
            public uint Id;
            public uint Flags;
            public uint CallbackMessage;
            public nint Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Tip;
            public uint State;
            public uint StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string InfoTitle;
            public uint InfoFlags;
            public Guid GuidItem;
            public nint BalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT bounds);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessageW(string name);

        [DllImport("user32.dll")]
        public static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }

    private static class ObjC
    {
        private const string LIBRARY = "/usr/lib/libobjc.A.dylib";

        public static nint Send(nint receiver, string selector) => MsgSend(receiver, sel_registerName(selector));

        public static nint Send(nint receiver, string selector, nint argument) => MsgSend(receiver, sel_registerName(selector), argument);

        public static void SendVoid(nint receiver, string selector, nint argument) => MsgSendVoid(receiver, sel_registerName(selector), argument);

        public static bool SendBool(nint receiver, string selector) => MsgSendByte(receiver, sel_registerName(selector)) != 0;

        public static string? Title(nint menuItem) => Marshal.PtrToStringUTF8(Send(Send(menuItem, "title"), "UTF8String"));

        [DllImport(LIBRARY, CharSet = CharSet.Ansi)]
        private static extern nint sel_registerName(string name);

        [DllImport(LIBRARY, EntryPoint = "objc_msgSend")]
        private static extern nint MsgSend(nint receiver, nint selector);

        [DllImport(LIBRARY, EntryPoint = "objc_msgSend")]
        private static extern nint MsgSend(nint receiver, nint selector, nint argument);

        [DllImport(LIBRARY, EntryPoint = "objc_msgSend")]
        private static extern void MsgSendVoid(nint receiver, nint selector, nint argument);

        [DllImport(LIBRARY, EntryPoint = "objc_msgSend")]
        private static extern byte MsgSendByte(nint receiver, nint selector);
    }
}
