using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Skia.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Interop.Direct2D;

/// <summary>
/// Direct2D (D3D11-backed) zero-copy host that bridges Skia GR GL into a D3D11 texture via
/// <c>WGL_NV_DX_interop</c>. Skia paints into a GL FBO whose color attachment is registered
/// with WGL_NV_DX_interop against a D3D11 texture; on unlock, D2D samples that texture via
/// the external raster source path.
/// </summary>
internal sealed class SkiaWglInteropHost : ISkiaSurfaceHost
{
    private readonly Direct2DGraphicsFactory _d2dFactory;

    private nint _hwnd;
    private nint _hdc;
    private nint _hglrc;
    private nint _wglDevice;
    private GRGlInterface? _glInterface;
    private GRContext? _grContext;

    private nint _d3d11Texture;
    private uint _glTexture;
    private uint _glFbo;
    private uint _glStencil;
    private nint _wglObject;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private IImage? _image;

    private int _pixelWidth;
    private int _pixelHeight;
    private bool _disposed;

    public SkiaWglInteropHost(Direct2DGraphicsFactory d2dFactory)
    {
        _d2dFactory = d2dFactory;
    }

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public bool SurfaceInvalidated => false;
    public string Description => "GPU zero-copy (Skia GL → WGL_NV_DX_interop → D3D11 → D2D)";
    public bool IsOpaque { get; set; }

    public bool EnsureSurface(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (pixelWidth <= 0 || pixelHeight <= 0) return false;
        if (_surface != null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight) return true;

        ReleaseSurfaceResources();
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;

        try
        {
            EnsureBootstrap();
            MakeContextCurrent();
            AllocateSharedTexture(pixelWidth, pixelHeight);
            CreateSkSurface(pixelWidth, pixelHeight);
            CreateMewUiImage(pixelWidth, pixelHeight);
            return _surface != null && _image != null;
        }
        catch
        {
            ReleaseSurfaceResources();
            throw;
        }
    }

    public IImage? Paint(Action<SKSurface> painter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(painter);

        if (_surface == null || _grContext == null || _wglObject == 0) return null;

        MakeContextCurrent();

        if (!WglD2DInterop.DXLockObject(_wglDevice, _wglObject)) return null;

        try
        {
            _grContext.ResetContext(GRBackendState.All);
            painter(_surface);
            _surface.Flush();
            _grContext.Flush();
        }
        finally
        {
            WglD2DInterop.DXUnlockObject(_wglDevice, _wglObject);
        }

        return _image;
    }

    private void EnsureBootstrap()
    {
        if (_grContext != null) return;

        _hwnd = WglD2DInterop.CreateWindowExW(
            dwExStyle: 0, lpClassName: "STATIC", lpWindowName: null,
            dwStyle: WglD2DInterop.WS_POPUP,
            X: 0, Y: 0, nWidth: 1, nHeight: 1,
            hWndParent: 0, hMenu: 0, hInstance: 0, lpParam: 0);
        if (_hwnd == 0) throw new InvalidOperationException("CreateWindowExW(STATIC) failed for hidden Skia WGL context.");

        _hdc = WglD2DInterop.GetDC(_hwnd);
        if (_hdc == 0) throw new InvalidOperationException("GetDC failed on hidden Skia WGL window.");

        var pfd = new WglD2DInterop.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<WglD2DInterop.PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = WglD2DInterop.PFD_DRAW_TO_WINDOW | WglD2DInterop.PFD_SUPPORT_OPENGL | WglD2DInterop.PFD_DOUBLEBUFFER,
            iPixelType = WglD2DInterop.PFD_TYPE_RGBA,
            cColorBits = 32, cAlphaBits = 8, cDepthBits = 24, cStencilBits = 8,
            iLayerType = WglD2DInterop.PFD_MAIN_PLANE,
        };
        int pf = WglD2DInterop.ChoosePixelFormat(_hdc, pfd);
        if (pf == 0 || !WglD2DInterop.SetPixelFormat(_hdc, pf, pfd))
            throw new InvalidOperationException("ChoosePixelFormat / SetPixelFormat failed on hidden Skia WGL window.");

        _hglrc = WglD2DInterop.wglCreateContext(_hdc);
        if (_hglrc == 0) throw new InvalidOperationException("wglCreateContext failed for hidden Skia WGL context.");

        if (!WglD2DInterop.wglMakeCurrent(_hdc, _hglrc))
            throw new InvalidOperationException("wglMakeCurrent failed for hidden Skia WGL context.");

        if (!WglD2DInterop.LoadWglNvDxInterop())
            throw new InvalidOperationException("WGL_NV_DX_interop entry points are not exposed on this GL driver — D3D11 sharing not available.");

        nint d3d11Device = _d2dFactory.NativeD3D11Device;
        if (d3d11Device == 0)
            throw new InvalidOperationException("Direct2DGraphicsFactory.NativeD3D11Device is 0 — D2D GPU pipeline not initialised.");

        _wglDevice = WglD2DInterop.DXOpenDevice(d3d11Device);
        if (_wglDevice == 0) throw new InvalidOperationException("wglDXOpenDeviceNV failed on D2D's D3D11 device.");

        _glInterface = GRGlInterface.Create()
            ?? throw new InvalidOperationException("GRGlInterface.Create() failed for hidden Skia WGL context.");
        _grContext = GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException("GRContext.CreateGl failed for hidden Skia WGL context.");
    }

    private void MakeContextCurrent()
    {
        if (WglD2DInterop.wglGetCurrentContext() != _hglrc)
        {
            WglD2DInterop.wglMakeCurrent(_hdc, _hglrc);
        }
    }

    private void AllocateSharedTexture(int width, int height)
    {
        var desc = new WglD2DInterop.D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width, Height = (uint)height,
            MipLevels = 1, ArraySize = 1,
            Format = WglD2DInterop.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new WglD2DInterop.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = WglD2DInterop.D3D11_USAGE_DEFAULT,
            BindFlags = WglD2DInterop.D3D11_BIND_RENDER_TARGET | WglD2DInterop.D3D11_BIND_SHADER_RESOURCE,
            CPUAccessFlags = 0,
            MiscFlags = WglD2DInterop.D3D11_RESOURCE_MISC_SHARED,
        };
        int hr = WglD2DInterop.CreateTexture2D(_d2dFactory.NativeD3D11Device, in desc, out _d3d11Texture);
        if (hr < 0 || _d3d11Texture == 0)
            throw new InvalidOperationException($"ID3D11Device::CreateTexture2D failed with HRESULT 0x{hr:X8}.");

        SkiaGLInterop.GenTextures(1, out _glTexture);
        if (_glTexture == 0) throw new InvalidOperationException("glGenTextures returned 0.");

        _wglObject = WglD2DInterop.DXRegisterObject(
            _wglDevice, _d3d11Texture, _glTexture,
            SkiaGLInterop.GL_TEXTURE_2D, WglD2DInterop.WGL_ACCESS_WRITE_DISCARD_NV);
        if (_wglObject == 0) throw new InvalidOperationException("wglDXRegisterObjectNV failed.");

        if (!WglD2DInterop.DXLockObject(_wglDevice, _wglObject))
            throw new InvalidOperationException("wglDXLockObjectsNV failed during FBO setup.");
        try
        {
            uint fbo;
            unsafe { SkiaGLInterop.GenFramebuffers(1, &fbo); }
            _glFbo = fbo;

            SkiaGLInterop.BindFramebuffer(SkiaGLInterop.GL_FRAMEBUFFER, _glFbo);
            SkiaGLInterop.FramebufferTexture2D(
                SkiaGLInterop.GL_FRAMEBUFFER, SkiaGLInterop.GL_COLOR_ATTACHMENT0,
                SkiaGLInterop.GL_TEXTURE_2D, _glTexture, 0);

            uint rb;
            unsafe { SkiaGLInterop.GenRenderbuffers(1, &rb); }
            _glStencil = rb;
            SkiaGLInterop.BindRenderbuffer(SkiaGLInterop.GL_RENDERBUFFER, _glStencil);
            SkiaGLInterop.RenderbufferStorage(SkiaGLInterop.GL_RENDERBUFFER, SkiaGLInterop.GL_DEPTH24_STENCIL8, width, height);
            SkiaGLInterop.FramebufferRenderbuffer(SkiaGLInterop.GL_FRAMEBUFFER,
                SkiaGLInterop.GL_DEPTH_STENCIL_ATTACHMENT, SkiaGLInterop.GL_RENDERBUFFER, _glStencil);
            SkiaGLInterop.BindRenderbuffer(SkiaGLInterop.GL_RENDERBUFFER, 0);

            uint status = SkiaGLInterop.CheckFramebufferStatus(SkiaGLInterop.GL_FRAMEBUFFER);
            SkiaGLInterop.BindFramebuffer(SkiaGLInterop.GL_FRAMEBUFFER, 0);
            if (status != SkiaGLInterop.GL_FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException($"Skia GL FBO incomplete after WGL_NV_DX_interop attach (0x{status:X8}).");
        }
        finally
        {
            WglD2DInterop.DXUnlockObject(_wglDevice, _wglObject);
        }
    }

    private void CreateSkSurface(int width, int height)
    {
        _renderTarget = new GRBackendRenderTarget(
            width, height, sampleCount: 0, stencilBits: 8,
            new GRGlFramebufferInfo(_glFbo, SkiaGLInterop.GL_RGBA8));

        _surface = SKSurface.Create(_grContext!, _renderTarget,
            GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888)
            ?? throw new InvalidOperationException("SKSurface.Create (GL FBO with shared D3D11 backing) returned null.");
    }

    private void CreateMewUiImage(int width, int height)
    {
        using var source = new D3D11TextureRasterSource(_d3d11Texture, width, height);
        _image = _d2dFactory.CreateImageView(source);
    }

    private sealed class D3D11TextureRasterSource : IExternalRasterSource
    {
        private readonly nint _texture2D;

        public D3D11TextureRasterSource(nint texture2D, int pixelWidth, int pixelHeight)
        {
            _texture2D = texture2D;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
        }

        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public int Version => 0;
        public RenderPixelFormat Format => RenderPixelFormat.Bgra8888Premultiplied;
        public BitmapAlphaMode AlphaMode => BitmapAlphaMode.Premultiplied;
        public bool YFlipped => false;
        public SurfaceCapabilities Capabilities =>
            SurfaceCapabilities.ExternalHandle |
            SurfaceCapabilities.ExternallySynchronized |
            SurfaceCapabilities.GpuSampleable |
            SurfaceCapabilities.Alpha |
            SurfaceCapabilities.Premultiplied;
        public IReadOnlyList<ExternalRasterPlane> Planes =>
        [
            new ExternalRasterPlane(0, _texture2D, PixelWidth, PixelHeight, 0, Format)
        ];

        public IExternalRasterLease Acquire()
        {
            if (_texture2D == 0) throw new ObjectDisposedException(nameof(D3D11TextureRasterSource));
            return new Lease(this);
        }

        public void Dispose() { }

        private sealed class Lease : IExternalRasterLease
        {
            private readonly D3D11TextureRasterSource _source;
            public Lease(D3D11TextureRasterSource source) { _source = source; }
            public int PixelWidth => _source.PixelWidth;
            public int PixelHeight => _source.PixelHeight;
            public bool YFlipped => false;
            public nint NativeHandle => _source._texture2D;
            public nint NativeAlternateHandle => 0;
            public void Dispose() { }
        }
    }

    private void ReleaseSurfaceResources()
    {
        _image?.Dispose(); _image = null;
        _surface?.Dispose(); _surface = null;
        _renderTarget?.Dispose(); _renderTarget = null;

        if (_wglObject != 0 && _wglDevice != 0)
        {
            WglD2DInterop.DXUnregisterObject(_wglDevice, _wglObject);
            _wglObject = 0;
        }

        if (_glFbo != 0)
        {
            uint fbo = _glFbo;
            unsafe { SkiaGLInterop.DeleteFramebuffers(1, &fbo); }
            _glFbo = 0;
        }
        if (_glStencil != 0)
        {
            uint rb = _glStencil;
            unsafe { SkiaGLInterop.DeleteRenderbuffers(1, &rb); }
            _glStencil = 0;
        }
        if (_glTexture != 0)
        {
            uint t = _glTexture;
            SkiaGLInterop.DeleteTextures(1, ref t);
            _glTexture = 0;
        }

        if (_d3d11Texture != 0) { WglD2DInterop.Release(_d3d11Texture); _d3d11Texture = 0; }

        _pixelWidth = 0;
        _pixelHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseSurfaceResources();

        if (_wglDevice != 0) { WglD2DInterop.DXCloseDevice(_wglDevice); _wglDevice = 0; }

        _grContext?.Dispose(); _grContext = null;
        _glInterface?.Dispose(); _glInterface = null;

        if (_hglrc != 0)
        {
            WglD2DInterop.wglMakeCurrent(0, 0);
            WglD2DInterop.wglDeleteContext(_hglrc);
            _hglrc = 0;
        }
        if (_hdc != 0 && _hwnd != 0)
        {
            WglD2DInterop.ReleaseDC(_hwnd, _hdc);
            _hdc = 0;
        }
        if (_hwnd != 0)
        {
            WglD2DInterop.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }
}
