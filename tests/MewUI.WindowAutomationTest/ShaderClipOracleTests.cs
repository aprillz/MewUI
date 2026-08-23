using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The GL backend's clip mask must cut every clip situation (single, nested, empty, beyond the
/// bounds, restored, transformed, rounded, path, image brush) the same way on a real window at
/// every scale the machine offers and on an offscreen surface whose origin differs from the window's. Two scenes carry an absolute
/// expectation so a clip that is silently ignored or stuck cannot pass by agreeing with itself,
/// and the offscreen dump lets one run's pixels be compared with another machine's.
/// </summary>
[TestClass]
public sealed class ShaderClipOracleTests
{
    private const double CANVAS_DIP = 200;
    private const double CANVAS_MARGIN_DIP = 20;
    private const double OFFSCREEN_OFFSET_DIP = 24;
    private const int EDGE_INSET_PX = 3;
    // Channel distance a pixel may differ by before it counts as a mismatch (AA and gamma noise).
    private const int CHANNEL_TOLERANCE = 24;
    // Share of mismatching pixels a scene may carry: clip edges are anti-aliased differently by an
    // R8 mask and a stencil, and a 1px band around a 200 DIP square is about 2% of its pixels.
    private const double MISMATCH_RATIO_LIMIT = 0.03;

    private static readonly Color _background = Color.FromRgb(250, 250, 250);
    private static readonly Color _fill = Color.FromRgb(30, 90, 200);

    private sealed record Scene(string Name, Action<IGraphicsContext, Rect, IImage> Draw, Expectation Expectation);

    private enum Expectation { Compare, NothingFilled, FullyFilled }

    private static readonly Scene[] _scenes =
    [
        new("single rect clip", static (context, area, _) =>
        {
            context.SetClip(Inset(area, 40));
            context.FillRectangle(area, _fill);
        }, Expectation.Compare),

        new("nested clip", static (context, area, _) =>
        {
            context.SetClip(Inset(area, 20));
            context.IntersectClip(new Rect(area.X, area.Y, area.Width * 0.6, area.Height * 0.6));
            context.FillRectangle(area, _fill);
        }, Expectation.Compare),

        new("empty clip", static (context, area, _) =>
        {
            context.SetClip(Inset(area, 20));
            context.IntersectClip(new Rect(area.Right + 10, area.Y, 50, 50));
            context.FillRectangle(area, _fill);
        }, Expectation.NothingFilled),

        new("clip beyond bounds", static (context, area, _) =>
        {
            context.SetClip(new Rect(area.X - 500, area.Y + 60, area.Width + 1000, 80));
            context.FillRectangle(area, _fill);
        }, Expectation.Compare),

        new("fill after restore", static (context, area, _) =>
        {
            context.Save();
            context.SetClip(Inset(area, 80));
            context.FillRectangle(area, Color.FromRgb(200, 40, 40));
            context.Restore();
            context.FillRectangle(area, _fill);
        }, Expectation.FullyFilled),

        new("clip then transform", static (context, area, _) =>
        {
            context.SetClip(Inset(area, 30));
            context.Save();
            context.Translate(area.X + 20, area.Y + 20);
            context.Scale(0.7, 0.7);
            context.FillRectangle(new Rect(0, 0, area.Width, area.Height), _fill);
            context.Restore();
        }, Expectation.Compare),

        new("rounded clip, linear gradient", static (context, area, _) =>
        {
            context.SetClipRoundedRect(Inset(area, 20), 40, 40);
            var brush = new LinearGradientBrush(
                new Point(area.X, area.Y), new Point(area.Right, area.Bottom),
                [new GradientStop(0, Color.FromRgb(240, 60, 60)), new GradientStop(1, Color.FromRgb(60, 60, 240))]);
            context.FillRectangle(area, brush);
        }, Expectation.Compare),

        new("path clip, radial gradient", static (context, area, _) =>
        {
            var star = new PathGeometry();
            var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            for (int i = 0; i < 10; i++)
            {
                double radius = i % 2 == 0 ? area.Width * 0.45 : area.Width * 0.18;
                double angle = -Math.PI / 2 + i * Math.PI / 5;
                var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
                if (i == 0) star.MoveTo(point); else star.LineTo(point);
            }
            star.Close();
            context.SetClipPath(star);
            var brush = new RadialGradientBrush(center, center, area.Width / 2, area.Height / 2,
                [new GradientStop(0, Color.FromRgb(255, 220, 80)), new GradientStop(1, Color.FromRgb(120, 40, 160))]);
            context.FillRectangle(area, brush);
        }, Expectation.Compare),

        new("rect clip, image brush", static (context, area, image) =>
        {
            context.SetClip(Inset(area, 30));
            var brush = new ImageBrush(image, new Rect(0, 0, 32, 32), new Rect(area.X, area.Y, 32, 32), TileMode.Tile);
            context.FillRectangle(area, brush);
        }, Expectation.Compare),
    ];

    private static Rect Inset(Rect rect, double amount)
        => new(rect.X + amount, rect.Y + amount, rect.Width - amount * 2, rect.Height - amount * 2);

    /// <summary>Draws the current scene over an opaque background; the window and the offscreen run share it.</summary>
    private sealed class OracleCanvas : FrameworkElement
    {
        public Scene? Scene { get; set; }
        public IImage? Pattern { get; set; }
        public int Renders { get; private set; }

        protected override void OnRender(IGraphicsContext context)
        {
            Renders++;
            var area = Bounds;
            context.FillRectangle(area, _background);
            if (Scene is not null && Pattern is not null)
            {
                context.Save();
                Scene.Draw(context, area, Pattern);
                context.Restore();
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(MonitorMatrix.DistinctScales), typeof(MonitorMatrix),
        DynamicDataDisplayName = nameof(MonitorMatrix.ScaleName),
        DynamicDataDisplayNameDeclaringType = typeof(MonitorMatrix))]
    public async Task OffscreenClipMatchesWindow(MonitorProbe monitor)
    {
        if (!RequireGlBackend() || !RequireScreenCapture()) return;

        await RealAppSession.RunAsync(async () =>
        {
            var failures = new List<string>();
            var pattern = CreatePattern();
            try
            {
                using var window = await OpenCanvasWindow(monitor, pattern, "window");
                double scale = monitor.Dpi / 96.0;

                foreach (var scene in _scenes)
                {
                    var fromWindow = await window.Render(scene);
                    var fromOffscreen = RenderOffscreen(scene, pattern, scale);
                    CheckAbsolute(scene, fromWindow, $"window {scene.Name} on {monitor.Label}", failures);
                    CheckAgainst(scene, fromWindow, fromOffscreen, $"offscreen {scene.Name} at {monitor.ScalePercent}%", failures);
                }
            }
            finally
            {
                pattern.Dispose();
            }

            Assert.IsTrue(failures.Count == 0, string.Join(Environment.NewLine, failures));
        });
    }

    /// <summary>
    /// Renders every scene offscreen at the scales the clip path has to serve and writes the pixels
    /// out, so one run per clip mode can be compared byte for byte without any window or screen
    /// capture in the loop. Needs MEWUI_CLIP_ORACLE_DUMP; does nothing otherwise.
    /// </summary>
    [TestMethod]
    public async Task OffscreenScenesDumpAtEveryScale()
    {
        if (!RequireGlBackend()) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MEWUI_CLIP_ORACLE_DUMP")))
        {
            Assert.Inconclusive("Set MEWUI_CLIP_ORACLE_DUMP to a directory to write the scene dumps.");
            return;
        }

        await RealAppSession.RunAsync(() =>
        {
            var failures = new List<string>();
            var pattern = CreatePattern();
            try
            {
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75 })
                {
                    foreach (var scene in _scenes)
                    {
                        var pixels = RenderOffscreen(scene, pattern, scale);
                        Dump(pixels, $"offscreen {scene.Name} {(int)(scale * 100)}");
                        CheckAbsolute(scene, pixels, $"offscreen {scene.Name} at {scale:P0}", failures);
                    }
                }
            }
            finally
            {
                pattern.Dispose();
            }

            Assert.IsTrue(failures.Count == 0, string.Join(Environment.NewLine, failures));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The window tests read the window back through DWM, which some drivers never feed a GL
    /// present into (the capture then shows the erased background). They run only where that is
    /// known to work; the offscreen dump test carries the clip verification everywhere else.
    /// </summary>
    private static bool RequireScreenCapture()
    {
        if (Environment.GetEnvironmentVariable("MEWUI_CLIP_ORACLE_SCREEN") != "1")
        {
            Assert.Inconclusive("Set MEWUI_CLIP_ORACLE_SCREEN=1 on a machine whose driver exposes GL windows to PrintWindow.");
            return false;
        }

        return true;
    }

    private static bool RequireGlBackend()
    {
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return false;
        }

        var backend = Application.Current.GraphicsFactory.Backend.ToString();
        if (!backend.Contains("MewVG", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive($"Shader clip is a GL backend feature; this run uses {backend}. Set MEWUI_AUTOMATION_BACKEND=MewVG.");
            return false;
        }

        return true;
    }

    /// <summary>A 32x32 checkerboard the image-brush scene tiles, drawn through the backend so it is a real backend image.</summary>
    private static IImage CreatePattern()
    {
        var factory = Application.Current.GraphicsFactory;
        // Outside a window's frame no GL context is current on this thread; the scope activates one.
        using var scope = factory.AcquireBackgroundRenderScope();
        var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(32, 32, 1.0, "ClipOraclePattern"));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                context.FillRectangle(new Rect(0, 0, 32, 32), Color.FromRgb(230, 230, 230));
                context.FillRectangle(new Rect(0, 0, 16, 16), Color.FromRgb(40, 40, 40));
                context.FillRectangle(new Rect(16, 16, 16, 16), Color.FromRgb(40, 40, 40));
            }
            finally
            {
                context.EndFrame();
            }
        }

        return factory.CreateImageView(surface);
    }

    private sealed class CanvasWindow : IDisposable
    {
        private readonly Window _window;
        private readonly OracleCanvas _canvas;
        private readonly double _scale;
        private int _frames;

        public CanvasWindow(Window window, OracleCanvas canvas, double scale)
        {
            _window = window;
            _canvas = canvas;
            _scale = scale;
            // Frame presentation count, to tell a window that is not repainting from one that draws nothing.
            _window.OnFrameRendered(() => _frames++);
        }

        /// <summary>Shows the scene, waits for a frame, and returns the canvas pixels with their AA edge trimmed.</summary>
        public async Task<Pixels> Render(Scene scene)
        {
            _canvas.Scene = scene;
            _canvas.InvalidateVisual();
            _window.InvalidateVisual();
            // The capture reads the screen, so nothing may sit on top of this window: topmost, since a
            // plain z-order raise does not pass another process's foreground window.
            const uint SWP_NOSIZE_NOMOVE_NOACTIVATE = 0x0001 | 0x0002 | 0x0010;
            const nint HWND_TOPMOST = -1;
            MonitorProbe.SetWindowPos(_window.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE_NOMOVE_NOACTIVATE);
            int framesBefore = _frames;
            await Task.Delay(600);
            var stats = _window.LastFrameStats;
            Log($"{_window.Title} {scene.Name}: frames={_frames - framesBefore} lastFrameDraws={stats.DrawCalls} canvasRenders={_canvas.Renders} canvasBounds={_canvas.Bounds} dpi={_window.Dpi}");

            var capture = ScreenCapture.OfClientArea(_window.Handle);
            Dump(new Pixels(capture.Width, capture.Height, capture.Bgra), $"{_window.Title} {scene.Name} {(int)Math.Round(_scale * 100)} full client");
            int origin = (int)Math.Round(CANVAS_MARGIN_DIP * _scale);
            int size = (int)Math.Round(CANVAS_DIP * _scale);
            return Pixels.Crop(capture.Bgra, capture.Width, origin + EDGE_INSET_PX, origin + EDGE_INSET_PX, size - EDGE_INSET_PX * 2, size - EDGE_INSET_PX * 2);
        }

        public void Dispose() => _window.Close();
    }

    private static async Task<CanvasWindow> OpenCanvasWindow(MonitorProbe monitor, IImage pattern, string mode, int slot = 0)
    {
        var canvas = new OracleCanvas
        {
            Width = CANVAS_DIP,
            Height = CANVAS_DIP,
            Margin = new Thickness(CANVAS_MARGIN_DIP),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Pattern = pattern,
        };
        var window = new Window
        {
            Title = $"ShaderClipOracle {mode}",
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(CANVAS_DIP + CANVAS_MARGIN_DIP * 2 + 40, CANVAS_DIP + CANVAS_MARGIN_DIP * 2 + 40),
            // The canvas origin is computed from its margin alone; the window must add no inset of its own.
            Padding = new Thickness(0),
            Content = canvas,
        };

        window.Show();
        // Slots sit next to each other on the monitor; the slot pitch covers the window at 175%.
        int pitch = (int)Math.Round((CANVAS_DIP + CANVAS_MARGIN_DIP * 2 + 80) * monitor.Dpi / 96.0);
        MonitorProbe.SetWindowPos(window.Handle, 0,
            monitor.PixelBounds.CenterX - pitch + slot * pitch, monitor.PixelBounds.CenterY - 160, 0, 0, MonitorProbe.MOVE_ONLY);
        await Task.Delay(400);

        Assert.AreEqual(monitor.Dpi, window.Dpi, $"the {mode} window must sit on {monitor.Label}");
        return new CanvasWindow(window, canvas, monitor.Dpi / 96.0);
    }

    /// <summary>Draws the scene into an offscreen surface at the window's scale, with the canvas placed away from the surface origin.</summary>
    private static Pixels RenderOffscreen(Scene scene, IImage pattern, double scale)
    {
        var factory = Application.Current.GraphicsFactory;
        using var scope = factory.AcquireBackgroundRenderScope();
        int offset = (int)Math.Round(OFFSCREEN_OFFSET_DIP * scale);
        int size = (int)Math.Round(CANVAS_DIP * scale);
        int extent = offset + size + 16;
        var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(extent, extent, 1.0, "ClipOracle"));
        try
        {
            using (var context = factory.CreateContext(surface))
            {
                context.BeginFrame(surface);
                try
                {
                    context.FillRectangle(new Rect(0, 0, extent, extent), Color.FromRgb(0, 0, 0));
                    context.Scale(scale, scale);
                    var area = new Rect(OFFSCREEN_OFFSET_DIP, OFFSCREEN_OFFSET_DIP, CANVAS_DIP, CANVAS_DIP);
                    context.FillRectangle(area, _background);
                    context.Save();
                    scene.Draw(context, area, pattern);
                    context.Restore();
                }
                finally
                {
                    context.EndFrame();
                }
            }

            var bgra = new byte[extent * extent * 4];
            Assert.IsTrue(factory.TryReadPixels(surface, bgra, extent * 4), "offscreen readback failed");
            return Pixels.Crop(bgra, extent, offset + EDGE_INSET_PX, offset + EDGE_INSET_PX, size - EDGE_INSET_PX * 2, size - EDGE_INSET_PX * 2);
        }
        finally
        {
            surface.Dispose();
        }
    }

    private sealed record Pixels(int Width, int Height, byte[] Bgra)
    {
        public static Pixels Crop(byte[] source, int sourceWidth, int x, int y, int width, int height)
        {
            var bgra = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(source, ((y + row) * sourceWidth + x) * 4, bgra, row * width * 4, width * 4);
            }

            return new Pixels(width, height, bgra);
        }

        public int Distance(int x, int y, Color color)
        {
            int offset = (y * Width + x) * 4;
            return Math.Max(Math.Abs(Bgra[offset] - color.B), Math.Max(Math.Abs(Bgra[offset + 1] - color.G), Math.Abs(Bgra[offset + 2] - color.R)));
        }
    }

    private static void CheckAbsolute(Scene scene, Pixels pixels, string label, List<string> failures)
    {
        if (scene.Expectation == Expectation.Compare)
        {
            return;
        }

        var expected = scene.Expectation == Expectation.NothingFilled ? _background : _fill;
        int off = 0;
        for (int y = 0; y < pixels.Height; y++)
        {
            for (int x = 0; x < pixels.Width; x++)
            {
                if (pixels.Distance(x, y, expected) > CHANNEL_TOLERANCE) off++;
            }
        }

        if (off > 0)
        {
            failures.Add($"{label}: expected every pixel to be {scene.Expectation}, {off} of {pixels.Width * pixels.Height} are not");
        }
    }

    /// <summary>Console plus, with MEWUI_CLIP_ORACLE_DUMP set, a log file next to the dumps: the MSTest runner swallows console output.</summary>
    private static void Log(string line)
    {
        Console.WriteLine("[clip-oracle] " + line);
        var directory = Environment.GetEnvironmentVariable("MEWUI_CLIP_ORACLE_DUMP");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "log.txt"), line + Environment.NewLine);
        }
    }

    /// <summary>With MEWUI_CLIP_ORACLE_DUMP set to a directory, every compared pair is written there as BMP for inspection.</summary>
    private static void Dump(Pixels pixels, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MEWUI_CLIP_ORACLE_DUMP");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var safe = string.Concat(name.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        using var file = File.Create(Path.Combine(directory, safe + ".bmp"));
        using var writer = new BinaryWriter(file);
        int rowBytes = pixels.Width * 4;
        int imageBytes = rowBytes * pixels.Height;
        writer.Write((ushort)0x4D42); writer.Write(54 + imageBytes); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(pixels.Width); writer.Write(-pixels.Height); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(0); writer.Write(imageBytes); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
        writer.Write(pixels.Bgra);
    }

    private static void CheckAgainst(Scene scene, Pixels reference, Pixels candidate, string label, List<string> failures)
    {
        Dump(reference, label + " reference");
        Dump(candidate, label + " candidate");
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
        {
            failures.Add($"{label}: size {candidate.Width}x{candidate.Height} vs reference {reference.Width}x{reference.Height}");
            return;
        }

        int mismatches = 0;
        int worst = 0;
        for (int i = 0; i < reference.Bgra.Length; i += 4)
        {
            int distance = Math.Max(Math.Abs(reference.Bgra[i] - candidate.Bgra[i]),
                Math.Max(Math.Abs(reference.Bgra[i + 1] - candidate.Bgra[i + 1]), Math.Abs(reference.Bgra[i + 2] - candidate.Bgra[i + 2])));
            if (distance > CHANNEL_TOLERANCE) mismatches++;
            if (distance > worst) worst = distance;
        }

        int total = reference.Width * reference.Height;
        double ratio = (double)mismatches / total;
        Log($"{label}: {mismatches}/{total} px differ ({ratio:P2}), worst channel delta {worst}");
        if (ratio > MISMATCH_RATIO_LIMIT)
        {
            failures.Add($"{label}: {mismatches}/{total} px differ ({ratio:P2}, limit {MISMATCH_RATIO_LIMIT:P0}), worst channel delta {worst}");
        }
    }
}
