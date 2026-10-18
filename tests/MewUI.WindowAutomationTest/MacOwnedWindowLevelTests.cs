using System.Reflection;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Owned windows on macOS take the level AppKit gives their kind (issue #278). Window levels order windows across
/// every application, so a dialog owned by a normal window may sit above the normal level only while its application
/// is active; otherwise it would cover whatever application the user switched to.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MacOwnedWindowLevelTests
{
    private const string OBJC = "/usr/lib/libobjc.A.dylib";
    private const string CORE_GRAPHICS = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CORE_FOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint ON_SCREEN_ONLY = 1;
    private const int NUMBER_SINT64 = 4;
    private const uint UTF8 = 0x08000100;
    private const long NORMAL_LEVEL = 0;
    private const long FLOATING_LEVEL = 3;
    private const long MODAL_PANEL_LEVEL = 8;
    private const int SETTLE_MS = 600;

    [TestMethod]
    public Task ModalDialog_OfANormalOwner_StaysAtTheNormalLevelAboveItsOwner_WhileTheAppIsInactive() => RunOnMacAsync(async () =>
    {
        // A test run over SSH is never the active application, the state the issue reports from.
        Assert.IsFalse(IsApplicationActive(), "this check needs the application inactive");
        var (owner, dialog, shown) = await ShowDialogAsync();
        try
        {
            Assert.AreEqual(NORMAL_LEVEL, Level(dialog), "an inactive application's dialog sits at the normal level");
            Assert.IsTrue(IsDirectlyAbove(dialog, owner), "the dialog stays ordered above its owner");
        }
        finally
        {
            dialog.Close();
            await shown;
            owner.Close();
        }
    });

    [TestMethod]
    public Task ModalDialog_RisesWhileTheAppIsActive_AndReturnsAboveItsOwnerWhenItIsNot() => RunOnMacAsync(async () =>
    {
        var (owner, dialog, shown) = await ShowDialogAsync();
        try
        {
            ApplyLevel(dialog, applicationActive: true);
            Assert.AreEqual(MODAL_PANEL_LEVEL, Level(dialog), "an active application's dialog takes the modal panel level");

            SendVoidArg(NsWindow(owner), Sel("orderFront:"), 0);
            ApplyLevel(dialog, applicationActive: false);
            await Task.Delay(200);
            Assert.AreEqual(NORMAL_LEVEL, Level(dialog), "the dialog leaves the modal level when the application deactivates");
            Assert.IsTrue(IsDirectlyAbove(dialog, owner), "back at the normal level the dialog is ordered above its owner again");
        }
        finally
        {
            dialog.Close();
            await shown;
            owner.Close();
        }
    });

    [TestMethod]
    public Task OwnedModelessWindow_FloatsAndHidesWhileTheAppIsInactive() => RunOnMacAsync(async () =>
    {
        var owner = NewWindow("LevelOwner", 420, 300);
        var panel = NewWindow("LevelPanel", 240, 140);
        try
        {
            owner.Show();
            owner.MoveTo(200, 200);
            panel.Show(owner);
            panel.MoveTo(260, 240);
            await Task.Delay(SETTLE_MS);

            Assert.AreEqual(FLOATING_LEVEL, Level(panel), "an owned window floats over its application's windows");
            Assert.IsTrue(SendBool(NsWindow(panel), Sel("hidesOnDeactivate")), "an owned window hides while its application is inactive");
            Assert.IsFalse(SendBool(NsWindow(owner), Sel("hidesOnDeactivate")), "an unowned window stays visible");
        }
        finally
        {
            panel.Close();
            owner.Close();
        }
    });

    [TestMethod]
    public Task ModalDialog_OfATopmostOwner_NeverDropsBelowIt() => RunOnMacAsync(async () =>
    {
        var (owner, dialog, shown) = await ShowDialogAsync(ownerTopmost: true);
        try
        {
            Assert.AreEqual(FLOATING_LEVEL, Level(owner), "the topmost owner floats");
            Assert.AreEqual(FLOATING_LEVEL, Level(dialog), "an inactive application's dialog keeps its topmost owner's level");
            Assert.IsTrue(IsDirectlyAbove(dialog, owner), "and stays above it");
        }
        finally
        {
            dialog.Close();
            await shown;
            owner.Close();
        }
    });

    private static async Task<(Window Owner, Window Dialog, Task Shown)> ShowDialogAsync(bool ownerTopmost = false)
    {
        var owner = NewWindow("LevelOwner", 420, 300);
        owner.Topmost = ownerTopmost;
        var dialog = NewWindow("LevelDialog", 260, 140);
        owner.Show();
        owner.MoveTo(200, 200);
        await Task.Delay(SETTLE_MS);
        var shown = dialog.ShowDialogAsync(owner);
        dialog.MoveTo(280, 260);
        await Task.Delay(SETTLE_MS);
        return (owner, dialog, shown);
    }

    private static Window NewWindow(string title, double width, double height)
        => new() { Title = title, StartupLocation = WindowStartupLocation.Manual, WindowSize = WindowSize.Fixed(width, height) };

    private static long Level(Window window) => SendLong(NsWindow(window), Sel("level"));

    private static void ApplyLevel(Window window, bool applicationActive)
    {
        var backend = Backend(window);
        backend.GetType().GetMethod("ApplyWindowLevel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(backend, [applicationActive]);
    }

    private static bool IsApplicationActive()
        => SendBool(Send(objc_getClass("NSApplication"), Sel("sharedApplication")), Sel("isActive"));

    /// <summary>Whether <paramref name="upper"/> is the next on-screen window in front of <paramref name="lower"/>.</summary>
    private static bool IsDirectlyAbove(Window upper, Window lower)
    {
        long upperNumber = SendLong(NsWindow(upper), Sel("windowNumber"));
        long lowerNumber = SendLong(NsWindow(lower), Sel("windowNumber"));
        nint list = CGWindowListCopyWindowInfo(ON_SCREEN_ONLY, 0);
        nint key = CFStringCreateWithCString(0, "kCGWindowNumber", UTF8);
        long previous = -1;
        bool found = false;
        long count = CFArrayGetCount(list);
        for (long index = 0; index < count; index++)
        {
            long number = 0;
            CFNumberGetValue(CFDictionaryGetValue(CFArrayGetValueAtIndex(list, index), key), NUMBER_SINT64, ref number);
            if (number == lowerNumber)
            {
                found = previous == upperNumber;
                break;
            }

            previous = number;
        }

        CFRelease(key);
        CFRelease(list);
        return found;
    }

    private static Task RunOnMacAsync(Func<Task> body)
    {
        if (!OperatingSystem.IsMacOS() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real macOS application loop.");
        }

        return RealAppSession.RunAsync(body);
    }

    private static object Backend(Window window)
        => typeof(Window).GetProperty("Backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;

    private static nint NsWindow(Window window)
    {
        var backend = Backend(window);
        return (nint)backend.GetType().GetField("_nsWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
    }

    private static nint Sel(string name) => sel_registerName(name);

    [DllImport(OBJC)] private static extern nint objc_getClass(string name);
    [DllImport(OBJC)] private static extern nint sel_registerName(string name);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern void SendVoidArg(nint receiver, nint selector, nint argument);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern long SendLong(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(nint receiver, nint selector);

    [DllImport(CORE_GRAPHICS)] private static extern nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CORE_FOUNDATION)] private static extern long CFArrayGetCount(nint array);
    [DllImport(CORE_FOUNDATION)] private static extern nint CFArrayGetValueAtIndex(nint array, long index);
    [DllImport(CORE_FOUNDATION)] private static extern nint CFDictionaryGetValue(nint dictionary, nint key);
    [DllImport(CORE_FOUNDATION)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(nint number, int type, ref long value);
    [DllImport(CORE_FOUNDATION)] private static extern nint CFStringCreateWithCString(nint allocator, string text, uint encoding);
    [DllImport(CORE_FOUNDATION)] private static extern void CFRelease(nint value);
}
