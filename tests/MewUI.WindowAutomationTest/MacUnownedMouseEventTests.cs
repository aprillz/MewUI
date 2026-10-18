using System.Reflection;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Mouse events AppKit delivers with no window: their location is in screen coordinates, so reading it as a
/// window position lights controls the pointer is nowhere near. The events are built with window number 0 and
/// posted to the application's own queue, the way AppKit hands them over while the pointer is over another app.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MacUnownedMouseEventTests
{
    private const string OBJC = "/usr/lib/libobjc.A.dylib";
    private const nuint LEFT_MOUSE_DOWN = 1;
    private const nuint LEFT_MOUSE_UP = 2;
    private const nuint MOUSE_MOVED = 5;
    private const nuint LEFT_MOUSE_DRAGGED = 6;
    private const int SETTLE_MS = 600;
    private const int EVENT_MS = 150;

    private sealed class Scene : IDisposable
    {
        public required Window Window { get; init; }
        public required Button Button { get; init; }
        public required Slider Slider { get; init; }

        public void Dispose() => Window.Close();
    }

    [TestMethod]
    public Task UnownedMove_WhoseScreenPointReadsAsTheButton_DoesNotHoverIt() => RunOnMacAsync(async () =>
    {
        using var scene = await ShowAsync();
        var center = Center(scene.Button);

        // Read as a window position this lands on the button; as the screen point it is, it lies far outside.
        var misread = new CocoaPoint(center.X, scene.Window.ClientSize.Height - center.Y);
        Assert.IsFalse(WindowFrame(scene.Window).Contains(new Point(misread.X, misread.Y)), "the probe point must lie outside the window");
        PostUnowned(MOUSE_MOVED, misread);
        await Task.Delay(EVENT_MS);

        Assert.IsFalse(scene.Button.IsMouseOver, "a move over another app hovered a control inside the window");
    });

    [TestMethod]
    public Task UnownedMove_OverTheButton_HoversItAndLeavingClearsIt() => RunOnMacAsync(async () =>
    {
        using var scene = await ShowAsync();

        PostUnowned(MOUSE_MOVED, ScreenPointOf(scene.Window, Center(scene.Button)));
        Assert.IsTrue(await WaitUntilAsync(() => scene.Button.IsMouseOver), "a move with no window over the button is still a move over the button");

        var frame = WindowFrame(scene.Window);
        PostUnowned(MOUSE_MOVED, new CocoaPoint(frame.X - 40, frame.Y + frame.Height / 2));
        Assert.IsTrue(await WaitUntilAsync(() => !scene.Button.IsMouseOver), "leaving the window cleared the hover");
    });

    [TestMethod]
    public Task UnownedDrag_PastTheWindowEdge_KeepsMovingTheCapturedSlider() => RunOnMacAsync(async () =>
    {
        using var scene = await ShowAsync();
        var bounds = scene.Slider.Bounds;
        var start = new Point(bounds.X + 4, bounds.Y + bounds.Height / 2);

        Post(LEFT_MOUSE_DOWN, new CocoaPoint(start.X, scene.Window.ClientSize.Height - start.Y), WindowNumber(scene.Window), clickCount: 1, pressure: 1);
        await Task.Delay(EVENT_MS);

        var frame = WindowFrame(scene.Window);
        var outsideRight = ScreenPointOf(scene.Window, start);
        outsideRight = new CocoaPoint(frame.X + frame.Width + 80, outsideRight.Y);
        PostUnowned(LEFT_MOUSE_DRAGGED, outsideRight);
        await Task.Delay(EVENT_MS);
        double dragged = scene.Slider.Value;

        PostUnowned(LEFT_MOUSE_UP, outsideRight, clickCount: 1);
        await Task.Delay(EVENT_MS);

        Assert.AreEqual(scene.Slider.Maximum, dragged, 0.5, "a drag past the right edge pushed the slider to its maximum");
        Assert.IsFalse(scene.Window.HasMouseCapture, "the release outside the window ended the capture");
    });

    private static async Task<Scene> ShowAsync()
    {
        var button = new Button { Content = new TextBlock { Text = "Hover me" }, Width = 120, Height = 40 };
        button.ToolTip = new TextBlock { Text = "Tooltip" };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 0, Width = 200, Height = 30 };
        var window = new Window
        {
            Title = "MacUnownedMouseEvent",
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(320, 220),
            Content = new StackPanel { Margin = new Thickness(40) }.Children(button, slider),
        };

        // Away from the screen's bottom-left, where a misread screen point would land inside the client.
        var reference = MainScreenFrame();
        window.Show();
        window.MoveTo(reference.Width - 420, 80);
        window.Activate();
        await Task.Delay(SETTLE_MS);
        return new Scene { Window = window, Button = button, Slider = slider };
    }

    /// <summary>
    /// Waits for a condition the posted events should bring about. A hover opens the tooltip's native window,
    /// and the first one takes long enough to create that a later event is still queued after a fixed wait.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return condition();
    }

    private static Point Center(UIElement element)
        => new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    /// <summary>A client point of the window as a Cocoa screen point (points, bottom-left origin of the main screen).</summary>
    private static CocoaPoint ScreenPointOf(Window window, Point client)
    {
        var screenPx = window.ClientToScreen(client);
        double scale = MainScreenScale();
        var reference = MainScreenFrame();
        return new CocoaPoint(screenPx.X / scale, reference.Y + reference.Height - screenPx.Y / scale);
    }

    /// <summary>The first screen, whose bottom-left corner is the origin of Cocoa screen coordinates.</summary>
    private static nint MainScreen()
        => SendIndex(Send(objc_getClass("NSScreen"), Sel("screens")), Sel("objectAtIndex:"), 0);

    private static NSRect MainScreenFrame() => SendRect(MainScreen(), Sel("frame"));

    private static double MainScreenScale()
    {
        double scale = SendDouble(MainScreen(), Sel("backingScaleFactor"));
        return scale > 0 ? scale : 1.0;
    }

    private static Rect WindowFrame(Window window)
    {
        var frame = SendRect(NsWindow(window), Sel("frame"));
        return new Rect(frame.X, frame.Y, frame.Width, frame.Height);
    }

    private static nint WindowNumber(Window window) => Send(NsWindow(window), Sel("windowNumber"));

    private static void PostUnowned(nuint type, CocoaPoint screenPoint, nint clickCount = 0)
        => Post(type, screenPoint, windowNumber: 0, clickCount, pressure: 0);

    private static void Post(nuint type, CocoaPoint location, nint windowNumber, nint clickCount, float pressure)
    {
        nint mouseEvent = SendMouseEvent(
            objc_getClass("NSEvent"),
            Sel("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            type, location, 0, 0, windowNumber, 0, 0, clickCount, pressure);
        Assert.AreNotEqual((nint)0, mouseEvent, $"NSEvent refused event type {type}.");

        nint application = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
        SendPostEvent(application, Sel("postEvent:atStart:"), mouseEvent, false);
    }

    private static Task RunOnMacAsync(Func<Task> body)
    {
        if (!OperatingSystem.IsMacOS() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real macOS application loop.");
        }

        return RealAppSession.RunAsync(body);
    }

    private static nint NsWindow(Window window)
    {
        var backend = typeof(Window).GetProperty("Backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (nint)backend.GetType().GetField("_nsWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
    }

    private static nint Sel(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CocoaPoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport(OBJC)] private static extern nint objc_getClass(string name);
    [DllImport(OBJC)] private static extern nint sel_registerName(string name);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern NSRect SendRect(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint SendIndex(nint receiver, nint selector, nuint index);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern double SendDouble(nint receiver, nint selector);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint SendMouseEvent(nint receiver, nint selector, nuint type, CocoaPoint location, nuint modifierFlags,
        double timestamp, nint windowNumber, nint context, nint eventNumber, nint clickCount, float pressure);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern void SendPostEvent(nint receiver, nint selector, nint mouseEvent, [MarshalAs(UnmanagedType.I1)] bool atStart);
}
