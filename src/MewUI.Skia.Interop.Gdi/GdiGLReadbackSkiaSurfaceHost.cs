using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Skia.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Interop.Gdi;

/// <summary>
/// GDI hybrid host: Skia renders into a hidden GL FBO (GPU-accelerated path tessellation /
/// AA / gradient), then <c>glReadPixels</c> copies the FBO into a DIB section that the GDI
/// backend BitBlts directly. Faster than Skia software for complex scenes when a usable GL
/// driver is present; falls back via CPU otherwise.
/// </summary>
/// <remarks>
/// One CPU readback per frame (~33MB at 4K). Trade-off vs <see cref="GdiSkiaSurfaceHost"/>:
/// GL path wins for complex Skia content (path AA, gradients, effects); software wins for
/// trivial UI where the readback cost dominates.
/// </remarks>
internal sealed class GdiGLReadbackSkiaSurfaceHost : ISkiaSurfaceHost, IOpaqueAwareSurfaceHost
{
    private readonly GdiGraphicsFactory _factory;

    // GL bootstrap (lazy, one-time)
    private nint _hwnd;
    private nint _hdc;
    private nint _hglrc;
    private GRGlInterface? _glInterface;
    private GRContext? _grContext;

    // Per-surface GL FBO
    private uint _glTexture;
    private uint _glFbo;
    private uint _glStencil;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _skSurface;

    // DIB sink
    private nint _dibHandle;
    private nint _dibBits;
    private IImage? _image;

    private int _pixelWidth;
    private int _pixelHeight;
    private bool _disposed;

    public GdiGLReadbackSkiaSurfaceHost(GdiGraphicsFactory factory)
    {
        _factory = factory;
    }

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public bool SurfaceInvalidated => false;
    public string Description => "Hybrid (Skia GL FBO → glReadPixels → DIB → GDI BitBlt)";

    public bool IsOpaque
    {
        get;
        set
        {
            field = value;
            if (_image is not null) _factory.SetImageOpaque(_image, value);
        }
    }

    public bool EnsureSurface(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (pixelWidth <= 0 || pixelHeight <= 0) return false;
        if (_skSurface != null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight) return true;

        ReleaseSurfaceResources();
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;

        try
        {
            EnsureBootstrap();
            MakeContextCurrent();
            AllocateGLFramebuffer(pixelWidth, pixelHeight);
            CreateSkSurface(pixelWidth, pixelHeight);
            AllocateDib(pixelWidth, pixelHeight);
            _image = _factory.CreateImageOverDibSection(pixelWidth, pixelHeight, _dibHandle, _dibBits);
            if (_image is not null) _factory.SetImageOpaque(_image, IsOpaque);
            return _skSurface != null && _image != null;
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

        if (_skSurface == null || _grContext == null || _image == null || _dibBits == 0) return null;

        MakeContextCurrent();

        // We unbound the FBO at the end of the previous Paint (glReadPixels followed by
        // BindFramebuffer 0). Skia's GR context still thinks its FBO is bound — reset so
        // its state tracking re-issues the binding on the next draw.
        _grContext.ResetContext(GRBackendState.All);

        painter(_skSurface);
        _skSurface.Flush(submit: true);
        _grContext.Flush(submit: true);

        // glReadPixels from our FBO. Since we set GRSurfaceOrigin.TopLeft on SKSurface,
        // Skia inverts its draws so the FBO holds top-down pixels and glReadPixels produces
        // a top-down result that matches the DIB's biHeight=-h orientation.
        SkiaGLInterop.BindFramebuffer(SkiaGLInterop.GL_FRAMEBUFFER, _glFbo);
        WglBootstrap.glPixelStorei(WglBootstrap.GL_PACK_ALIGNMENT, 4);
        WglBootstrap.glReadPixels(0, 0, _pixelWidth, _pixelHeight,
            WglBootstrap.GL_BGRA, WglBootstrap.GL_UNSIGNED_INT_8_8_8_8_REV, _dibBits);
        SkiaGLInterop.BindFramebuffer(SkiaGLInterop.GL_FRAMEBUFFER, 0);

        _factory.MarkExternalImageBitsChanged(_image);
        return _image;
    }

    private void EnsureBootstrap()
    {
        if (_grContext != null) return;

        _hwnd = WglBootstrap.CreateWindowExW(
            dwExStyle: 0, lpClassName: "STATIC", lpWindowName: null,
            dwStyle: WglBootstrap.WS_POPUP,
            X: 0, Y: 0, nWidth: 1, nHeight: 1,
            hWndParent: 0, hMenu: 0, hInstance: 0, lpParam: 0);
        if (_hwnd == 0) throw new InvalidOperationException("CreateWindowExW(STATIC) failed for hidden Skia GL window.");

        _hdc = WglBootstrap.GetDC(_hwnd);
        if (_hdc == 0) throw new InvalidOperationException("GetDC failed on hidden Skia GL window.");

        var pfd = new WglBootstrap.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<WglBootstrap.PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = WglBootstrap.PFD_DRAW_TO_WINDOW | WglBootstrap.PFD_SUPPORT_OPENGL | WglBootstrap.PFD_DOUBLEBUFFER,
            iPixelType = WglBootstrap.PFD_TYPE_RGBA,
            cColorBits = 32,
            cAlphaBits = 8,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = WglBootstrap.PFD_MAIN_PLANE,
        };
        int pf = WglBootstrap.ChoosePixelFormat(_hdc, pfd);
        if (pf == 0 || !WglBootstrap.SetPixelFormat(_hdc, pf, pfd))
            throw new InvalidOperationException("ChoosePixelFormat / SetPixelFormat failed on hidden Skia GL window.");

        _hglrc = WglBootstrap.wglCreateContext(_hdc);
        if (_hglrc == 0) throw new InvalidOperationException("wglCreateContext failed for hidden Skia GL context.");
        if (!WglBootstrap.wglMakeCurrent(_hdc, _hglrc))
            throw new InvalidOperationException("wglMakeCurrent failed for hidden Skia GL context.");

        _glInterface = GRGlInterface.Create()
            ?? throw new InvalidOperationException("GRGlInterface.Create() failed for hidden Skia GL context.");
        _grContext = GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException("GRContext.CreateGl failed for hidden Skia GL context.");

        System.Diagnostics.Debug.WriteLine(
            $"[GdiGLReadbackSkiaSurfaceHost] GL vendor='{WglBootstrap.GetGLString(WglBootstrap.GL_VENDOR)}' " +
            $"renderer='{WglBootstrap.GetGLString(WglBootstrap.GL_RENDERER)}' " +
            $"version='{WglBootstrap.GetGLString(WglBootstrap.GL_VERSION)}'");
    }

    private void MakeContextCurrent()
    {
        if (WglBootstrap.wglGetCurrentContext() != _hglrc)
        {
            WglBootstrap.wglMakeCurrent(_hdc, _hglrc);
        }
    }

    private void AllocateGLFramebuffer(int width, int height)
    {
        // Color texture
        SkiaGLInterop.GenTextures(1, out _glTexture);
        if (_glTexture == 0) throw new InvalidOperationException("glGenTextures returned 0.");
        SkiaGLInterop.BindTexture(SkiaGLInterop.GL_TEXTURE_2D, _glTexture);
        // BGRA external format hint — driver-allocated storage matches DIB byte order so
        // glReadPixels(GL_BGRA, ...) takes the fast no-swizzle path. Internal format stays
        // GL_RGBA8 (the standard 32-bit RGBA storage class).
        SkiaGLInterop.TexImage2D(SkiaGLInterop.GL_TEXTURE_2D, 0, (int)SkiaGLInterop.GL_RGBA8,
            width, height, 0, WglBootstrap.GL_BGRA, SkiaGLInterop.GL_UNSIGNED_BYTE, 0);
        SkiaGLInterop.TexParameteri(SkiaGLInterop.GL_TEXTURE_2D, SkiaGLInterop.GL_TEXTURE_MIN_FILTER, (int)SkiaGLInterop.GL_LINEAR);
        SkiaGLInterop.TexParameteri(SkiaGLInterop.GL_TEXTURE_2D, SkiaGLInterop.GL_TEXTURE_MAG_FILTER, (int)SkiaGLInterop.GL_LINEAR);
        SkiaGLInterop.TexParameteri(SkiaGLInterop.GL_TEXTURE_2D, SkiaGLInterop.GL_TEXTURE_WRAP_S, (int)SkiaGLInterop.GL_CLAMP_TO_EDGE);
        SkiaGLInterop.TexParameteri(SkiaGLInterop.GL_TEXTURE_2D, SkiaGLInterop.GL_TEXTURE_WRAP_T, (int)SkiaGLInterop.GL_CLAMP_TO_EDGE);

        // FBO
        uint fbo;
        unsafe { SkiaGLInterop.GenFramebuffers(1, &fbo); }
        _glFbo = fbo;
        SkiaGLInterop.BindFramebuffer(SkiaGLInterop.GL_FRAMEBUFFER, _glFbo);
        SkiaGLInterop.FramebufferTexture2D(SkiaGLInterop.GL_FRAMEBUFFER, SkiaGLInterop.GL_COLOR_ATTACHMENT0,
            SkiaGLInterop.GL_TEXTURE_2D, _glTexture, 0);

        // Stencil for Skia path AA / clip
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
            throw new InvalidOperationException($"Skia GL FBO incomplete (0x{status:X8}).");
    }

    private void CreateSkSurface(int width, int height)
    {
        _renderTarget = new GRBackendRenderTarget(
            width, height, sampleCount: 0, stencilBits: 8,
            new GRGlFramebufferInfo(_glFbo, SkiaGLInterop.GL_RGBA8));
        _skSurface = SKSurface.Create(_grContext!, _renderTarget,
            // TopLeft → Skia draws inverted so the FBO holds top-down pixels; glReadPixels
            // (which reads bottom-up by default) then produces a top-down BGRA buffer that
            // matches the top-down DIB without an extra flip.
            GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888)
            ?? throw new InvalidOperationException("SKSurface.Create (GL FBO) returned null.");
    }

    private void AllocateDib(int width, int height)
    {
        var bmi = GdiDibInterop.Create32bppTopDown(width, height);
        nint screenDc = GdiDibInterop.GetDC(0);
        try
        {
            _dibHandle = GdiDibInterop.CreateDIBSection(screenDc, ref bmi, GdiDibInterop.DIB_RGB_COLORS, out _dibBits, 0, 0);
        }
        finally
        {
            GdiDibInterop.ReleaseDC(0, screenDc);
        }

        if (_dibHandle == 0 || _dibBits == 0)
            throw new InvalidOperationException("CreateDIBSection failed for GL readback sink.");
    }

    private void ReleaseSurfaceResources()
    {
        _image?.Dispose(); _image = null;
        _skSurface?.Dispose(); _skSurface = null;
        _renderTarget?.Dispose(); _renderTarget = null;

        if (_glFbo != 0) { uint fbo = _glFbo; unsafe { SkiaGLInterop.DeleteFramebuffers(1, &fbo); } _glFbo = 0; }
        if (_glStencil != 0) { uint rb = _glStencil; unsafe { SkiaGLInterop.DeleteRenderbuffers(1, &rb); } _glStencil = 0; }
        if (_glTexture != 0) { uint t = _glTexture; SkiaGLInterop.DeleteTextures(1, ref t); _glTexture = 0; }

        if (_dibHandle != 0) { GdiDibInterop.DeleteObject(_dibHandle); _dibHandle = 0; _dibBits = 0; }

        _pixelWidth = 0;
        _pixelHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseSurfaceResources();

        _grContext?.Dispose(); _grContext = null;
        _glInterface?.Dispose(); _glInterface = null;

        if (_hglrc != 0)
        {
            WglBootstrap.wglMakeCurrent(0, 0);
            WglBootstrap.wglDeleteContext(_hglrc);
            _hglrc = 0;
        }
        if (_hdc != 0 && _hwnd != 0)
        {
            WglBootstrap.ReleaseDC(_hwnd, _hdc);
            _hdc = 0;
        }
        if (_hwnd != 0)
        {
            WglBootstrap.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }
}
