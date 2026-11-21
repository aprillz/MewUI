using System.Diagnostics;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A transparent window asking for a backdrop material gets it from the system compositor: sharp stripes in a
/// window behind it come through blurred, and the window reports the material it got. Without transparency, or
/// with the material taken away again, the stripes behind stay sharp. Runs on Windows, on macOS with the screen
/// unlocked, and on X11 under KDE's compositor (the only X11 compositor with a blur a window can ask for).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowSystemBackdropTests
{
    private const int STRIPES_DIP = 420;
    private const int WINDOW_DIP = 240;
    private const int STRIPE_DIP = 6;
    private const int BLOCK_DIP = 30;
    private const int MENU_WIDTH_DIP = 180;
    private const int MENU_INSET_DIP = 14;
    private static readonly Color BlockColor = Color.FromRgb(230, 40, 40);
    private static readonly Color FallbackColor = Color.FromRgb(30, 200, 70);

    [TestMethod]
    public Task Blur_BlursWhatIsBehindTheWindow() => RunAsync(async (window, measure) =>
    {
        double sharp = await measure();
        AssertStripesSeen(window, sharp);
        window.SystemBackdrop = SystemBackdrop.Blur;
        await Task.Delay(800);
        if (ReportsNoMaterial(window))
        {
            return;
        }

        AssertContentShows(window);
        double blurred = await measure();

        Assert.AreEqual(SystemBackdrop.Blur, window.ActualSystemBackdrop, "the window did not get the blur");
        Assert.IsLessThan(sharp * 0.3, blurred, $"{window.GraphicsFactory.Backend}: the stripes behind read {blurred:F1} under the blur against {sharp:F1} without it");
    });

    [TestMethod]
    public Task Acrylic_BlursWhatIsBehindTheWindow() => RunAsync(async (window, measure) =>
    {
        double sharp = await measure();
        AssertStripesSeen(window, sharp);
        window.SystemBackdrop = SystemBackdrop.Acrylic;
        await Task.Delay(800);
        if (ReportsNoMaterial(window))
        {
            return;
        }

        AssertContentShows(window);
        double blurred = await measure();

        // A platform without an acrylic of its own lowers it to its blur.
        Assert.AreNotEqual(SystemBackdrop.None, window.ActualSystemBackdrop, "the window got no material");
        Assert.IsLessThan(sharp * 0.3, blurred, $"{window.GraphicsFactory.Backend}: the stripes behind read {blurred:F1} under the acrylic against {sharp:F1} without it");
    });

    [TestMethod]
    public Task Mica_HidesTheWindowsBehind() => RunAsync(async (window, measure) =>
    {
        double sharp = await measure();
        AssertStripesSeen(window, sharp);
        window.SystemBackdrop = SystemBackdrop.Mica;
        await Task.Delay(800);
        if (ReportsNoMaterial(window))
        {
            return;
        }

        AssertContentShows(window);
        double covered = await measure();

        // Mica needs Windows 11; older systems and other platforms lower it to a material that blurs the stripes too.
        Assert.AreNotEqual(SystemBackdrop.None, window.ActualSystemBackdrop, "the window got no material");
        if (OperatingSystem.IsMacOS())
        {
            // Mica is a Windows material; macOS has none of its own to report under that name.
            Assert.AreEqual(SystemBackdrop.Acrylic, window.ActualSystemBackdrop);
        }

        Assert.IsLessThan(sharp * 0.3, covered, $"{window.GraphicsFactory.Backend}: the stripes behind read {covered:F1} under {window.ActualSystemBackdrop} against {sharp:F1} without it");
    });

    [TestMethod]
    public Task TakingTheMaterialAway_ShowsTheStripesAgain() => RunAsync(async (window, measure) =>
    {
        double sharp = await measure();
        AssertStripesSeen(window, sharp);
        window.SystemBackdrop = SystemBackdrop.Blur;
        await Task.Delay(800);
        if (ReportsNoMaterial(window))
        {
            return;
        }

        window.SystemBackdrop = SystemBackdrop.None;
        await Task.Delay(800);
        double again = await measure();

        Assert.AreEqual(SystemBackdrop.None, window.ActualSystemBackdrop);
        Assert.IsGreaterThan(sharp * 0.8, again, $"{window.GraphicsFactory.Backend}: the stripes read {again:F1} once the blur was gone against {sharp:F1} before it");
    });

    [TestMethod]
    public async Task AMaterialSetBeforeTheWindowShows_KeepsTheContentShowing()
    {
        if (!RealAppSession.IsAvailable || (OperatingSystem.IsLinux() && !IsKdeSession()))
        {
            Assert.Inconclusive("Needs the real application loop and a compositor with a blur.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            var window = new Window
            {
                Title = "SystemBackdropBeforeShow",
                StartupLocation = WindowStartupLocation.Manual,
                AllowsTransparency = true,
                Background = Color.Transparent,
                Padding = new Thickness(0),
                Borderless = true,
                SystemBackdrop = SystemBackdrop.Acrylic,
                WindowSize = WindowSize.Fixed(WINDOW_DIP, WINDOW_DIP),
                Content = new Border
                {
                    Width = BLOCK_DIP,
                    Height = BLOCK_DIP,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = BlockColor,
                },
            };

            try
            {
                window.Show();
                window.MoveTo(160, 160);
                await Task.Delay(900);
                if (!ReportsNoMaterial(window))
                {
                    Assert.AreNotEqual(SystemBackdrop.None, window.ActualSystemBackdrop, "the window got no material");
                    AssertContentShows(window);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public Task AnOpaqueWindow_GetsNoMaterial() => RunAsync(async (window, _) =>
    {
        window.AllowsTransparency = false;
        window.SystemBackdrop = SystemBackdrop.Blur;
        await Task.Delay(400);

        Assert.AreEqual(SystemBackdrop.None, window.ActualSystemBackdrop);
    }, transparent: false);

    [TestMethod]
    public async Task AnX11CompositorWithoutBlur_ReportsNoMaterial()
    {
        if (!OperatingSystem.IsLinux() || IsKdeSession() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs an X11 session whose compositor offers no blur.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            var window = new Window
            {
                Title = "SystemBackdropWithoutBlur",
                AllowsTransparency = true,
                Background = Color.Transparent,
                SystemBackdrop = SystemBackdrop.Acrylic,
                WindowSize = WindowSize.Fixed(WINDOW_DIP, WINDOW_DIP),
            };

            try
            {
                window.Show();
                await Task.Delay(400);
                Assert.AreEqual(SystemBackdrop.None, window.ActualSystemBackdrop);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public Task AWindowWithoutItsMaterial_DrawsTheFallbackBrush() => RunAsync(async (window, _) =>
    {
        window.BackdropFallbackBrush = FallbackColor;
        window.SystemBackdrop = SystemBackdrop.Blur;
        await Task.Delay(500);

        Assert.AreEqual(SystemBackdrop.None, window.ActualSystemBackdrop);
        AssertShowsFallback(window, WINDOW_DIP / 2.0, WINDOW_DIP / 4.0, WINDOW_DIP / 2.0);
    }, transparent: false);

    [TestMethod]
    public Task AContextMenuInItsOwnWindow_HasTheMaterialOnItsBox() => RunAsync(async (window, measure) =>
    {
        double sharp = await measure();
        AssertStripesSeen(window, sharp);

        var menu = CreateMaterialMenu(SystemBackdrop.Blur);
        menu.BackdropFallbackBrush = FallbackColor;
        // On macOS a popup hides while its application is inactive; activating the popup itself would dismiss it.
        window.Activate();
        await Task.Delay(200);
        menu.Show(window, new Point(20, 50));
        try
        {
            await Task.Delay(900);
            var chrome = (PopupChrome)menu.Parent!;
            var surface = chrome.HostSurface;
            Assert.IsNotNull(surface, "the menu is not in a window of its own");
            Assert.AreNotEqual((nint)0, surface.Handle, "the menu's window closed before it could be read");

            var padding = PopupChrome.ShadowPadding;
            double row = padding.Top + menu.Bounds.Height / 2;
            double start = padding.Left + MENU_INSET_DIP;
            double span = menu.Bounds.Width - MENU_INSET_DIP * 2;
            if (ReportsNoMaterial(surface))
            {
                AssertShowsFallback(surface, row, start, span);
                return;
            }

            Assert.AreNotEqual(SystemBackdrop.None, surface.ActualSystemBackdrop, "the menu's window got no material");
            double blurred = SharpnessOf(surface, row, start, span);
            Assert.IsLessThan(sharp * 0.3, blurred, $"{surface.GraphicsFactory.Backend}: the stripes behind the menu read {blurred:F1} against {sharp:F1} without it");
        }
        finally
        {
            window.CloseAllPopups();
        }
    });

    [TestMethod]
    [DataRow(SystemBackdrop.Blur)]
    [DataRow(SystemBackdrop.Acrylic)]
    [DataRow(SystemBackdrop.Mica)]
    public async Task AContextMenuInTheOwnerSurface_BlursWhatTheSurfaceHolds(SystemBackdrop material)
    {
        if (!RealAppSession.IsAvailable || (OperatingSystem.IsLinux() && !IsKdeSession()))
        {
            Assert.Inconclusive("Needs the real application loop and, on X11, KDE's screen capture.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            bool preferredNative = PopupManager.PreferNativePopups;
            PopupManager.PreferNativePopups = false;
            var owner = new Window
            {
                Title = "SystemBackdropOverlayMenu",
                StartupLocation = WindowStartupLocation.Manual,
                Background = Color.White,
                Padding = new Thickness(0),
                Borderless = true,
                WindowSize = WindowSize.Fixed(STRIPES_DIP, STRIPES_DIP),
                Content = new Stripes(),
            };

            try
            {
                owner.Show();
                owner.MoveTo(120, 120);
                await Task.Delay(600);

                var menu = CreateMaterialMenu(material);
                menu.Show(owner, new Point(40, 40));
                await Task.Delay(600);
                Assert.IsNull(((PopupChrome)menu.Parent!).HostSurface, "the menu is not drawn inside the owner's surface");

                var box = menu.Bounds;
                double row = box.Y + box.Height / 2;
                double start = box.X + MENU_INSET_DIP;
                double span = box.Width - MENU_INSET_DIP * 2;
                double sharp = SharpnessOf(owner, box.Bottom + 40, start, span);
                AssertStripesSeen(owner, sharp);
                double blurred = SharpnessOf(owner, row, start, span);
                Assert.IsLessThan(sharp * 0.3, blurred, $"{owner.GraphicsFactory.Backend}: the stripes under the {material} menu read {blurred:F1} against {sharp:F1} beside it");
            }
            finally
            {
                owner.CloseAllPopups();
                owner.Close();
                PopupManager.PreferNativePopups = preferredNative;
            }
        });
    }

    /// <summary>A menu whose rows draw no text and whose fill is clear, so what shows in its box is what is behind it.</summary>
    private static ContextMenu CreateMaterialMenu(SystemBackdrop material)
    {
        var menu = new ContextMenu
        {
            SystemBackdrop = material,
            Background = Color.Transparent,
            Width = MENU_WIDTH_DIP,
        };
        menu.AddItem(" ");
        menu.AddItem(" ");
        menu.AddItem(" ");
        return menu;
    }

    private static void AssertShowsFallback(Window window, double y, double x, double span)
    {
        var (row, width) = CaptureRow(window, y, x, span);
        int middle = width / 2 * 4;
        byte blue = row[middle], green = row[middle + 1], red = row[middle + 2];
        Assert.IsTrue(green > 150 && red < 90 && blue < 110,
            $"{window.GraphicsFactory.Backend}: the fallback place read ({red}, {green}, {blue})");
    }

    /// <summary>Shows a striped window and a transparent window over it, and hands over a measure of how sharp the stripes look through it.</summary>
    private static async Task RunAsync(Func<Window, Func<Task<double>>, Task> body, bool transparent = true)
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
            return;
        }

        if (OperatingSystem.IsLinux() && !IsKdeSession())
        {
            Assert.Inconclusive("Only KDE's compositor offers a blur on X11.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            if (OperatingSystem.IsWindows())
            {
                WakeDisplay();
            }

            var stripes = new Window
            {
                Title = "SystemBackdropStripes",
                StartupLocation = WindowStartupLocation.Manual,
                Background = Color.White,
                Padding = new Thickness(0),
                WindowSize = WindowSize.Fixed(STRIPES_DIP, STRIPES_DIP),
                Content = new Stripes(),
            };
            var window = new Window
            {
                Title = "SystemBackdrop",
                StartupLocation = WindowStartupLocation.Manual,
                AllowsTransparency = transparent,
                Background = Color.Transparent,
                Padding = new Thickness(0),
                Borderless = true,
                WindowSize = WindowSize.Fixed(WINDOW_DIP, WINDOW_DIP),
                // A solid block in a corner: the window's own content, which has to stay over the material.
                Content = new Border
                {
                    Width = BLOCK_DIP,
                    Height = BLOCK_DIP,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = BlockColor,
                },
            };

            try
            {
                stripes.Show();
                stripes.MoveTo(100, 100);
                await Task.Delay(500);
                window.Show();
                window.MoveTo(160, 160);
                await Task.Delay(800);

                await body(window, () => Task.FromResult(Sharpness(window)));
            }
            finally
            {
                window.Close();
                stripes.Close();
            }
        });
    }

    /// <summary>
    /// A Windows window that hands its pixels over as a layered bitmap cannot stand on a material and has to say so;
    /// true when this backend is one of those, after checking that it reported none.
    /// </summary>
    private static bool ReportsNoMaterial(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.GraphicsFactory.Backend.StartsWith("Direct2D", StringComparison.Ordinal))
        {
            return false;
        }

        Assert.AreEqual(SystemBackdrop.None, window.ActualSystemBackdrop, $"{window.GraphicsFactory.Backend}: a layered window reported a material it cannot show");
        return true;
    }

    /// <summary>The window's own content has to show over the material, not be hidden under it.</summary>
    private static void AssertContentShows(Window window)
    {
        var (row, width) = CaptureRow(window, BLOCK_DIP / 2.0, 0, BLOCK_DIP);
        int middle = width / 2 * 4;
        byte blue = row[middle], green = row[middle + 1], red = row[middle + 2];
        Assert.IsTrue(red > 180 && green < 90 && blue < 90,
            $"{window.GraphicsFactory.Backend}: the block in the window read ({red}, {green}, {blue}) over {window.ActualSystemBackdrop}; the window's content is not showing");
    }

    /// <summary>Without a material the capture has to cross the stripes; otherwise it read the wrong place and proves nothing.</summary>
    private static void AssertStripesSeen(Window window, double sharpness)
    {
        // A full black-to-white step once per stripe averages 255 over the stripe's width in device pixels.
        double sharpStripes = 255.0 / (STRIPE_DIP * window.DpiScale);
        Assert.IsGreaterThan(sharpStripes * 0.6, sharpness, $"the capture saw no stripes (sharpness {sharpness:F1}, sharp stripes {sharpStripes:F1}); it did not read the window's place on the screen");
    }

    /// <summary>Mean brightness step between neighbouring pixels across the stripes, along the middle half of the window's middle row.</summary>
    private static double Sharpness(Window window)
    {
        var (row, width) = CaptureMiddleRow(window);
        return SharpnessOf(row, width);
    }

    private static double SharpnessOf(Window window, double y, double x, double span)
    {
        var (row, width) = CaptureRow(window, y, x, span);
        return SharpnessOf(row, width);
    }

    private static double SharpnessOf(byte[] row, int width)
    {
        double total = 0;
        for (int column = 1; column < width; column++)
        {
            int offset = column * 4;
            int current = row[offset] + row[offset + 1] + row[offset + 2];
            int previous = row[offset - 4] + row[offset - 3] + row[offset - 2];
            total += Math.Abs(current - previous) / 3.0;
        }

        return total / (width - 1);
    }

    /// <summary>The screen's BGRA pixels along the middle half of the window's middle row.</summary>
    private static (byte[] Row, int Width) CaptureMiddleRow(Window window)
        => CaptureRow(window, WINDOW_DIP / 2.0, WINDOW_DIP / 4.0, WINDOW_DIP / 2.0);

    /// <summary>The screen's BGRA pixels along one row of the window, <paramref name="y"/> DIPs down, from <paramref name="x"/> across <paramref name="span"/> DIPs.</summary>
    private static (byte[] Row, int Width) CaptureRow(Window window, double y, double x, double span)
    {
        if (OperatingSystem.IsWindows())
        {
            GetWindowRect(window.Handle, out var rect);
            double windowScale = window.DpiScale;
            var shot = ScreenCapture.OfScreen(rect.Left + (int)(x * windowScale), rect.Top + (int)(y * windowScale), Math.Max(1, (int)(span * windowScale)), 1);
            return (shot.Bgra, shot.Width);
        }

        // Window positions are in points (macOS) or pixels at the window's scale (X11); the capture is in device pixels.
        var position = window.Position;
        double scale = window.DpiScale;
        string path = Path.Combine(Path.GetTempPath(), $"backdrop-material-{Guid.NewGuid():N}.png");
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                int left = (int)(position.X + x);
                int top = (int)(position.Y + y);
                Run("screencapture", $"-x -t png -R{left},{top},{(int)span},1 \"{path}\"");
                var shot = Decode(path);
                return (shot.Data, shot.WidthPx);
            }

            Run("spectacle", $"--background --nonotify --fullscreen --output \"{path}\"");
            var screen = Decode(path);
            int screenX = (int)((position.X + x) * scale);
            int screenY = (int)((position.Y + y) * scale);
            int pixelSpan = Math.Max(1, (int)(span * scale));
            var pixels = new byte[pixelSpan * 4];
            Buffer.BlockCopy(screen.Data, (screenY * screen.WidthPx + screenX) * 4, pixels, 0, pixels.Length);
            return (pixels, pixelSpan);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Bgra32PixelBuffer Decode(string path)
    {
        Bgra32PixelBuffer bitmap = default;
        if (!File.Exists(path) || !new PngDecoder().TryDecode(File.ReadAllBytes(path), out bitmap))
        {
            Assert.Inconclusive($"the screen capture wrote no readable image to {path}");
        }

        return bitmap;
    }

    private static void Run(string tool, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(tool, arguments) { UseShellExecute = false })!;
        process.WaitForExit(15000);
    }

    private static bool IsKdeSession()
        => (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty).Contains("KDE", StringComparison.OrdinalIgnoreCase);

    /// <summary>A display gone to sleep shows only window backgrounds to a screen capture, so the run wakes it first.</summary>
    private static void WakeDisplay()
    {
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;
        const uint ES_DISPLAY_REQUIRED = 0x00000002;
        const uint MOUSEEVENTF_MOVE = 0x0001;
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        mouse_event(MOUSEEVENTF_MOVE, 1, 0, 0, 0);
        mouse_event(MOUSEEVENTF_MOVE, unchecked((uint)-1), 0, 0, 0);
    }

    /// <summary>Black and white vertical stripes over the whole element.</summary>
    private sealed class Stripes : FrameworkElement
    {
        protected override void OnRender(IGraphicsContext context)
        {
            for (double left = 0; left < Bounds.Width; left += STRIPE_DIP * 2)
            {
                context.FillRectangle(new Rect(left, 0, STRIPE_DIP, Bounds.Height), Color.Black);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nint extraInfo);
}
