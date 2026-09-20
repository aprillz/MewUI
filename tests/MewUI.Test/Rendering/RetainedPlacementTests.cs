extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A visual that only moves is replayed from the recording it already has, placed where it now stands.
/// Every case here moves visuals some way other than a whole-pixel scroll and requires the scene-driven
/// frame to equal a frame drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedPlacementTests
{
    private const int WIDTH = 200;
    private const int HEIGHT = 140;
    private const uint DPI_150_PERCENT = 144;

    private sealed class Leaf : Control
    {
        internal int RecordCount { get; private set; }

        internal Color Fill { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 24);

        protected override void OnRender(IGraphicsContext context)
        {
            RecordCount++;
            context.FillRectangle(Bounds, Fill);
            context.DrawRectangle(Bounds, Color.FromArgb(255, 20, 20, 20), 1);
        }
    }

    [TestMethod]
    public void FractionalScroll_MatchesTheReferenceFrame()
    {
        RunScroll(dpi: 96, offsets: [7.5, 12.25, 3.75, 20.5]);
    }

    [TestMethod]
    public void ScrollAtOneAndAHalfScale_MatchesTheReferenceFrame()
    {
        RunScroll(DPI_150_PERCENT, offsets: [10, 11, 17, 4, 30]);
    }

    [TestMethod]
    public void FractionalScrollAtOneAndAHalfScale_MatchesTheReferenceFrame()
    {
        RunScroll(DPI_150_PERCENT, offsets: [7.5, 12.25, 3.75, 20.5]);
    }

    [TestMethod]
    public void FractionalScrollWithoutLayoutRounding_MatchesTheReferenceFrame()
    {
        // Without rounding the visuals land between device pixels, which is the only way a move stops
        // being a whole number of them; a recording placed there would be snapped differently.
        RunScroll(dpi: 96, offsets: [7.5, 12.25, 3.75, 20.5], useLayoutRounding: false);
    }

    [TestMethod]
    public void Direct2DTextScrollAtOneAndAQuarterScale_MatchesTheReferenceFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory();
        RunTextScroll(factory, 120);
    }

    [TestMethod]
    public void GdiTextScrollAtOneAndAQuarterScale_MatchesTheReferenceFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        RunTextScroll(factory, 120);
    }

    [TestMethod]
    [DataRow("Gdi")]
    [DataRow("Direct2D")]
    [DataRow("MewVG")]
    public void TextScrollAtOneAndAHalfScale_MatchesTheReferenceFrame(string backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
            return;
        }

        IGraphicsFactory factory = backend switch
        {
            "Gdi" => new GdiGraphicsFactory(),
            "Direct2D" => new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory(),
            _ => new MewVGWin32GraphicsFactory(),
        };
        using var disposable = factory as IDisposable;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;
        RunTextScroll(factory, DPI_150_PERCENT);
    }

    private static void RunTextScroll(IGraphicsFactory factory, uint dpi)
    {
        Application.DefaultGraphicsFactory = factory;

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        for (int itemIndex = 0; itemIndex < 12; itemIndex++)
        {
            panel.Children(new Border
            {
                Height = 36,
                Margin = new Thickness(8, 2, 8, 2),
                Background = Color.FromArgb(255, (byte)(40 + itemIndex * 17), (byte)(200 - itemIndex * 12), 120),
                Child = new TextBlock { Text = $"Row {itemIndex}", Margin = new Thickness(8) },
            });
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = panel };
        var window = HeadlessWindow.Create(240, 160);
        window.SetDpi(dpi);
        window.Content = scroll;
        window.PerformLayout();

        double scale = dpi / 96.0;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
            (int)Math.Round(240 * scale), (int)Math.Round(160 * scale), scale));
        window.RenderFrameToSurface(surface);

        double[] offsets = [20, 51, 7, 33];
        for (int step = 0; step < offsets.Length; step++)
        {
            scroll.SetScrollOffsets(0, offsets[step]);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);

            using var reference = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
                surface.PixelWidth, surface.PixelHeight, scale));
            window.RenderReferenceFrameToSurface(reference);
            AssertSurfacesEqual(reference, surface, $"{factory.Backend} text scroll {offsets[step]} at {dpi} dpi");
        }
    }

    [TestMethod]
    public void SiblingGrowth_ShiftsTheRestWithoutRecordingItAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var leaves = BuildLeaves(5);
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(leaves);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = stack;
        window.PerformLayout();

        using var surface = CreateSurface(factory, 1.0);
        window.RenderFrameToSurface(surface);
        window.RenderFrameToSurface(surface);
        int recordsBefore = RecordsOf(leaves, 1);

        // The first leaf grows by whole pixels, so every leaf below it moves and none of them changes.
        leaves[0].Height = 40;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        Assert.AreEqual(
            recordsBefore,
            RecordsOf(leaves, 1),
            "a layout shift recorded the visuals it only moved");
        AssertMatchesReference(factory, window, surface, 1.0, "layout shift");
    }

    [TestMethod]
    public void SplitterMove_MatchesTheReferenceFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var left = new StackPanel { Orientation = Orientation.Vertical };
        left.Children(BuildLeaves(3));
        var right = new StackPanel { Orientation = Orientation.Vertical };
        right.Children(BuildLeaves(3));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixels(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        Grid.SetColumn(right, 1);
        grid.Children(left, right);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        window.PerformLayout();

        using var surface = CreateSurface(factory, 1.0);
        window.RenderFrameToSurface(surface);

        double[] widths = [72, 72.5, 90, 41.25, 60];
        for (int step = 0; step < widths.Length; step++)
        {
            grid.ColumnDefinitions[0].Width = GridLength.Pixels(widths[step]);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            AssertMatchesReference(factory, window, surface, 1.0, $"column width {widths[step]}");
        }
    }

    private static void RunScroll(uint dpi, double[] offsets, bool useLayoutRounding = true)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(BuildLeaves(12));
        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = stack };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.SetDpi(dpi);
        window.UseLayoutRounding = useLayoutRounding;
        window.Content = scroll;
        window.PerformLayout();

        double scale = dpi / 96.0;
        using var surface = CreateSurface(factory, scale);
        window.RenderFrameToSurface(surface);

        for (int step = 0; step < offsets.Length; step++)
        {
            scroll.SetScrollOffsets(0, offsets[step]);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            AssertMatchesReference(factory, window, surface, scale, $"scroll offset {offsets[step]} at {dpi} dpi");
        }
    }

    private static Leaf[] BuildLeaves(int count)
    {
        var leaves = new Leaf[count];
        for (int index = 0; index < count; index++)
        {
            leaves[index] = new Leaf
            {
                Height = 24,
                Margin = new Thickness(3, 1, 3, 1),
                Fill = Color.FromArgb(255, (byte)(40 + index * 17), (byte)(200 - index * 11), (byte)(90 + index * 9)),
            };
        }

        return leaves;
    }

    private static int RecordsOf(Leaf[] leaves, int startIndex)
    {
        int total = 0;
        for (int index = startIndex; index < leaves.Length; index++)
        {
            total += leaves[index].RecordCount;
        }

        return total;
    }

    private static IRenderSurface CreateSurface(IGraphicsFactory factory, double scale)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(
            (int)Math.Ceiling(WIDTH * scale),
            (int)Math.Ceiling(HEIGHT * scale),
            scale,
            hasAlpha: false));

    private static void AssertMatchesReference(
        IGraphicsFactory factory,
        Window window,
        IRenderSurface actualSurface,
        double scale,
        string label)
    {
        using var referenceSurface = CreateSurface(factory, scale);
        window.RenderReferenceFrameToSurface(referenceSurface);
        AssertSurfacesEqual(referenceSurface, actualSurface, label);
    }

    private static void AssertSurfacesEqual(IRenderSurface referenceSurface, IRenderSurface actualSurface, string label)
    {
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)referenceSurface).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> actual = ((ICpuPixelSurface)actualSurface).GetReadOnlyPixelSpan();
        int pixelWidth = referenceSurface.PixelWidth;
        int differing = 0;
        int first = -1;
        int last = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != actual[offset] ||
                expected[offset + 1] != actual[offset + 1] ||
                expected[offset + 2] != actual[offset + 2])
            {
                differing++;
                last = offset / 4;
                if (first < 0)
                {
                    first = offset / 4;
                }
            }
        }

        Assert.AreEqual(
            0,
            differing,
            $"{label}: {differing} pixels differ from the reference frame, first at ({first % Math.Max(1, pixelWidth)}, {first / Math.Max(1, pixelWidth)})");
    }
}
