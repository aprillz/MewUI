using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates that text rendered into a generic offscreen surface carries correct alpha, so it
/// remains visible after premultiplied compositing (the CacheMode "invisible text" bug). These
/// tests measure the actual per-pixel alpha produced by the GDI backend; they fail while the bug
/// is present and should pass once the offscreen text-alpha fix lands.
/// </summary>
[TestClass]
public sealed class OffscreenTextAlphaTests
{
    private const int Width = 96;
    private const int Height = 28;

    [TestMethod]
    public void GdiOffscreen_TransparentSurface_TextProducesAlpha()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (maxAlpha, coveredPixels) = RenderTextAndMeasureAlpha(fillOpaqueBackground: false);
        Console.WriteLine($"[transparent bg] maxAlpha={maxAlpha}, coveredPixels(alpha>16)={coveredPixels}");

        Assert.IsTrue(maxAlpha > 0,
            $"Text on a transparent offscreen surface has zero alpha everywhere (maxAlpha={maxAlpha}) " +
            "→ invisible after premultiplied compositing. Reproduces the CacheMode invisible-text bug.");
        Assert.IsTrue(coveredPixels >= 5,
            $"Expected a cluster of text pixels carrying alpha; got {coveredPixels}.");
    }

    [TestMethod]
    public void GdiOffscreen_OpaqueBackground_TextProducesAlpha()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (maxAlpha, coveredPixels) = RenderTextAndMeasureAlpha(fillOpaqueBackground: true);
        Console.WriteLine($"[opaque bg] maxAlpha={maxAlpha}, coveredPixels(alpha>16)={coveredPixels}");

        // With an opaque background the whole surface should be alpha 255; the diagnostic value is
        // whether the text region differs in color (covered pixels here means non-background pixels).
        Assert.IsTrue(maxAlpha > 0, $"Opaque-background surface unexpectedly has zero alpha (maxAlpha={maxAlpha}).");
    }

    /// <summary>
    /// Reproduces the CacheMode compose path: render text into surface A, wrap it via
    /// CreateImageView, DrawImage it onto surface B, then measure text alpha in B. If text alpha
    /// survives in A (proven above) but vanishes in B, the bug is in the image-view/blit step.
    /// </summary>
    [TestMethod]
    public void GdiOffscreen_ImageViewRoundTrip_TextSurvives()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        GdiGraphicsFactory factory = new GdiGraphicsFactory();

        IRenderSurface source = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(Width, Height, 1.0));
        IRenderSurface dest = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(Width, Height, 1.0));
        try
        {
            using (IGraphicsContext context = factory.CreateContext(source))
            {
                context.BeginFrame(source);
                IFont font = factory.CreateFont("Segoe UI", 16, 96);
                context.DrawText("Hello", new Rect(2, 2, Width - 4, Height - 4), font, Color.FromArgb(255, 220, 40, 40));
                context.EndFrame();
            }

            IImage image = factory.CreateImageView(source);
            using (IGraphicsContext context = factory.CreateContext(dest))
            {
                context.BeginFrame(dest);
                context.DrawImage(image, new Rect(0, 0, Width, Height));
                context.EndFrame();
            }

            var cpu = (ICpuPixelSurface)dest;
            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int stride = cpu.StrideBytes;
            int maxAlpha = 0, coveredPixels = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    byte alpha = pixels[y * stride + x * 4 + 3];
                    if (alpha > maxAlpha) maxAlpha = alpha;
                    if (alpha > 16) coveredPixels++;
                }
            }

            Console.WriteLine($"[imageview round-trip] maxAlpha={maxAlpha}, coveredPixels(alpha>16)={coveredPixels}");
            Assert.IsTrue(coveredPixels >= 5,
                $"Text vanished through CreateImageView+DrawImage (coveredPixels={coveredPixels}). " +
                "The CacheMode invisible-text bug is in the image-view/blit step, not text rendering.");
        }
        finally
        {
            source.Dispose();
            dest.Dispose();
        }
    }

    /// <summary>
    /// Renders a real <see cref="TextBlock"/> (the control path: DrawTextLayout + theme Foreground)
    /// into an offscreen surface and inspects the text color. Reproduces what CacheMode's snapshot
    /// does for a real control. The user reports cached text appears WHITE (color lost); this
    /// pinpoints whether the control text path differs from raw context.DrawText.
    /// </summary>
    [TestMethod]
    public void GdiOffscreen_RealTextBlock_KeepsForegroundColor()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        GdiGraphicsFactory factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;   // TextBlock.Measure needs a factory for text measurement
        IRenderSurface surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(Width, Height, 1.0));
        try
        {
            var textBlock = new Aprillz.MewUI.Controls.TextBlock
            {
                Text = "Hello",
                Foreground = Color.FromArgb(255, 30, 30, 30),   // dark
                FontSize = 16,
                SkipViewportCull = true,                         // no window root; bypass cull
            };
            textBlock.Measure(new Size(Width, Height));
            textBlock.Arrange(new Rect(0, 0, Width, Height));

            using (IGraphicsContext context = factory.CreateContext(surface))
            {
                context.BeginFrame(surface);
                textBlock.Render(context);
                context.EndFrame();
            }

            var cpu = (ICpuPixelSurface)surface;
            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int stride = cpu.StrideBytes;
            int maxAlpha = 0, peakB = 0, peakG = 0, peakR = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int offset = y * stride + x * 4;
                    byte alpha = pixels[offset + 3];
                    if (alpha > maxAlpha)
                    {
                        maxAlpha = alpha;
                        peakB = pixels[offset + 0];
                        peakG = pixels[offset + 1];
                        peakR = pixels[offset + 2];
                    }
                }
            }

            // Dark text premultiplied ⇒ peak pixel RGB all LOW. White (color lost) ⇒ RGB all HIGH.
            bool looksWhite = peakR > 180 && peakG > 180 && peakB > 180;
            Console.WriteLine($"[real TextBlock] maxAlpha={maxAlpha}, peak BGRA=({peakB},{peakG},{peakR},{maxAlpha})  {(looksWhite ? "WHITE(color lost!)" : "DARK(correct)")}");
            Assert.IsTrue(maxAlpha > 16, $"TextBlock produced no visible coverage (maxAlpha={maxAlpha}).");
            Assert.IsFalse(looksWhite,
                $"Real TextBlock text rendered WHITE into offscreen surface (peak RGB={peakR},{peakG},{peakB}) - " +
                "foreground color lost. Reproduces the CacheMode white-text bug.");
        }
        finally
        {
            surface.Dispose();
        }
    }

    /// <summary>
    /// Closest headless proxy for the real failing scenario: render a dark TextBlock into a cache
    /// surface at non-unit DPI, wrap via CreateImageView, then DrawImage it onto an OPAQUE target
    /// pre-filled white (mimicking the live opaque window). Measures whether the text stays dark or
    /// turns white (the reported bug).
    /// </summary>
    [TestMethod]
    public void GdiOffscreen_BlitOntoOpaqueWindowProxy_KeepsTextDark()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        const double dpi = 1.5;
        int pxW = (int)(Width * dpi), pxH = (int)(Height * dpi);

        GdiGraphicsFactory factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        IRenderSurface cache = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(pxW, pxH, dpi));
        // Opaque (no-alpha) target, closer to the live WindowRenderTarget than a premultiplied surface.
        IRenderSurface window = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pxW, pxH, dpi, hasAlpha: false));
        try
        {
            var textBlock = new Aprillz.MewUI.Controls.TextBlock
            {
                Text = "Hello",
                Foreground = Color.FromArgb(255, 30, 30, 30),
                FontSize = 16,
                SkipViewportCull = true,
            };
            textBlock.Measure(new Size(Width, Height));
            textBlock.Arrange(new Rect(0, 0, Width, Height));

            using (IGraphicsContext context = factory.CreateContext(cache))
            {
                context.BeginFrame(cache);
                textBlock.Render(context);
                context.EndFrame();
            }

            IImage image = factory.CreateImageView(cache);
            using (IGraphicsContext context = factory.CreateContext(window))
            {
                context.BeginFrame(window);
                context.FillRectangle(new Rect(0, 0, Width, Height), Color.FromArgb(255, 255, 255, 255)); // white window bg
                context.DrawImage(image, new Rect(0, 0, Width, Height));
                context.EndFrame();
            }

            var cpu = (ICpuPixelSurface)window;
            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int stride = cpu.StrideBytes;
            // Find the darkest pixel (the text) and report its color.
            int minLuma = 255, darkB = 255, darkG = 255, darkR = 255;
            for (int y = 0; y < pxH; y++)
            {
                for (int x = 0; x < pxW; x++)
                {
                    int offset = y * stride + x * 4;
                    int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                    int luma = (r + g + b) / 3;
                    if (luma < minLuma) { minLuma = luma; darkB = b; darkG = g; darkR = r; }
                }
            }

            Console.WriteLine($"[opaque window proxy @dpi{dpi}] darkest pixel BGRA=({darkB},{darkG},{darkR}) luma={minLuma}  " +
                $"{(minLuma < 128 ? "DARK text present(correct)" : "NO dark text → white/invisible(BUG)")}");
            Assert.IsTrue(minLuma < 128,
                $"After blitting the cache onto an opaque white target, no dark text pixels exist (min luma={minLuma}) - " +
                "text turned white/invisible. Reproduces the CacheMode bug.");
        }
        finally
        {
            cache.Dispose();
            window.Dispose();
        }
    }

    /// <summary>
    /// Full CacheMode end-to-end with the real nesting: render a cached TextBlock onto a "window"
    /// context while that window frame is active, so EnsureCache opens a nested BeginFrame/EndFrame
    /// on the cache surface mid-frame (exactly what happens live). This is the one factor the other
    /// headless tests did not reproduce.
    /// </summary>
    [TestMethod]
    public void Gdi_CacheModeEndToEnd_NestedInWindowFrame_KeepsTextDark()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        GdiGraphicsFactory factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        // Larger surface; place the cached label at a non-zero offset (as inside a padded StackPanel).
        const int surfW = 360, surfH = 220;
        const int offX = 30, offY = 100;
        IRenderSurface windowSurface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(surfW, surfH, 1.0, hasAlpha: false));
        try
        {
            var label = new Aprillz.MewUI.Controls.Label
            {
                Text = "Hello",
                Foreground = Color.FromArgb(255, 30, 30, 30),
                FontSize = 16,
                SkipViewportCull = true,
                CacheMode = new BitmapCache(),   // ← cache ON
            };
            label.Measure(new Size(Width, Height));
            label.Arrange(new Rect(offX, offY, Width, Height));   // offset, not (0,0)

            using (IGraphicsContext windowContext = factory.CreateContext(windowSurface))
            {
                windowContext.BeginFrame(windowSurface);
                windowContext.FillRectangle(new Rect(0, 0, surfW, surfH), Color.FromArgb(255, 255, 255, 255));
                // Triggers RenderCached → EnsureCache nests a BeginFrame/EndFrame on the cache surface
                // INSIDE this active window frame, then DrawImage's the result back here.
                label.Render(windowContext);
                windowContext.EndFrame();
            }

            var cpu = (ICpuPixelSurface)windowSurface;
            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int stride = cpu.StrideBytes;
            int minLuma = 255, darkB = 255, darkG = 255, darkR = 255;
            for (int y = 0; y < surfH; y++)
            {
                for (int x = 0; x < surfW; x++)
                {
                    int offset = y * stride + x * 4;
                    int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                    int luma = (r + g + b) / 3;
                    if (luma < minLuma) { minLuma = luma; darkB = b; darkG = g; darkR = r; }
                }
            }

            Console.WriteLine($"[CacheMode nested e2e] darkest BGRA=({darkB},{darkG},{darkR}) luma={minLuma}  " +
                $"{(minLuma < 128 ? "DARK(correct)" : "WHITE/invisible(BUG REPRODUCED)")}");
            Assert.IsTrue(minLuma < 128,
                $"Cached TextBlock rendered with nested frame produced no dark text (min luma={minLuma}) - bug reproduced.");
        }
        finally
        {
            windowSurface.Dispose();
        }
    }

    [TestMethod]
    public void Gdi_CacheModeRebuild_SameSize_DoesNotKeepPreviousText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        GdiGraphicsFactory factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        const int surfaceWidth = 260;
        const int surfaceHeight = 60;
        IRenderSurface windowSurface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(surfaceWidth, surfaceHeight, 1.0, hasAlpha: false));
        try
        {
            var label = new Aprillz.MewUI.Controls.Label
            {
                Text = "MMMMMMMMMMMM",
                Foreground = Color.Black,
                FontSize = 20,
                SkipViewportCull = true,
                CacheMode = new BitmapCache(),
            };
            label.Measure(new Size(surfaceWidth, surfaceHeight));
            label.Arrange(new Rect(0, 0, surfaceWidth, surfaceHeight));

            RenderLabel(label, factory, windowSurface, surfaceWidth, surfaceHeight);

            label.Text = "I";
            RenderLabel(label, factory, windowSurface, surfaceWidth, surfaceHeight);

            var cpu = (ICpuPixelSurface)windowSurface;
            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int darkPixelsPastShortText = 0;
            for (int y = 0; y < surfaceHeight; y++)
            {
                for (int x = 50; x < surfaceWidth; x++)
                {
                    int offset = y * cpu.StrideBytes + x * 4;
                    if (pixels[offset] < 128 || pixels[offset + 1] < 128 || pixels[offset + 2] < 128)
                    {
                        darkPixelsPastShortText++;
                    }
                }
            }

            Assert.AreEqual(0, darkPixelsPastShortText);
        }
        finally
        {
            windowSurface.Dispose();
        }
    }

    [TestMethod]
    public void Gdi_CachedText_RespectsParentClip()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        GdiGraphicsFactory factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        const int surfaceWidth = 240;
        const int surfaceHeight = 60;
        const int clipWidth = 80;
        IRenderSurface surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(surfaceWidth, surfaceHeight, 1.0, hasAlpha: false));
        try
        {
            var label = new Aprillz.MewUI.Controls.Label
            {
                Text = "MMMMMMMMMMMM",
                Width = 220,
                Height = surfaceHeight,
                Foreground = Color.Black,
                FontSize = 20,
                SkipViewportCull = true,
                CacheMode = new BitmapCache(),
            };
            var border = new Aprillz.MewUI.Controls.Border
            {
                Width = clipWidth,
                Height = surfaceHeight,
                ClipToBounds = true,
                Child = label,
            };
            border.Measure(new Size(clipWidth, surfaceHeight));
            border.Arrange(new Rect(0, 0, clipWidth, surfaceHeight));

            using IGraphicsContext context = factory.CreateContext(surface);
            context.BeginFrame(surface);
            context.Clear(Color.White);
            border.Render(context);
            context.EndFrame();

            var cpu = (ICpuPixelSurface)surface;
            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int darkPixelsOutsideClip = 0;
            for (int y = 0; y < surfaceHeight; y++)
            {
                for (int x = clipWidth; x < surfaceWidth; x++)
                {
                    int offset = y * cpu.StrideBytes + x * 4;
                    if (pixels[offset] < 128 || pixels[offset + 1] < 128 || pixels[offset + 2] < 128)
                    {
                        darkPixelsOutsideClip++;
                    }
                }
            }

            Assert.AreEqual(0, darkPixelsOutsideClip);
        }
        finally
        {
            surface.Dispose();
        }
    }

    [TestMethod]
    public void Gdi_CachedParent_TextRespectsInternalClip()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        GdiGraphicsFactory factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        const int surfaceWidth = 240;
        const int surfaceHeight = 60;
        const int clipWidth = 80;
        IRenderSurface surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(surfaceWidth, surfaceHeight, 1.0, hasAlpha: false));
        try
        {
            var label = new Aprillz.MewUI.Controls.Label
            {
                Text = "MMMMMMMMMMMM",
                Width = 220,
                Height = surfaceHeight,
                Foreground = Color.Black,
                FontSize = 20,
                SkipViewportCull = true,
            };
            var border = new Aprillz.MewUI.Controls.Border
            {
                Width = clipWidth,
                Height = surfaceHeight,
                ClipToBounds = true,
                CacheMode = new BitmapCache(),
                Child = label,
            };
            border.Measure(new Size(clipWidth, surfaceHeight));
            border.Arrange(new Rect(0, 0, clipWidth, surfaceHeight));

            using IGraphicsContext context = factory.CreateContext(surface);
            context.BeginFrame(surface);
            context.Clear(Color.White);
            border.Render(context);
            context.EndFrame();

            var cpu = (ICpuPixelSurface)surface;
            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int darkPixelsOutsideClip = 0;
            for (int y = 0; y < surfaceHeight; y++)
            {
                for (int x = clipWidth; x < surfaceWidth; x++)
                {
                    int offset = y * cpu.StrideBytes + x * 4;
                    if (pixels[offset] < 128 || pixels[offset + 1] < 128 || pixels[offset + 2] < 128)
                    {
                        darkPixelsOutsideClip++;
                    }
                }
            }

            Assert.AreEqual(0, darkPixelsOutsideClip);
        }
        finally
        {
            surface.Dispose();
        }
    }

    private static void RenderLabel(
        Aprillz.MewUI.Controls.Label label,
        GdiGraphicsFactory factory,
        IRenderSurface surface,
        int width,
        int height)
    {
        using IGraphicsContext context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.White);
        label.Render(context);
        context.EndFrame();
    }

    private static (int maxAlpha, int coveredPixels) RenderTextAndMeasureAlpha(bool fillOpaqueBackground)
    {
        GdiGraphicsFactory factory = new GdiGraphicsFactory();
        IRenderSurface surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(Width, Height, 1.0));
        try
        {
            using (IGraphicsContext context = factory.CreateContext(surface))
            {
                context.BeginFrame(surface);
                if (fillOpaqueBackground)
                {
                    context.FillRectangle(new Rect(0, 0, Width, Height), Color.FromArgb(255, 250, 250, 250));
                }

                IFont font = factory.CreateFont("Segoe UI", 16, 96);
                context.DrawText("Hello", new Rect(2, 2, Width - 4, Height - 4), font, Color.FromArgb(255, 220, 40, 40));
                context.EndFrame();
            }

            if (surface is not ICpuPixelSurface cpu)
            {
                Assert.Inconclusive("GDI offscreen surface is not CPU-readable; cannot measure alpha.");
                return (0, 0);
            }

            ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
            int stride = cpu.StrideBytes;

            int maxAlpha = 0;
            int coveredPixels = 0;
            int peakB = 0, peakG = 0, peakR = 0, peakA = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int offset = y * stride + x * 4;
                    byte alpha = pixels[offset + 3];
                    if (alpha > maxAlpha)
                    {
                        maxAlpha = alpha;
                        peakB = pixels[offset + 0];
                        peakG = pixels[offset + 1];
                        peakR = pixels[offset + 2];
                        peakA = alpha;
                    }
                    if (alpha > 16) coveredPixels++;
                }
            }

            // Text was drawn RED (220,40,40). Premultiplied-correct ⇒ peak pixel R>>G,B.
            // White coverage (color lost) ⇒ R≈G≈B. This is the actual bug signature.
            Console.WriteLine($"   peak pixel BGRA=({peakB},{peakG},{peakR},{peakA})  " +
                $"{(peakR > peakG + 40 && peakR > peakB + 40 ? "RED(correct)" : "NOT-RED(color lost?)")}");

            return (maxAlpha, coveredPixels);
        }
        finally
        {
            surface.Dispose();
        }
    }
}
