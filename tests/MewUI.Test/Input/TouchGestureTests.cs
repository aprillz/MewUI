using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Input;

/// <summary>
/// Raw touch contacts become the pointer events controls understand: a tap clicks, a drag past the
/// threshold scrolls and coasts, a hold right-clicks, and a second finger is not routed.
/// Not parallelizable: the coast runs on the process-wide animation manager, and input routing shares
/// static drag state.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TouchGestureTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    private sealed class Contacts(Window window)
    {
        public long Now { get; private set; } = Stopwatch.GetTimestamp();

        public void Advance(double milliseconds) => Now += (long)(milliseconds * Stopwatch.Frequency / 1000);

        public void Down(int id, Point point)
            => WindowInputRouter.PointerDown(window, id, PointerType.Touch, point, point, Now, ModifierKeys.None);

        public void Move(int id, Point point)
            => WindowInputRouter.PointerMove(window, id, PointerType.Touch, point, point, Now, ModifierKeys.None);

        public void Up(int id, Point point)
            => WindowInputRouter.PointerUp(window, id, PointerType.Touch, point, point, Now, ModifierKeys.None);

        public void Tap(int id, Point point)
        {
            Down(id, point);
            Advance(50);
            Up(id, point);
        }

        /// <summary>Drags straight up from <paramref name="start"/> in steps of <paramref name="step"/> DIPs, one per 10ms, and lifts.</summary>
        public void FlickUp(int id, Point start, int steps, double step)
        {
            Down(id, start);
            var point = start;
            for (int index = 0; index < steps; index++)
            {
                Advance(10);
                point = new Point(point.X, point.Y - step);
                Move(id, point);
            }

            Up(id, point);
        }
    }

    private sealed class Scene
    {
        public required Window Window { get; init; }
        public required ScrollViewer Viewer { get; init; }
        public required Button Button { get; init; }
        public required Border Target { get; init; }
        public required Slider Slider { get; init; }
        public int Clicks { get; set; }
        public int RightPresses { get; set; }
    }

    private static Scene CreateScene()
    {
        var button = new Button { Content = new TextBlock { Text = "Tap" }, Height = 40 };
        var target = new Border { Background = Color.FromRgb(20, 40, 60), Height = 40 };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 0, Height = 30 };
        var viewer = new ScrollViewer
        {
            Content = new StackPanel().Children(button, target, slider, new Border { Height = 2000 }),
        };

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = viewer;
        window.PerformLayout();

        var scene = new Scene { Window = window, Viewer = viewer, Button = button, Target = target, Slider = slider };
        button.Click += () => scene.Clicks++;
        target.MouseDown += args =>
        {
            if (args.Button == MouseButton.Right)
            {
                scene.RightPresses++;
            }
        };
        return scene;
    }

    private static void Pulse(long timestamp)
    {
        AnimationManager.Instance.UpdateAt(timestamp);
    }

    [TestMethod]
    public void Tap_OnAButton_ClicksOnceAndScrollsNothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);

        contacts.Tap(1, scene.Button.CenterOf());

        Assert.AreEqual(1, scene.Clicks);
        Assert.AreEqual(0, scene.Viewer.VerticalOffset);
    }

    [TestMethod]
    public void Drag_PastTheThreshold_ScrollsAndDoesNotClick()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);
        var start = scene.Button.CenterOf();

        contacts.Down(1, start);
        contacts.Advance(200);
        contacts.Move(1, new Point(start.X, start.Y - 100));
        contacts.Advance(200);
        contacts.Up(1, new Point(start.X, start.Y - 100));
        scene.Window.PerformLayout();

        Assert.AreEqual(0, scene.Clicks, "the press was abandoned when the drag became a scroll");
        Assert.AreEqual(100 - PointerGestureRecognizer.PAN_THRESHOLD_DIP, scene.Viewer.VerticalOffset, 1.0,
            "the content follows the finger past the threshold");
        Assert.IsFalse(scene.Window.IsScrollCoasting, "a finger that rested before lifting does not throw the content");
    }

    [TestMethod]
    public void Drag_OnASlider_ChangesItsValueWithoutScrolling()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);
        var bounds = scene.Slider.Bounds;
        var start = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2);
        var end = new Point(bounds.X + bounds.Width - 10, start.Y - 40);

        contacts.Down(1, start);
        contacts.Advance(20);
        contacts.Move(1, end);
        contacts.Advance(20);
        contacts.Up(1, end);

        Assert.IsGreaterThan(50.0, scene.Slider.Value, "the slider captured the press and kept the movement");
        Assert.AreEqual(0, scene.Viewer.VerticalOffset);
    }

    [TestMethod]
    public void Hold_ForTheLongPressTime_RightClicksOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);
        var point = scene.Target.CenterOf();

        contacts.Down(1, point);
        contacts.Advance(PointerGestureRecognizer.LONG_PRESS_MS - 1);
        scene.Window.PointerGestures.Tick(contacts.Now);
        Assert.AreEqual(0, scene.RightPresses, "not yet");

        contacts.Advance(1);
        scene.Window.PointerGestures.Tick(contacts.Now);
        contacts.Advance(100);
        contacts.Up(1, point);

        Assert.AreEqual(1, scene.RightPresses);
    }

    [TestMethod]
    public void Hold_WhenTheTimerFiresAMomentEarly_StillRightClicks()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);
        var point = scene.Target.CenterOf();
        long pressed = contacts.Now;

        contacts.Down(1, point);
        long early = pressed + (long)((PointerGestureRecognizer.LONG_PRESS_MS - 0.5) * Stopwatch.Frequency / 1000);
        scene.Window.PointerGestures.OnLongPressTimer(early);
        Assert.AreEqual(0, scene.RightPresses, "not yet");
        Assert.IsTrue(scene.Window.PointerGestures.IsLongPressArmed, "the timer waits out the rest of the hold");

        scene.Window.PointerGestures.OnLongPressTimer(pressed + (long)(PointerGestureRecognizer.LONG_PRESS_MS * Stopwatch.Frequency / 1000));
        contacts.Advance(PointerGestureRecognizer.LONG_PRESS_MS + 100);
        contacts.Up(1, point);

        Assert.AreEqual(1, scene.RightPresses);
    }

    [TestMethod]
    public void Hold_OnAGridRowWithAContextMenu_LeavesTheMenuOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var menu = new ContextMenu().Item("First").Item("Second");
        var grid = new GridView()
            .RowHeight(30)
            .Columns(new GridViewColumn<string>().Header("Name").Width(200).Text(item => item))
            .PrepareContainer<string>((row, _, _, _) => row.ContextMenu = menu);
        grid.ItemsSource = ItemsView.Create(new[] { "One", "Two", "Three" });

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        window.PerformLayout();
        var row = (GridViewRow?)VisualTree.Find(grid, element => element is GridViewRow);
        Assert.IsNotNull(row);

        var contacts = new Contacts(window);
        var point = row.CenterOf();
        contacts.Down(1, point);
        contacts.Advance(PointerGestureRecognizer.LONG_PRESS_MS);
        window.PointerGestures.Tick(contacts.Now);
        window.PerformLayout();
        Assert.IsTrue(window.TryGetPopupOwner(menu, out _), "the hold opened the menu");

        contacts.Advance(300);
        contacts.Up(1, point);
        window.PerformLayout();
        Assert.IsTrue(window.TryGetPopupOwner(menu, out _), "lifting the finger that opened it leaves the menu open");
    }

    [TestMethod]
    public void Hold_AfterMovingPastTheThreshold_DoesNotRightClick()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);
        var point = scene.Target.CenterOf();

        contacts.Down(1, point);
        contacts.Advance(100);
        contacts.Move(1, new Point(point.X, point.Y - 20));
        contacts.Advance(PointerGestureRecognizer.LONG_PRESS_MS);
        scene.Window.PointerGestures.Tick(contacts.Now);
        contacts.Up(1, new Point(point.X, point.Y - 20));

        Assert.AreEqual(0, scene.RightPresses);
    }

    [TestMethod]
    public void Tap_DuringACoast_StopsItWithoutClicking()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);

        contacts.FlickUp(1, new Point(200, 280), steps: 10, step: 10);
        Assert.IsTrue(scene.Window.IsScrollCoasting, "a finger still moving when it lifts throws the content");

        contacts.Advance(16);
        Pulse(contacts.Now);
        scene.Window.PerformLayout();
        var buttonPoint = scene.Button.CenterOf();
        contacts.Tap(2, buttonPoint);

        Assert.IsFalse(scene.Window.IsScrollCoasting);
        Assert.AreEqual(0, scene.Clicks, "the tap was spent stopping the coast");

        double stopped = scene.Viewer.VerticalOffset;
        contacts.Advance(100);
        Pulse(contacts.Now);
        Assert.AreEqual(stopped, scene.Viewer.VerticalOffset);
    }

    [TestMethod]
    public void Drag_AfterCatchingACoast_ScrollsAgainWithoutClicking()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);

        contacts.FlickUp(1, new Point(200, 280), steps: 10, step: 10);
        contacts.Advance(16);
        Pulse(contacts.Now);
        scene.Window.PerformLayout();

        var point = new Point(200, 200);
        contacts.Down(2, point);
        Assert.IsFalse(scene.Window.IsScrollCoasting, "the press caught the coast");
        double caught = scene.Viewer.VerticalOffset;

        for (int step = 1; step <= 5; step++)
        {
            contacts.Advance(100);
            contacts.Move(2, new Point(point.X, point.Y - step * 10));
        }

        contacts.Advance(200);
        contacts.Up(2, new Point(point.X, point.Y - 50));
        scene.Window.PerformLayout();

        Assert.AreEqual(caught + 50 - PointerGestureRecognizer.PAN_THRESHOLD_DIP, scene.Viewer.VerticalOffset, 1.0,
            "the finger that caught the coast scrolls on");
        Assert.AreEqual(0, scene.Clicks, "the catching press reached no control");
    }

    [TestMethod]
    public void SecondFinger_IsNotRoutedAndCallsOffTheHold()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);

        contacts.Down(1, scene.Target.CenterOf());
        contacts.Advance(100);
        contacts.Tap(2, scene.Button.CenterOf());
        contacts.Advance(PointerGestureRecognizer.LONG_PRESS_MS);
        scene.Window.PointerGestures.Tick(contacts.Now);
        contacts.Up(1, scene.Target.CenterOf());

        Assert.AreEqual(0, scene.Clicks, "the second finger reached no control");
        Assert.AreEqual(0, scene.RightPresses, "the second finger called off the hold");
    }

    [TestMethod]
    public void Coast_CoversTheDistanceTheDecayCurveGives()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);

        // 10 DIPs every 10ms is a throw of 1000 DIP/s.
        contacts.FlickUp(1, new Point(200, 280), steps: 10, step: 10);
        double atRelease = scene.Viewer.VerticalOffset;
        long release = contacts.Now;

        const int FRAMES = 30;
        for (int frame = 1; frame <= FRAMES; frame++)
        {
            Pulse(release + (long)(frame * Stopwatch.Frequency / 60.0));
        }

        double seconds = FRAMES / 60.0;
        double expected = 1000 * (Math.Pow(ScrollGesture.COAST_DECAY_PER_SECOND, seconds) - 1)
            / Math.Log(ScrollGesture.COAST_DECAY_PER_SECOND);
        Assert.AreEqual(expected, scene.Viewer.VerticalOffset - atRelease, 1.5);
        Assert.IsTrue(scene.Window.IsScrollCoasting, "still well above the end speed at half a second");

        for (int frame = FRAMES + 1; frame <= 600 && scene.Window.IsScrollCoasting; frame++)
        {
            Pulse(release + (long)(frame * Stopwatch.Frequency / 60.0));
        }

        Assert.IsFalse(scene.Window.IsScrollCoasting, "the coast ends by itself");
        double total = 1000 / -Math.Log(ScrollGesture.COAST_DECAY_PER_SECOND);
        Assert.IsLessThan(total, scene.Viewer.VerticalOffset - atRelease);
    }

    [TestMethod]
    public void Coast_KeepsGoingWhenTwoPulsesLandInOneFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scene = CreateScene();
        var contacts = new Contacts(scene.Window);

        contacts.FlickUp(1, new Point(200, 280), steps: 10, step: 10);
        long release = contacts.Now;
        long millisecond = Stopwatch.Frequency / 1000;
        Pulse(release + 3 * millisecond);
        Pulse(release + 4 * millisecond);
        for (int frame = 1; frame <= 10; frame++)
        {
            Pulse(release + 4 * millisecond + (long)(frame * Stopwatch.Frequency / 60.0));
        }

        Assert.IsTrue(scene.Window.IsScrollCoasting, "a pulse one millisecond after another is not a 1000Hz display");
    }
}
