extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A layered (per-pixel transparent) Win32 window is presented by drawing each frame into a surface its
/// presenter keeps for the window. The context drawing into that surface is kept as long as the surface is,
/// so what it gathers while drawing, the realized text runs above all, is not built again every frame.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LayeredPresentContextTests
{
    private const int WIDTH = 200;
    private const int HEIGHT = 120;

    [TestMethod]
    public void Gdi_SuccessiveUpdates_DrawWithTheSameContext()
        => AssertOneContextAcrossUpdates(new GdiGraphicsFactory());

    [TestMethod]
    public void Direct2D_SuccessiveUpdates_DrawWithTheSameContext()
        => AssertOneContextAcrossUpdates(new Direct2DGraphicsFactory());

    [TestMethod]
    public void Gdi_AResizedWindow_DrawsWithANewContext()
        => AssertNewContextAfterResize(new GdiGraphicsFactory());

    [TestMethod]
    public void Direct2D_AResizedWindow_DrawsWithANewContext()
        => AssertNewContextAfterResize(new Direct2DGraphicsFactory());

    [TestMethod]
    public void Gdi_ReleasingTheWindowGraphics_LetsGoOfTheContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 backends are Windows-only.");
            return;
        }

        using var scene = new Scene(new GdiGraphicsFactory());
        scene.Present();
        scene.Window.ReleaseWindowGraphicsResources(Scene.HWND);

        Assert.AreEqual(0, scene.Factory.LiveContexts,
            "the context kept for the presented surface outlived the window's graphics");
    }

    [TestMethod]
    public void Gdi_KeptContext_PartialUpdatesMatchAFullFrame()
        => AssertPartialUpdatesMatchAFullFrame(new GdiGraphicsFactory());

    [TestMethod]
    public void Direct2D_KeptContext_PartialUpdatesMatchAFullFrame()
        => AssertPartialUpdatesMatchAFullFrame(new Direct2DGraphicsFactory());

    [TestMethod]
    public void MewVGWin32_KeptContext_PartialUpdatesMatchAFullFrame()
        => AssertPartialUpdatesMatchAFullFrame(new MewVGWin32GraphicsFactory());

    /// <summary>
    /// A transparent window updated part by part through its kept context ends up with the same pixels as
    /// the same state drawn whole by a fresh window.
    /// </summary>
    private static void AssertPartialUpdatesMatchAFullFrame(IGraphicsFactory factory)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 backends are Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        Application.DefaultGraphicsFactory = factory;
        try
        {
            using var scope = factory.AcquireBackgroundRenderScope();
            var (updated, rows) = Menu();
            using var kept = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: true));
            updated.PerformLayout();
            updated.RenderFrameToPresentSurface(kept);
            for (int frame = 0; frame < 6; frame++)
            {
                rows[frame % rows.Count].Background = Color.FromRgb(200, 220, 255);
                rows[(frame + rows.Count - 1) % rows.Count].Background = Color.Transparent;
                ((TextBlock)rows[(frame + 2) % rows.Count].Child!).Text = "Changed " + frame;
                updated.PerformLayout();
                updated.RenderFrameToPresentSurface(kept);
            }

            var (fresh, freshRows) = Menu();
            for (int index = 0; index < rows.Count; index++)
            {
                freshRows[index].Background = rows[index].Background;
                ((TextBlock)freshRows[index].Child!).Text = ((TextBlock)rows[index].Child!).Text;
            }

            using var whole = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: true));
            fresh.PerformLayout();
            fresh.RenderFrameToSurface(whole);

            var keptPixels = Pixels(factory, kept);
            var wholePixels = Pixels(factory, whole);
            int differing = 0;
            for (int index = 0; index < keptPixels.Length; index++)
            {
                if (keptPixels[index] != wholePixels[index])
                {
                    differing++;
                }
            }

            Assert.AreEqual(0, differing, $"{factory.Backend}: {differing} bytes differ between the partly updated frame and a whole one");
            updated.ReleasePresentSurfaceContext();
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
            (factory as IDisposable)?.Dispose();
        }
    }

    private static (Window Window, List<Border> Rows) Menu()
    {
        var rows = Enumerable.Range(0, 5).Select(index => new Border { Padding = new Thickness(6, 2, 6, 2), Child = new TextBlock { Text = "Item " + index } }).ToList();
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.AllowsTransparency = true;
        window.Content = new StackPanel().Children([.. rows]);
        return (window, rows);
    }

    private static byte[] Pixels(IGraphicsFactory factory, IRenderSurface surface)
    {
        var pixels = new byte[surface.PixelWidth * surface.PixelHeight * 4];
        Assert.IsTrue(factory.TryReadPixels(surface, pixels, surface.PixelWidth * 4), "the surface could not be read back");
        return pixels;
    }

    private static void AssertOneContextAcrossUpdates(IGraphicsFactory backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 backends are Windows-only.");
            return;
        }

        using var scene = new Scene(backend);
        scene.Present();
        int afterFirst = scene.Factory.ContextsCreated;

        for (int frame = 0; frame < 5; frame++)
        {
            scene.Label.Text = "Frame " + frame;
            scene.Present();
        }

        Assert.AreEqual(afterFirst, scene.Factory.ContextsCreated,
            $"{backend.Backend}: each of 5 updates created a context for the surface the presenter keeps");
    }

    private static void AssertNewContextAfterResize(IGraphicsFactory backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 backends are Windows-only.");
            return;
        }

        using var scene = new Scene(backend);
        scene.Present();
        scene.Present();
        int before = scene.Factory.ContextsCreated;

        scene.Resize(WIDTH + 40, HEIGHT + 20);
        scene.Present();
        scene.Present();

        Assert.AreEqual(before + 1, scene.Factory.ContextsCreated,
            $"{backend.Backend}: the resized surface did not get exactly one new context");
        Assert.AreEqual(1, scene.Factory.LiveContexts,
            $"{backend.Backend}: the context of the replaced surface was not let go");
    }

    private sealed class Scene : IDisposable
    {
        public const nint HWND = 1;

        private readonly IGraphicsFactory _previousFactory = Application.DefaultGraphicsFactory;
        private int _width = WIDTH;
        private int _height = HEIGHT;

        public Scene(IGraphicsFactory backend)
        {
            Factory = new CountingPresenterFactory(backend);
            Application.DefaultGraphicsFactory = Factory;
            Label = new TextBlock { Text = "Layered" };
            Window = HeadlessWindow.Create(WIDTH, HEIGHT);
            Window.AllowsTransparency = true;
            Window.Content = new Border { Background = Color.White, Child = Label };
        }

        public CountingPresenterFactory Factory { get; }

        public TextBlock Label { get; }

        public Window Window { get; }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            Window.SetClientSizeDip(width, height);
        }

        public void Present()
        {
            Window.PerformLayout();
            Assert.IsTrue(Factory.Present(Window, new LayeredSurface(HWND, _width, _height), 1.0), "the presenter did not handle the surface");
        }

        public void Dispose()
        {
            Window.ReleaseWindowGraphicsResources(HWND);
            Application.DefaultGraphicsFactory = _previousFactory;
            Factory.Dispose();
        }
    }

    private sealed record LayeredSurface(nint Hwnd, int PixelWidth, int PixelHeight) : IWin32WindowSurface
    {
        public nint Handle => Hwnd;

        public double DpiScale => 1.0;
    }

    /// <summary>Presents through the backend while counting the contexts the window creates and the ones still alive.</summary>
    private sealed class CountingPresenterFactory(IGraphicsFactory inner)
        : IGraphicsFactory, ITextBackendFactory, IWindowSurfacePresenter, IWindowResourceReleaser
    {
        private readonly List<WeakReference<IGraphicsContext>> _created = [];

        public int ContextsCreated { get; private set; }

        /// <summary>Contexts created and not yet disposed, judged by the backend's own disposed state.</summary>
        public int LiveContexts => _created.Count(reference => reference.TryGetTarget(out var context) && !IsDisposed(context));

        private static bool IsDisposed(IGraphicsContext context)
            => context is GraphicsContextBase backendContext && backendContext.IsDisposed;

        public bool Present(Window window, IWindowSurface surface, double opacity)
            => ((IWindowSurfacePresenter)inner).Present(window, surface, opacity);

        public void ReleaseWindowResources(nint windowHandle)
            => (inner as IWindowResourceReleaser)?.ReleaseWindowResources(windowHandle);

        public IGraphicsContext CreateContext(IRenderTarget target) => Track(inner.CreateContext(target));

        public IGraphicsContext CreateContext(IRenderSurface surface) => Track(inner.CreateContext(surface));

        private IGraphicsContext Track(IGraphicsContext context)
        {
            ContextsCreated++;
            _created.Add(new WeakReference<IGraphicsContext>(context));
            return context;
        }

        ITextBackendMeasurementContext ITextBackendFactory.CreateTextMeasurementContext(uint dpi)
            => ((ITextBackendFactory)inner).CreateTextMeasurementContext(dpi);

        public string Backend => inner.Backend;
        public RenderDeviceIdentity RenderIdentity => inner.RenderIdentity;

        public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
            bool italic = false, bool underline = false, bool strikethrough = false)
            => inner.CreateFont(family, size, weight, italic, underline, strikethrough);

        public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
            bool italic = false, bool underline = false, bool strikethrough = false)
            => inner.CreateFont(family, size, dpi, weight, italic, underline, strikethrough);

        public IImage CreateImageFromFile(string path) => inner.CreateImageFromFile(path);
        public IImage CreateImageFromBytes(byte[] data) => inner.CreateImageFromBytes(data);
        public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor) => inner.CreateSurface(descriptor);
        public IImage CreateImageView(IRenderSurface surface) => inner.CreateImageView(surface);
        public IImage CreateImageView(IPixelBufferSource source) => inner.CreateImageView(source);
        public IImage CreateImageView(IExternalRasterSource source) => inner.CreateImageView(source);

        public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
            => inner.TryReadPixels(source, destination, destinationStrideBytes);

        public IRenderOperation RequestReadback(IRenderSurface source) => inner.RequestReadback(source);
        public IRenderOperation FlushAsyncWork() => inner.FlushAsyncWork();
        public IRenderResourceCache? ResourceCache => inner.ResourceCache;
        public IRenderEffectDevice? Effects => inner.Effects;

        public void Dispose() => inner.Dispose();
    }
}
