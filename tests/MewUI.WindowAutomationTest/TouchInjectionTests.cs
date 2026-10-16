using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Touch the system itself delivers to a real window. On Windows contacts are injected with InjectTouchInput
/// and arrive as WM_POINTER messages; on X11 they come from a uinput touchscreen and arrive as XInput2 touch
/// sequences. Either way the mouse events the system would make up from them have to be kept out.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TouchInjectionTests
{
    private const int SETTLE_MS = 150;

    private sealed class Scene : IDisposable
    {
        public required Window Window { get; init; }
        public required ScrollViewer Viewer { get; init; }
        public required Button Button { get; init; }
        public required Border Target { get; init; }
        public required ComboBox Combo { get; init; }
        public int Clicks;
        public int TouchPresses;
        public int MousePresses;
        public int TouchRightPresses;
        public int MouseRightPresses;
        public Point LastTargetPress;

        public void Dispose() => Window.Close();
    }

    private static Scene Open(string title)
    {
        var button = new Button { Content = new TextBlock { Text = "Tap" }, Height = 60 };
        var target = new Border { Background = Color.FromRgb(30, 60, 90), Height = 120 };
        var combo = new ComboBox().Items(["One", "Two", "Three"]);
        var viewer = new ScrollViewer
        {
            Content = new StackPanel().Children(button, target, combo, new Border { Height = 3000 }),
        };

        var window = new Window
        {
            Title = title,
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(420, 420),
            Content = viewer,
        };

        var scene = new Scene { Window = window, Viewer = viewer, Button = button, Target = target, Combo = combo };
        button.Click += () => scene.Clicks++;
        button.MouseDown += args =>
        {
            if (args.PointerType == PointerType.Touch) scene.TouchPresses++;
            if (args.PointerType == PointerType.Mouse) scene.MousePresses++;
        };
        target.MouseDown += args =>
        {
            scene.LastTargetPress = args.Position;
            if (args.Button != MouseButton.Right)
            {
                return;
            }

            if (args.PointerType == PointerType.Touch) scene.TouchRightPresses++;
            if (args.PointerType == PointerType.Mouse) scene.MouseRightPresses++;
        };
        return scene;
    }

    private static async Task<Scene> ShowAsync(string title)
    {
        var scene = Open(title);
        scene.Window.Show();
        scene.Window.MoveTo(200, 120);
        await Task.Delay(400);
        return scene;
    }

    private static Point Center(UIElement element)
        => new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    private static bool RequireInjection()
    {
        if (!TouchInjector.TryInitialize(out string reason))
        {
            Assert.Inconclusive(reason);
            return false;
        }

        return true;
    }

    [TestMethod]
    public async Task Tap_ClicksOnceAndTheSystemMakesUpNoMouseClick()
    {
        if (!RequireInjection()) return;

        await RealAppSession.RunAsync(async () =>
        {
            using var scene = await ShowAsync("TouchTap");
            await TouchInjector.TapAsync(scene.Window, Center(scene.Button));
            await Task.Delay(SETTLE_MS);

            Assert.AreEqual(1, scene.Clicks, "the tap clicks the button once");
            Assert.AreEqual(1, scene.TouchPresses, "the press arrived as touch");
            Assert.AreEqual(0, scene.MousePresses, "no mouse press was made up from the contact");
        });
    }

    [TestMethod]
    public async Task Pan_ScrollsAndCoastsAfterTheFingerLifts()
    {
        if (!RequireInjection()) return;

        await RealAppSession.RunAsync(async () =>
        {
            using var scene = await ShowAsync("TouchPan");
            var start = new Point(210, 330);
            await TouchInjector.FlickAsync(scene.Window, start, new Point(210, 90), steps: 12, stepMs: 8);
            double atRelease = scene.Viewer.VerticalOffset;
            await Task.Delay(400);
            double afterCoast = scene.Viewer.VerticalOffset;

            Assert.IsGreaterThan(100.0, atRelease, $"the drag scrolled the content (offset {atRelease})");
            Assert.IsGreaterThan(atRelease, afterCoast, "the content kept moving after the finger lifted");
            Assert.AreEqual(0, scene.Clicks, "the drag clicked nothing");
            Assert.AreEqual(0, scene.MousePresses, "no mouse press was made up from the contact");
        });
    }

    [TestMethod]
    public async Task Hold_RightClicksOnceWithoutTheSystemRightClick()
    {
        if (!RequireInjection()) return;

        await RealAppSession.RunAsync(async () =>
        {
            using var scene = await ShowAsync("TouchHold");
            var point = Center(scene.Target);
            await TouchInjector.DownAsync(scene.Window, point);
            await Task.Delay(800);
            await TouchInjector.UpAsync(scene.Window, point);
            await Task.Delay(SETTLE_MS);

            Assert.AreEqual(1, scene.TouchRightPresses, "the hold right-clicks once");
            Assert.AreEqual(0, scene.MouseRightPresses, "the system's own hold right click is off");
        });
    }

    [TestMethod]
    public async Task Tap_OutsideAnOpenDropDown_ClosesIt()
    {
        if (!RequireInjection()) return;

        await RealAppSession.RunAsync(async () =>
        {
            using var scene = await ShowAsync("TouchDismiss");
            scene.Combo.IsDropDownOpen = true;
            await Task.Delay(300);
            Assert.IsTrue(scene.Combo.IsDropDownOpen, "the drop-down opened");

            await TouchInjector.TapAsync(scene.Window, Center(scene.Button));
            await Task.Delay(300);

            Assert.IsFalse(scene.Combo.IsDropDownOpen, "a tap outside closes the drop-down");
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DynamicData(nameof(MonitorMatrix.DistinctScales), typeof(MonitorMatrix),
        DynamicDataDisplayName = nameof(MonitorMatrix.ScaleName),
        DynamicDataDisplayNameDeclaringType = typeof(MonitorMatrix))]
    public async Task Tap_LandsWithinOneDipOfTheContact(MonitorProbe monitor)
    {
        if (!RequireInjection()) return;

        await RealAppSession.RunAsync(async () =>
        {
            using var scene = Open($"TouchDpi {monitor.ScalePercent}%");
            scene.Window.Show();
            MonitorProbe.SetWindowPos(scene.Window.Handle, 0,
                monitor.PixelBounds.CenterX - 200, monitor.PixelBounds.CenterY - 200, 0, 0, MonitorProbe.MOVE_ONLY);
            await Task.Delay(400);
            Assert.AreEqual(monitor.Dpi, scene.Window.Dpi, $"the window must sit on {monitor.Label}");

            var bounds = scene.Target.Bounds;
            var aimed = new Point(bounds.X + 37.3, bounds.Y + 41.7);
            await TouchInjector.TapAsync(scene.Window, aimed);
            await Task.Delay(SETTLE_MS);

            Assert.AreEqual(aimed.X, scene.LastTargetPress.X, 1.0, $"x at {monitor.ScalePercent}%");
            Assert.AreEqual(aimed.Y, scene.LastTargetPress.Y, 1.0, $"y at {monitor.ScalePercent}%");
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow(96)]
    [DataRow(144)]
    public async Task Tap_LandsWithinOneDipOfTheContact_AtXSettingsDpi(int dpi)
    {
        if (!RequireInjection()) return;

        // The desktop's XSETTINGS daemon owns the scale MewUI reads first, so the test sets it there and puts it back.
        if (!XfconfDpi.TryRead(out string? original))
        {
            Assert.Inconclusive("xfconf-query is not available to change the XSETTINGS scale.");
            return;
        }

        try
        {
            XfconfDpi.Write(dpi.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await RealAppSession.RunAsync(async () =>
            {
                using var scene = await ShowAsync($"TouchDpi {dpi}");
                for (int attempt = 0; attempt < 30 && scene.Window.Dpi != dpi; attempt++)
                {
                    await Task.Delay(100);
                }

                Assert.AreEqual((uint)dpi, scene.Window.Dpi, "the window follows the XSETTINGS scale");
                await Task.Delay(200);

                var bounds = scene.Target.Bounds;
                var aimed = new Point(bounds.X + 37.3, bounds.Y + 41.7);
                await TouchInjector.TapAsync(scene.Window, aimed);
                await Task.Delay(SETTLE_MS);

                Assert.AreEqual(aimed.X, scene.LastTargetPress.X, 1.0, $"x at {dpi} dpi");
                Assert.AreEqual(aimed.Y, scene.LastTargetPress.Y, 1.0, $"y at {dpi} dpi");
            });
        }
        finally
        {
            XfconfDpi.Restore(original);
            await Task.Delay(700);
        }
    }
}

/// <summary>Reads and sets the XFCE XSETTINGS scale (Xft/DPI).</summary>
internal static class XfconfDpi
{
    private const string ARGUMENTS = "-c xsettings -p /Xft/DPI";

    public static bool TryRead(out string? value)
    {
        value = null;
        try
        {
            var (exitCode, output) = Run(ARGUMENTS);
            if (exitCode == 0)
            {
                value = output.Trim();
            }

            // A missing property reads as a failure but is still a desktop whose scale can be set.
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static void Write(string value)
    {
        var (exitCode, output) = Run($"{ARGUMENTS} -n -t int -s {value}");
        Assert.AreEqual(0, exitCode, $"xfconf-query could not set the scale: {output}");
    }

    public static void Restore(string? original)
    {
        if (original == null)
        {
            Run($"{ARGUMENTS} -r");
        }
        else
        {
            Run($"{ARGUMENTS} -n -t int -s {original}");
        }
    }

    private static (int ExitCode, string Output) Run(string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xfconf-query", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}

/// <summary>
/// Injects one finger at client DIPs of a window: through InjectTouchInput on Windows, through a uinput
/// touchscreen on Linux.
/// </summary>
internal static class TouchInjector
{
    private enum Phase
    {
        Down,
        Move,
        Up,
    }

    private const uint TOUCH_FEEDBACK_NONE = 3;
    private const uint PT_TOUCH = 2;
    private const uint POINTER_FLAG_INRANGE = 0x00000002;
    private const uint POINTER_FLAG_INCONTACT = 0x00000004;
    private const uint POINTER_FLAG_DOWN = 0x00010000;
    private const uint POINTER_FLAG_UPDATE = 0x00020000;
    private const uint POINTER_FLAG_UP = 0x00040000;
    private const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
    private const int CONTACT_RADIUS_PX = 2;

    private static bool? _initialized;
    private static int _error;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public nint SourceDevice;
        public nint HwndTarget;
        public POINT PtPixelLocation;
        public POINT PtHimetricLocation;
        public POINT PtPixelLocationRaw;
        public POINT PtHimetricLocationRaw;
        public uint DwTime;
        public uint HistoryCount;
        public int InputData;
        public uint DwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO PointerInfo;
        public uint TouchFlags;
        public uint TouchMask;
        public RECT RcContact;
        public RECT RcContactRaw;
        public uint Orientation;
        public uint Pressure;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);

    public static bool TryInitialize(out string reason)
    {
        if (OperatingSystem.IsLinux())
        {
            return UinputTouchscreen.TryGet(out _, out reason);
        }

        if (!OperatingSystem.IsWindows())
        {
            reason = "Touch injection runs on Windows and Linux (X11).";
            return false;
        }

        if (_initialized == null)
        {
            _initialized = InitializeTouchInjection(1, TOUCH_FEEDBACK_NONE);
            _error = _initialized == true ? 0 : Marshal.GetLastWin32Error();
        }

        reason = $"InitializeTouchInjection failed with error {_error}; this session cannot inject touch.";
        return _initialized == true;
    }

    public static async Task DownAsync(Window window, Point client)
    {
        Inject(window, client, Phase.Down);
        await Task.Delay(30);
    }

    public static async Task UpAsync(Window window, Point client)
    {
        Inject(window, client, Phase.Up);
        await Task.Delay(30);
    }

    public static async Task TapAsync(Window window, Point client)
    {
        await DownAsync(window, client);
        await UpAsync(window, client);
    }

    /// <summary>Drags from one client point to another in even steps and lifts while still moving.</summary>
    public static async Task FlickAsync(Window window, Point from, Point to, int steps, int stepMs)
    {
        Inject(window, from, Phase.Down);
        await Task.Delay(stepMs);
        for (int step = 1; step <= steps; step++)
        {
            double fraction = (double)step / steps;
            var point = new Point(from.X + (to.X - from.X) * fraction, from.Y + (to.Y - from.Y) * fraction);
            Inject(window, point, Phase.Move);
            await Task.Delay(stepMs);
        }

        Inject(window, to, Phase.Up);
        await Task.Delay(30);
    }

    private static void Inject(Window window, Point client, Phase phase)
    {
        var screen = window.ClientToScreen(client);
        int x = (int)Math.Round(screen.X);
        int y = (int)Math.Round(screen.Y);
        if (OperatingSystem.IsLinux())
        {
            UinputTouchscreen.TryGet(out var device, out _);
            switch (phase)
            {
                case Phase.Down:
                    device!.Down(x, y);
                    break;

                case Phase.Move:
                    device!.Move(x, y);
                    break;

                default:
                    device!.Up();
                    break;
            }

            return;
        }

        uint flags = phase switch
        {
            Phase.Down => POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT,
            Phase.Move => POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT,
            _ => POINTER_FLAG_UP,
        };
        var contact = new POINTER_TOUCH_INFO
        {
            PointerInfo = new POINTER_INFO
            {
                PointerType = PT_TOUCH,
                PointerId = 0,
                PointerFlags = flags,
                PtPixelLocation = new POINT { X = x, Y = y },
            },
            TouchMask = TOUCH_MASK_CONTACTAREA,
            RcContact = new RECT
            {
                Left = x - CONTACT_RADIUS_PX,
                Top = y - CONTACT_RADIUS_PX,
                Right = x + CONTACT_RADIUS_PX,
                Bottom = y + CONTACT_RADIUS_PX,
            },
        };

        if (!InjectTouchInput(1, [contact]))
        {
            Assert.Fail($"InjectTouchInput failed with error {Marshal.GetLastWin32Error()} (flags 0x{flags:X}).");
        }
    }
}
