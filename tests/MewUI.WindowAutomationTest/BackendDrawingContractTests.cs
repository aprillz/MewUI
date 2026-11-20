using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// What every backend of every platform draws alike: an opacity scope fades its drawing once as a group,
/// a pen strokes with an image brush, and a visual that reads its transform draws again at a new scale.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class BackendDrawingContractTests
{
    private const int SIZE = 60;
    private const int TOLERANCE = 3;
    private static readonly Color _red = Color.FromRgb(220, 30, 30);

    // Half the red over white.
    private static readonly Color _halfRed = Color.FromRgb(238, 143, 143);

    [TestMethod]
    public Task AnOpacityScope_FadesOverlappingFillsOnce() => CaptureScene.RunAsync(async scene =>
    {
        // A GL backend draws offscreen under the context of a window, so one is shown first.
        var window = await scene.ShowAsync(new Border { Background = Color.White });
        var factory = window.GraphicsFactory;
        var pixels = DrawOffscreen(factory, context =>
        {
            context.BeginOpacity(0.5);
            context.FillRectangle(new Rect(0, 0, 40, SIZE), _red);
            context.FillRectangle(new Rect(20, 0, 40, SIZE), _red);
            context.EndOpacity();
        });

        AssertClose(_halfRed, PixelAt(pixels, 10, SIZE / 2), $"{factory.Backend}: one fill under the scope");
        AssertClose(_halfRed, PixelAt(pixels, 30, SIZE / 2), $"{factory.Backend}: two overlapping fills under the scope");
    });

    [TestMethod]
    public Task AnOpacityScopeAControlDraws_FadesOnceInTheWindow() => CaptureScene.RunAsync(async scene =>
    {
        var control = new ScopedFills();
        var window = await scene.ShowAsync(new Border { Background = Color.White, Child = control });
        await Task.Delay(400);

        string backend = window.GraphicsFactory.Backend;
        foreach (bool reference in new[] { true, false })
        {
            var (pixels, width, scale) = CaptureWindow(window, reference);
            string label = reference ? "a frame drawn straight from the visuals" : "a retained frame";
            var bounds = control.Bounds;
            int row = (int)Math.Round((bounds.Y + 20) * scale);
            AssertClose(_halfRed, PixelAt(pixels, width, (int)Math.Round((bounds.X + 10) * scale), row), $"{backend}, {label}: one fill");
            AssertClose(_halfRed, PixelAt(pixels, width, (int)Math.Round((bounds.X + 30) * scale), row), $"{backend}, {label}: two overlapping fills");
        }
    });

    [TestMethod]
    public Task APathStrokedWithAnImageBrush_ShowsTheImage() => CaptureScene.RunAsync(async scene =>
    {
        var window = await scene.ShowAsync(new Border { Background = Color.White });
        var factory = window.GraphicsFactory;
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var tileSurface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(8, 8, dpiScale: 1.0, debugName: "StrokeTile"));
        using (var tileContext = factory.CreateContext(tileSurface))
        {
            tileContext.BeginFrame(tileSurface);
            tileContext.FillRectangle(new Rect(0, 0, 8, 8), _red);
            tileContext.EndFrame();
        }

        using var tile = factory.CreateImageView(tileSurface);
        var pixels = DrawOffscreen(factory, context =>
        {
            var line = new PathGeometry();
            line.MoveTo(5, SIZE / 2);
            line.LineTo(SIZE - 5, SIZE / 2);
            context.DrawPath(line, new Pen(new ImageBrush(tile, new Rect(0, 0, 8, 8), new Rect(0, 0, 8, 8)), 20));
        });

        AssertClose(_red, PixelAt(pixels, SIZE / 2, SIZE / 2), $"{factory.Backend}: the middle of the stroke");
    });

    [TestMethod]
    public Task AChildThatReadsItsTransform_DrawsAgainAtANewScale() => CaptureScene.RunAsync(async scene =>
    {
        var child = new ScaleReader();
        var decorator = new TransformDecorator
        {
            Child = child,
            Transform = Matrix3x2.CreateScale(1.5f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = await scene.ShowAsync(new Border { Background = Color.White, Child = decorator });
        await Task.Delay(400);
        float before = child.LastScale;

        decorator.Transform = Matrix3x2.CreateScale(3f);
        await Task.Delay(400);

        Assert.AreEqual(1.5f, before, 0.01f, $"{window.GraphicsFactory.Backend}: the scale the child first drew at");
        Assert.AreEqual(3f, child.LastScale, 0.01f, $"{window.GraphicsFactory.Backend}: the child did not draw again at the new scale");
    });

    private static byte[] DrawOffscreen(IGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        // A GL backend renders offscreen only under a current context.
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIZE, SIZE, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                context.Clear(Color.White);
                draw(context);
            }
            finally
            {
                context.EndFrame();
            }
        }

        var pixels = new byte[SIZE * SIZE * 4];
        if (!((IRenderDevice)factory).TryReadPixels(surface, pixels, SIZE * 4))
        {
            Assert.Inconclusive($"{factory.Backend} cannot read an offscreen surface back.");
        }

        return pixels;
    }

    private static (byte[] Pixels, int Width, double Scale) CaptureWindow(Window window, bool reference)
    {
        var factory = window.GraphicsFactory;
        double scale = window.GetDpi() / 96.0;
        int width = (int)Math.Round(window.ClientSize.Width * scale);
        int height = (int)Math.Round(window.ClientSize.Height * scale);
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(width, height, scale, hasAlpha: false));
        if (reference)
        {
            window.RenderReferenceFrameToSurface(surface);
        }
        else
        {
            // The first frame into a new surface is whole; the second goes through the kept frame.
            window.RenderFrameToSurface(surface);
            window.RenderFrameToSurface(surface);
        }

        var pixels = new byte[width * height * 4];
        if (!((IRenderDevice)factory).TryReadPixels(surface, pixels, width * 4))
        {
            Assert.Inconclusive($"{factory.Backend} cannot read an offscreen surface back.");
        }

        return (pixels, width, scale);
    }

    private static Color PixelAt(byte[] pixels, int x, int y) => PixelAt(pixels, SIZE, x, y);

    private static Color PixelAt(byte[] pixels, int width, int x, int y)
    {
        int offset = (y * width + x) * 4;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static void AssertClose(Color expected, Color actual, string where)
    {
        bool close = Math.Abs(expected.R - actual.R) <= TOLERANCE &&
            Math.Abs(expected.G - actual.G) <= TOLERANCE &&
            Math.Abs(expected.B - actual.B) <= TOLERANCE;
        Assert.IsTrue(close, $"{where}: expected about {expected}, found {actual}");
    }

    /// <summary>Two overlapping fills under one opacity scope, drawn by the control itself.</summary>
    private sealed class ScopedFills : FrameworkElement
    {
        protected override Size MeasureContent(Size availableSize) => new(60, 40);

        protected override void OnRender(IGraphicsContext context)
        {
            var bounds = Bounds;
            context.BeginOpacity(0.5);
            context.FillRectangle(new Rect(bounds.X, bounds.Y, 40, 40), _red);
            context.FillRectangle(new Rect(bounds.X + 20, bounds.Y, 40, 40), _red);
            context.EndOpacity();
        }
    }

    /// <summary>Notes the scale it is drawn at, as a vector image picking its raster size does.</summary>
    private sealed class ScaleReader : FrameworkElement
    {
        internal float LastScale { get; private set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 30);

        protected override void OnRender(IGraphicsContext context)
        {
            LastScale = context.GetTransform().M11;
            context.FillRectangle(Bounds, Color.FromRgb(30, 120, 200));
        }
    }
}
