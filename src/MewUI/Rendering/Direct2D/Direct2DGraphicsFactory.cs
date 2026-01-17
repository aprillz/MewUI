using Aprillz.MewUI;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

public sealed unsafe class Direct2DGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser, IDisposable
{
    public static Direct2DGraphicsFactory Instance => field ??= new Direct2DGraphicsFactory();

    public GraphicsBackend Backend => GraphicsBackend.Direct2D;

    private nint _d2dFactory;
    private nint _dwriteFactory;
    private bool _initialized;

    private readonly object _rtLock = new();
    private readonly Dictionary<nint, WindowRenderTarget> _windowTargets = new();

    private Direct2DGraphicsFactory() { }

    public void Dispose()
    {
        lock (_rtLock)
        {
            foreach (var (_, entry) in _windowTargets)
            {
                ComHelpers.Release(entry.RenderTarget);
            }

            _windowTargets.Clear();
        }

        ComHelpers.Release(_dwriteFactory);
        _dwriteFactory = 0;
        ComHelpers.Release(_d2dFactory);
        _d2dFactory = 0;
        _initialized = false;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Ole32.CoInitializeEx(0, Ole32.COINIT_APARTMENTTHREADED);

        int hr = D2D1.D2D1CreateFactory(D2D1_FACTORY_TYPE.SINGLE_THREADED, D2D1.IID_ID2D1Factory, 0, out _d2dFactory);
        if (hr < 0 || _d2dFactory == 0)
        {
            throw new InvalidOperationException($"D2D1CreateFactory failed: 0x{hr:X8}");
        }

        hr = DWrite.DWriteCreateFactory(DWRITE_FACTORY_TYPE.SHARED, DWrite.IID_IDWriteFactory, out _dwriteFactory);
        if (hr < 0 || _dwriteFactory == 0)
        {
            throw new InvalidOperationException($"DWriteCreateFactory failed: 0x{hr:X8}");
        }

        _initialized = true;
    }

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false) =>
        new DirectWriteFont(family, size, weight, italic, underline, strikethrough);

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false) =>
        new DirectWriteFont(family, size, weight, italic, underline, strikethrough);

    public IImage CreateImageFromFile(string path) =>
        CreateImageFromBytes(File.ReadAllBytes(path));

    public IImage CreateImageFromBytes(byte[] data) =>
        ImageDecoders.TryDecode(data, out var bmp)
            ? new Direct2DImage(bmp)
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    public IImage CreateImageFromPixelSource(IPixelBufferSource source) => new Direct2DImage(source);

    public IGraphicsContext CreateContext(nint hwnd, nint hdc, double dpiScale)
    {
        EnsureInitialized();

        var (rt, generation) = GetOrCreateWindowRenderTarget(hwnd, dpiScale);
        return new Direct2DGraphicsContext(hwnd, dpiScale, rt, generation, _dwriteFactory, onRecreateTarget: () => InvalidateWindowRenderTarget(hwnd));
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
    {
        EnsureInitialized();
        return new Direct2DMeasurementContext(_dwriteFactory);
    }

    public void ReleaseWindowResources(nint hwnd)
    {
        lock (_rtLock)
        {
            if (_windowTargets.Remove(hwnd, out var entry))
            {
                ComHelpers.Release(entry.RenderTarget);
            }
        }
    }

    private void InvalidateWindowRenderTarget(nint hwnd) => ReleaseWindowResources(hwnd);

    private (nint renderTarget, int generation) GetOrCreateWindowRenderTarget(nint hwnd, double dpiScale)
    {
        var rc = D2D1VTable.GetClientRect(hwnd);
        uint w = (uint)Math.Max(1, rc.Width);
        uint h = (uint)Math.Max(1, rc.Height);
        float dpi = (float)(96.0 * dpiScale);

        lock (_rtLock)
        {
            int generation = 0;
            if (_windowTargets.TryGetValue(hwnd, out var entry) && entry.RenderTarget != 0)
            {
                if (entry.Width == w && entry.Height == h && entry.DpiX == dpi)
                {
                    return (entry.RenderTarget, entry.Generation);
                }

                // If size/DPI changed, recreate the target. (Safer than calling ID2D1HwndRenderTarget::Resize via vtable indices.)
                ComHelpers.Release(entry.RenderTarget);
                entry.RenderTarget = 0;
                entry.Generation++;
                generation = entry.Generation;
                _windowTargets.Remove(hwnd);
            }

            // HWND render target: use alpha IGNORE to keep ClearType enabled (PREMULTIPLIED will force grayscale).
            var pixelFormat = new D2D1_PIXEL_FORMAT(0, D2D1_ALPHA_MODE.IGNORE);
            var rtProps = new D2D1_RENDER_TARGET_PROPERTIES(D2D1_RENDER_TARGET_TYPE.DEFAULT, pixelFormat, 0, 0, 0, 0);
            var hwndProps = new D2D1_HWND_RENDER_TARGET_PROPERTIES(hwnd, new D2D1_SIZE_U(w, h), D2D1_PRESENT_OPTIONS.NONE);

            int hr = D2D1VTable.CreateHwndRenderTarget((ID2D1Factory*)_d2dFactory, ref rtProps, ref hwndProps, out var renderTarget);
            if (hr < 0 || renderTarget == 0)
            {
                throw new InvalidOperationException($"CreateHwndRenderTarget failed: 0x{hr:X8}");
            }

            D2D1VTable.SetDpi((ID2D1RenderTarget*)renderTarget, dpi, dpi);
            _windowTargets[hwnd] = new WindowRenderTarget(renderTarget, w, h, dpi, generation);
            return (renderTarget, generation);
        }
    }

    private sealed class WindowRenderTarget
    {
        public nint RenderTarget;
        public uint Width;
        public uint Height;
        public float DpiX;
        public int Generation;

        public WindowRenderTarget(nint renderTarget, uint width, uint height, float dpiX, int generation)
        {
            RenderTarget = renderTarget;
            Width = width;
            Height = height;
            DpiX = dpiX;
            Generation = generation;
        }
    }
}
