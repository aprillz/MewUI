extern alias MewVGWin32;

using System.Diagnostics;
using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Measures what one shape costs on the processor when a visual redraws many of them every frame: filled
/// rectangles, rounded rectangles with a border, a frozen curve path drawn under a translation, text that stays
/// the same and text that is new every frame. The frames
/// go to a surface whose context is kept, as a window's frame surface is.
/// It reports numbers and asserts none. Runs only when MEWUI_SHAPE_COST is 1; MEWUI_SHAPE_COST_KIND runs one case and MEWUI_SHAPE_COST_SECONDS
/// keeps a case running that long, for a profiler to attach to.
/// Not parallelizable: timing, and the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedShapeCostTests
{
    private const int WIDTH = 1024;
    private const int HEIGHT = 600;
    private const int FRAMES = 200;

    [TestMethod]
    [DataRow("rectangles", 2000)]
    [DataRow("rounded", 1000)]
    [DataRow("paths", 1000)]
    [DataRow("texts", 1000)]
    [DataRow("newtexts", 1000)]
    public void MeasureShapes(string kind, int count)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_SHAPE_COST=1 on Windows to run the shape cost measurement.");
            return;
        }

        string? only = Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST_KIND");
        if (!string.IsNullOrEmpty(only) && only != kind)
        {
            Assert.Inconclusive("MEWUI_SHAPE_COST_KIND names another case.");
            return;
        }

        var factory = new MewVGWin32GraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory.AcquireBackgroundRenderScope();

        var shapes = new Shapes(kind, count);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = shapes };
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        for (int warm = 0; warm < 30; warm++)
        {
            shapes.Advance();
            window.PerformLayout();
            window.RenderFrameToPresentSurface(surface);
        }

        double seconds = double.TryParse(Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST_SECONDS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double asked) ? asked : 0;
        long until = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        var samples = new List<double>(FRAMES);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        while (samples.Count < FRAMES || Stopwatch.GetTimestamp() < until)
        {
            shapes.Advance();
            window.PerformLayout();
            long start = Stopwatch.GetTimestamp();
            window.RenderFrameToPresentSurface(surface);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        window.ReleasePresentSurfaceContext();
        samples.Sort();
        double median = samples[samples.Count / 2];
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === {count} {kind} every frame (MewVG, {samples.Count} frames) ===
            frame {median:0.00} ms, {median * 1000 / count:0.00} us per shape, {bytes / samples.Count / 1024.0:0.0} KB per frame
            """));
    }

    /// <summary>A grid of bordered cells with a short text each, of which some get new text and a new background every frame.</summary>
    [TestMethod]
    [DataRow(1000, 1)]
    [DataRow(1000, 10)]
    [DataRow(4000, 1)]
    [DataRow(4000, 50)]
    public void MeasureGrid(int cells, int percent)
    {
        string kind = "grid" + cells + "-" + percent;
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_SHAPE_COST=1 on Windows to run the shape cost measurement.");
            return;
        }

        string? only = Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST_KIND");
        if (!string.IsNullOrEmpty(only) && only != kind)
        {
            Assert.Inconclusive("MEWUI_SHAPE_COST_KIND names another case.");
            return;
        }

        var factory = new MewVGWin32GraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory.AcquireBackgroundRenderScope();

        int columns = Math.Max(1, (int)Math.Round(Math.Sqrt(cells * (double)WIDTH / HEIGHT)));
        int rows = (cells + columns - 1) / columns;
        int cellWidth = Math.Max(1, WIDTH / columns);
        int cellHeight = Math.Max(1, HEIGHT / rows);
        var borders = new Border[cells];
        var texts = new TextBlock[cells];
        var canvas = new Canvas();
        for (int index = 0; index < cells; index++)
        {
            texts[index] = new TextBlock
            {
                Text = (index % 100).ToString("00", CultureInfo.InvariantCulture),
                FontSize = Math.Max(6, Math.Min(13, cellHeight - 5)),
                Foreground = Color.FromArgb(255, 255, 255, 255),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            borders[index] = new Border
            {
                Width = cellWidth,
                Height = cellHeight,
                Background = CellColor(index),
                BorderBrush = Color.FromArgb(255, 20, 20, 20),
                BorderThickness = 1,
                Child = texts[index],
            };
            Canvas.SetLeft(borders[index], index % columns * cellWidth);
            Canvas.SetTop(borders[index], index / columns * cellHeight);
            canvas.Children(borders[index]);
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = canvas;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        int changed = Math.Max(1, cells * percent / 100);
        long step = 0;
        void Advance()
        {
            step++;
            for (int change = 0; change < changed; change++)
            {
                int index = (int)(((step * 7919) + (change * 104729L)) % cells);
                texts[index].Text = ((index + step) % 100).ToString("00", CultureInfo.InvariantCulture);
                borders[index].Background = CellColor(index + (int)step);
            }
        }

        for (int warm = 0; warm < 30; warm++)
        {
            Advance();
            window.PerformLayout();
            window.RenderFrameToPresentSurface(surface);
        }

        double seconds = double.TryParse(Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST_SECONDS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double asked) ? asked : 0;
        long until = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        var layout = new List<double>(FRAMES);
        var render = new List<double>(FRAMES);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        while (render.Count < FRAMES || Stopwatch.GetTimestamp() < until)
        {
            Advance();
            long start = Stopwatch.GetTimestamp();
            window.PerformLayout();
            layout.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            start = Stopwatch.GetTimestamp();
            window.RenderFrameToPresentSurface(surface);
            render.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        window.ReleasePresentSurfaceContext();
        layout.Sort();
        render.Sort();
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === grid of {cells} cells, {changed} changed every frame (MewVG, {render.Count} frames) ===
            layout {layout[layout.Count / 2]:0.00} ms, frame {render[render.Count / 2]:0.00} ms, {bytes / render.Count / 1024.0:0.0} KB per frame, dirty areas {window.LastRetainedDirtyRects.Count} ({window.LastWholeFrameReason})
            """));
    }

    /// <summary>Rectangle elements on a canvas, every one moved through its position properties every frame.</summary>
    [TestMethod]
    [DataRow(1000)]
    public void MeasureMovedElements(int count)
    {
        string kind = "moved";
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_SHAPE_COST=1 on Windows to run the shape cost measurement.");
            return;
        }

        string? only = Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST_KIND");
        if (!string.IsNullOrEmpty(only) && only != kind)
        {
            Assert.Inconclusive("MEWUI_SHAPE_COST_KIND names another case.");
            return;
        }

        var factory = new MewVGWin32GraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory.AcquireBackgroundRenderScope();

        var random = new Random(5);
        var rectangles = new Aprillz.MewUI.Rectangle[count];
        var homeX = new int[count];
        var homeY = new int[count];
        var canvas = new Canvas();
        for (int index = 0; index < count; index++)
        {
            homeX[index] = random.Next(WIDTH - 32);
            homeY[index] = random.Next(HEIGHT - 32);
            rectangles[index] = new Aprillz.MewUI.Rectangle { Width = 24, Height = 24, Fill = CellColor(index) };
            Canvas.SetLeft(rectangles[index], homeX[index]);
            Canvas.SetTop(rectangles[index], homeY[index]);
            canvas.Children(rectangles[index]);
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = canvas;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        int step = 0;
        void Advance()
        {
            step++;
            for (int index = 0; index < count; index++)
            {
                Canvas.SetLeft(rectangles[index], homeX[index] + ((step + index) % 7));
                Canvas.SetTop(rectangles[index], homeY[index] + ((step + (index * 3)) % 5));
            }
        }

        for (int warm = 0; warm < 30; warm++)
        {
            Advance();
            window.PerformLayout();
            window.RenderFrameToPresentSurface(surface);
        }

        double seconds = double.TryParse(Environment.GetEnvironmentVariable("MEWUI_SHAPE_COST_SECONDS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double asked) ? asked : 0;
        long until = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        var layout = new List<double>(FRAMES);
        var render = new List<double>(FRAMES);
        long setBytes = 0;
        long layoutBytes = 0;
        long renderBytes = 0;
        while (render.Count < FRAMES || Stopwatch.GetTimestamp() < until)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Advance();
            long afterSet = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            window.PerformLayout();
            layout.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            long afterLayout = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
            window.RenderFrameToPresentSurface(surface);
            render.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            setBytes += afterSet - before;
            layoutBytes += afterLayout - afterSet;
            renderBytes += GC.GetAllocatedBytesForCurrentThread() - afterLayout;
        }

        window.RetainedStatistics!.Reset();
        Advance();
        window.PerformLayout();
        window.RenderFrameToPresentSurface(surface);
        var statistics = window.RetainedStatistics;
        window.ReleasePresentSurfaceContext();
        layout.Sort();
        render.Sort();
        int frames = render.Count;
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === {count} elements moved every frame (MewVG, {frames} frames) ===
            layout {layout[frames / 2]:0.00} ms, frame {render[frames / 2]:0.00} ms, per element and frame: {setBytes / (double)frames / count:0} B setting, {layoutBytes / (double)frames / count:0} B in layout, {renderBytes / (double)frames / count:0} B in the frame ({window.LastWholeFrameReason}); one frame recorded {statistics.ContentRecordCount}, replayed {statistics.ContentReplayCount}, drew live {statistics.LiveFallbackCount}
            """));
    }

    private static Color CellColor(int index)
        => Color.FromArgb(255, (byte)(40 + ((index * 37) % 200)), (byte)(40 + ((index * 59) % 200)), (byte)(40 + ((index * 83) % 200)));

    private sealed class Shapes : FrameworkElement
    {
        private const int SIZE = 48;
        private readonly string _kind;
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly PathGeometry _star = BuildStar();
        private readonly Pen _pen = new(Color.FromArgb(255, 20, 20, 20), 2);
        private readonly TextRunStyle _textStyle = new("Segoe UI", 14);
        private readonly TextDrawOptions _keptText = new(Color.FromArgb(255, 20, 20, 20));
        private readonly TextDrawOptions _newText = new(Color.FromArgb(255, 20, 20, 20), Transient: true);
        private ITextLayout[]? _layouts;
        private int _step;

        public Shapes(string kind, int count)
        {
            _kind = kind;
            _x = new double[count];
            _y = new double[count];
            var random = new Random(11);
            for (int index = 0; index < count; index++)
            {
                _x[index] = random.Next(WIDTH - SIZE);
                _y[index] = random.Next(HEIGHT - SIZE);
            }

            IsHitTestVisible = false;
        }

        public void Advance()
        {
            _step++;
            InvalidateVisual();
        }

        protected override void OnRender(IGraphicsContext context)
        {
            for (int index = 0; index < _x.Length; index++)
            {
                double x = _x[index] + ((_step + index) % 7);
                double y = _y[index] + ((_step + (index * 3)) % 5);
                var color = Color.FromArgb(255, (byte)(40 + ((index * 37) % 200)), (byte)(40 + ((index * 59) % 200)), (byte)(40 + ((index * 83) % 200)));
                switch (_kind)
                {
                    case "rectangles":
                        context.FillRectangle(new Rect(x, y, 24, 16), color);
                        break;
                    case "rounded":
                        context.FillRoundedRectangle(new Rect(x, y, SIZE, 32), 8, 8, color);
                        context.DrawRoundedRectangle(new Rect(x, y, SIZE, 32), 8, 8, _pen);
                        break;
                    case "texts":
                        _layouts ??= CreateLayouts(context);
                        context.Text.Draw(_layouts[index % _layouts.Length], new Point(x, y), in _keptText);
                        break;
                    case "newtexts":
                        var layout = Application.DefaultGraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
                        {
                            Text = (((index * 7919L) + (_step * 13)) % 1_000_000).ToString(CultureInfo.InvariantCulture).AsMemory(),
                            Dpi = 96,
                            DefaultStyle = _textStyle,
                        });
                        context.Text.Draw(layout, new Point(x, y), in _newText);
                        break;
                    default:
                        context.Save();
                        context.Translate(x, y);
                        context.FillPath(_star, color);
                        context.Restore();
                        break;
                }
            }
        }

        private ITextLayout[] CreateLayouts(IGraphicsContext context)
        {
            var layouts = new ITextLayout[64];
            for (int index = 0; index < layouts.Length; index++)
            {
                layouts[index] = Application.DefaultGraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
                {
                    Text = ("Word" + index).AsMemory(),
                    Dpi = (uint)Math.Round(96 * context.DpiScale),
                    DefaultStyle = _textStyle,
                });
            }

            return layouts;
        }

        private static PathGeometry BuildStar()
        {
            const int PETALS = 5;
            double center = SIZE / 2.0;
            double inner = SIZE * 0.2;
            static double InnerAngle(int petal) => (-90 - 36 + (petal * 72)) * Math.PI / 180;
            var path = new PathGeometry();
            path.MoveTo(center + (Math.Cos(InnerAngle(0)) * inner), center + (Math.Sin(InnerAngle(0)) * inner));
            for (int petal = 0; petal < PETALS; petal++)
            {
                double tipAngle = (-90 + (petal * 72)) * Math.PI / 180;
                double tipX = center + (Math.Cos(tipAngle) * center);
                double tipY = center + (Math.Sin(tipAngle) * center);
                path.BezierTo(tipX, tipY, tipX, tipY, center + (Math.Cos(InnerAngle(petal + 1)) * inner), center + (Math.Sin(InnerAngle(petal + 1)) * inner));
            }

            path.Close();
            path.Freeze();
            return path;
        }
    }
}
