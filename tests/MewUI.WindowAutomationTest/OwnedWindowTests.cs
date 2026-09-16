using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A window shown with an owner is owned by the platform even when it keeps its own taskbar button, so
/// the platform keeps it above the owner and the taskbar button survives the ownership.
/// </summary>
[TestClass]
public sealed class OwnedWindowTests
{
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public Task OnWindows_TheDialogIsOwnedKeepsItsTaskbarStyleAndStaysAboveTheOwner() => RunOwnedDialogAsync(async (owner, dialog) =>
    {
        Assert.AreEqual(owner.Handle, Win32.GetWindow(dialog.Handle, Win32.GW_OWNER), "the dialog was not owned by the window it was shown for");

        nint exStyle = Win32.GetWindowLongPtr(dialog.Handle, Win32.GWL_EXSTYLE);
        Assert.AreNotEqual((nint)0, exStyle & Win32.WS_EX_APPWINDOW, "the owned dialog lost the style that keeps its taskbar button");

        // Stands in for a click on the disabled owner, which the desktop state can block for real input.
        owner.Activate();
        await Task.Delay(400);

        Assert.IsTrue(Win32.IsAbove(dialog.Handle, owner.Handle), "the owner covered the dialog when it was activated");
    });

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public Task OnX11_TheDialogIsTransientForTheOwnerAndCarriesTheModalState() => RunOwnedDialogAsync((owner, dialog) =>
    {
        nint display = X11.XOpenDisplay(0);
        Assert.AreNotEqual((nint)0, display, "a second X connection could not be opened");

        try
        {
            _ = X11.XGetTransientForHint(display, (nuint)dialog.Handle, out nuint transientFor);
            Assert.AreEqual((nuint)owner.Handle, transientFor, "the dialog was not transient for the window it was shown for");

            nint modalAtom = X11.XInternAtom(display, "_NET_WM_STATE_MODAL", 0);
            Assert.IsTrue(X11.HasState(display, (nuint)dialog.Handle, modalAtom), "the dialog did not carry the modal state the window manager reads");
        }
        finally
        {
            _ = X11.XCloseDisplay(display);
        }

        return Task.CompletedTask;
    });

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    public Task OnMacOS_TheDialogIsAChildOfTheOwnerAndSitsOnAHigherLevel() => RunOwnedDialogAsync((owner, dialog) =>
    {
        nint ownerWindow = MacOS.WindowOfView(owner.Handle);
        nint dialogWindow = MacOS.WindowOfView(dialog.Handle);
        Assert.AreNotEqual((nint)0, dialogWindow, "the dialog view has no window");

        Assert.AreEqual(ownerWindow, MacOS.ParentWindow(dialogWindow), "the dialog was not attached to the window it was shown for");
        Assert.IsGreaterThan(MacOS.Level(ownerWindow), MacOS.Level(dialogWindow), "the dialog did not sit above its owner's level");

        return Task.CompletedTask;
    });

    // The scene every platform checks: an owner window, and a modal dialog that keeps its own taskbar button.
    private static async Task RunOwnedDialogAsync(Func<Window, Window, Task> check)
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var owner = new Window
            {
                Title = "OwnedWindowOwner",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(420, 320),
                Content = new TextBlock { Text = "owner" },
            };
            var dialog = new Window
            {
                Title = "OwnedWindowDialog",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(260, 180),
                ShowInTaskbar = true,
                Content = new TextBlock { Text = "dialog" },
            };

            Task? dialogClosed = null;
            try
            {
                owner.Show();
                owner.MoveTo(120, 120);
                await Task.Delay(400);

                dialogClosed = dialog.ShowDialogAsync(owner);
                dialog.MoveTo(260, 240);
                await Task.Delay(500);

                await check(owner, dialog);
            }
            finally
            {
                dialog.Close();
                if (dialogClosed != null)
                {
                    await dialogClosed;
                }

                owner.Close();
            }
        });
    }

    private static class Win32
    {
        public const uint GW_OWNER = 4;
        public const int GWL_EXSTYLE = -20;
        public const uint WS_EX_APPWINDOW = 0x00040000;

        private const uint GW_HWNDNEXT = 2;

        // Walks the z-order downwards from the window: reaching the other one means this one is in front.
        public static bool IsAbove(nint window, nint other)
        {
            for (nint current = GetWindow(window, GW_HWNDNEXT); current != 0; current = GetWindow(current, GW_HWNDNEXT))
            {
                if (current == other)
                {
                    return true;
                }
            }

            return false;
        }

        [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    }

    private static class X11
    {
        private const int ATOM_TYPE = 4;
        private const long MAX_STATE_ATOMS = 32;

        public static bool HasState(nint display, nuint window, nint stateAtom)
        {
            nint netWmState = XInternAtom(display, "_NET_WM_STATE", 0);
            if (netWmState == 0 || stateAtom == 0)
            {
                return false;
            }

            int status = XGetWindowProperty(display, window, netWmState, 0, MAX_STATE_ATOMS, 0, ATOM_TYPE,
                out _, out _, out nuint count, out _, out nint values);
            if (status != 0 || values == 0)
            {
                return false;
            }

            try
            {
                for (nuint index = 0; index < count; index++)
                {
                    if (Marshal.ReadIntPtr(values, (int)index * IntPtr.Size) == stateAtom)
                    {
                        return true;
                    }
                }
            }
            finally
            {
                _ = XFree(values);
            }

            return false;
        }

        [DllImport("libX11.so.6")] public static extern nint XOpenDisplay(nint displayName);
        [DllImport("libX11.so.6")] public static extern int XCloseDisplay(nint display);
        [DllImport("libX11.so.6")] public static extern int XGetTransientForHint(nint display, nuint window, out nuint propWindow);
        [DllImport("libX11.so.6")] public static extern nint XInternAtom(nint display, string name, int onlyIfExists);
        [DllImport("libX11.so.6")] public static extern int XFree(nint data);
        [DllImport("libX11.so.6")]
        public static extern int XGetWindowProperty(nint display, nuint window, nint property, long offset, long length,
            int delete, nint requestedType, out nint actualType, out int actualFormat,
            out nuint itemCount, out nuint bytesAfter, out nint values);
    }

    private static class MacOS
    {
        private const string OBJC = "/usr/lib/libobjc.dylib";

        public static nint WindowOfView(nint view) => view == 0 ? 0 : Send(view, sel_registerName("window"));

        public static nint ParentWindow(nint window) => window == 0 ? 0 : Send(window, sel_registerName("parentWindow"));

        public static long Level(nint window) => window == 0 ? 0 : SendLong(window, sel_registerName("level"));

        [DllImport(OBJC)] private static extern nint sel_registerName(string name);
        [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
        [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern long SendLong(nint receiver, nint selector);
    }
}
