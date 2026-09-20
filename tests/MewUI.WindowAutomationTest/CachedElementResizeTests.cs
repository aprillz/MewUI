using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Shrinking a cached element makes the scratch pool hand it a surface taller than the content it
/// now renders. The Direct2D blit passed its source rectangle in pixels to a bridge bitmap whose
/// DPI is the surface scale, so on a scaled monitor the sampled region grew by that factor into
/// the stale band below the content, squashing the cached element vertically. A bottom stripe in
/// a different colour tells a squashed frame from a correct one; the stale band above it carries
/// the previous render's body colour, which a single-colour probe cannot distinguish.
/// </summary>
[TestClass]
public sealed class CachedElementResizeTests
{
    private const double PANEL_W_DIP = 240;
    private const double TALL_DIP = 300;
    private const double SHORT_DIP = 220;
    private const double STRIPE_DIP = 24;
    private const int FRAME_WAIT_MS = 5000;
    private const int FRAME_POLL_MS = 50;
    private const int FRAME_HOLD_MS = 200;

    [TestMethod]
    public async Task CachedPanel_KeepsItsBottomStripe_AfterShrinkingOnAScaledMonitor()
    {
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return;
        }

        var monitor = MonitorMatrix.Monitors.FirstOrDefault(m => m.ScalePercent != 100);
        if (monitor is null)
        {
            Assert.Inconclusive("Needs a monitor scaled above 100%: the bridge bitmap DPI only diverges there.");
            return;
        }
        double scale = monitor.Dpi / 96.0;

        await RealAppSession.RunAsync(async () =>
        {
            var stripe = new Border
            {
                Height = STRIPE_DIP,
                Background = Color.FromArgb(255, 0, 200, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            var panel = new Border
            {
                Width = PANEL_W_DIP,
                Height = TALL_DIP,
                Background = Color.FromArgb(255, 220, 40, 40),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                CacheMode = new BitmapCache(),
                Child = stripe,
            };
            var window = new Window
            {
                Title = "CachedElementResize",
                StartupLocation = WindowStartupLocation.Manual,
                Padding = new Thickness(0),
                Background = Color.FromArgb(255, 0, 0, 255),
                WindowSize = WindowSize.Fixed(PANEL_W_DIP + 40, TALL_DIP + 40),
                Content = panel,
            };

            try
            {
                window.Show();
                MonitorProbe.SetWindowPos(window.Handle, 0,
                    monitor.PixelBounds.Left + 60, monitor.PixelBounds.Top + 60, 0, 0, MonitorProbe.MOVE_ONLY);
                await AssertStripeAtBottomAsync(window.Handle, TALL_DIP, scale, "at the initial size");

                // Shrinking retires the tall surface and reuses it for the shorter request.
                panel.Height = SHORT_DIP;
                window.InvalidateVisual();
                await AssertStripeAtBottomAsync(window.Handle, SHORT_DIP, scale, "after shrinking the cached element");

                panel.Height = TALL_DIP;
                window.InvalidateVisual();
                await AssertStripeAtBottomAsync(window.Handle, TALL_DIP, scale, "after growing it back");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// The stripe must sit at the element's true bottom edge, with the body above it, once the change has
    /// reached the composed window image, and must still sit there a moment later.
    /// </summary>
    private static async Task AssertStripeAtBottomAsync(nint hwnd, double heightDip, double scale, string stage)
    {
        // A frame not composed yet reads black and the frame before the change has another colour at this
        // height, so neither passes for the awaited one: a match is what tells that the frame arrived.
        var waited = System.Diagnostics.Stopwatch.StartNew();
        string? mismatch = FindStripeMismatch(hwnd, heightDip, scale);
        while (mismatch != null && waited.ElapsedMilliseconds < FRAME_WAIT_MS)
        {
            await Task.Delay(FRAME_POLL_MS);
            mismatch = FindStripeMismatch(hwnd, heightDip, scale);
        }

        Assert.IsNull(mismatch, $"{stage}: {mismatch} (waited {waited.ElapsedMilliseconds} ms for the frame to reach the window image)");

        await Task.Delay(FRAME_HOLD_MS);
        string? later = FindStripeMismatch(hwnd, heightDip, scale);
        Assert.IsNull(later, $"{stage}: {later} ({FRAME_HOLD_MS} ms after a frame that matched)");
    }

    /// <summary>What is wrong with the window image as it is now, or null when the stripe and the body are in place.</summary>
    private static string? FindStripeMismatch(nint hwnd, double heightDip, double scale)
    {
        var shot = ScreenCapture.OfClientArea(hwnd);
        int widthPx = (int)Math.Round(PANEL_W_DIP * scale);
        int heightPx = (int)Math.Round(heightDip * scale);
        int stripePx = (int)Math.Round(STRIPE_DIP * scale);
        if (shot.Width < widthPx)
        {
            return $"client narrower than the element ({shot.Width} < {widthPx})";
        }

        if (shot.Height < heightPx)
        {
            return $"client shorter than the element ({shot.Height} < {heightPx})";
        }

        int column = widthPx / 2;
        var bottom = shot.At(column, heightPx - 3);
        var body = shot.At(column, heightPx - stripePx - 6);
        var top = shot.At(column, 3);

        if (bottom.G <= 150 || bottom.R >= 120)
        {
            return $"the bottom edge is not the stripe (B={bottom.B} G={bottom.G} R={bottom.R}); " +
                "a squashed cache leaves the previous render's body here";
        }

        if (body.R <= 150 || body.G >= 120)
        {
            return $"just above the stripe should be the body (B={body.B} G={body.G} R={body.R})";
        }

        if (top.R <= 150 || top.G >= 120)
        {
            return $"the top edge should be the body (B={top.B} G={top.G} R={top.R})";
        }

        return null;
    }
}
