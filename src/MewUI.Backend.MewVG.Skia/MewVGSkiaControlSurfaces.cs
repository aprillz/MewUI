using System.Runtime.CompilerServices;

using SkiaSharp;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGMetalSkiaControlSurface : ISkiaGpuControlSurface
{
    private readonly IMewVGMetalWindowInterop _resources;
    private bool _disposed;
    private bool _needsRecreate = true;
    private nint _texture;
    private GRBackendTexture? _backendTexture;
    private SKSurface? _surface;
    private SKImage? _image;

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public double DpiScale { get; private set; }

    public SKColorType ColorType => SKColorType.Bgra8888;

    public SKAlphaType AlphaType => SKAlphaType.Premul;

    public MewVGMetalSkiaControlSurface(
        IMewVGMetalWindowInterop resources,
        int pixelWidth,
        int pixelHeight,
        double dpiScale)
    {
        _resources = resources;
        Resize(pixelWidth, pixelHeight, dpiScale);
    }

    public void Resize(int pixelWidth, int pixelHeight, double dpiScale)
    {
        PixelWidth = Math.Max(1, pixelWidth);
        PixelHeight = Math.Max(1, pixelHeight);
        DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        _needsRecreate = true;
    }

    public void Draw(IGraphicsContext context, Rect bounds, bool redraw, Action<SKSurface> painter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(painter);

        EnsureSurface();

        if (_surface == null || _image == null)
        {
            return;
        }

        var grContext = MewVGSkiaGpuContexts.GetOrCreate(_resources);

        if (redraw)
        {
            painter(_surface);
            _surface.Flush(submit: true, synchronous: false);
            grContext.Flush(submit: true, synchronous: false);
        }

        if (context is not IMewVGMetalExternalCompositeContext metalContext ||
            !metalContext.TryBeginExternalComposite(out var state))
        {
            return;
        }

        try
        {
            using var renderTarget = new GRBackendRenderTarget(
                state.ViewportWidthPx,
                state.ViewportHeightPx,
                new GRMtlTextureInfo(state.DrawableTexture));

            using var surface = SKSurface.Create(
                grContext,
                renderTarget,
                GRSurfaceOrigin.TopLeft,
                ColorType);

            if (surface == null)
            {
                return;
            }

            var canvas = surface.Canvas;
            int restoreCount = canvas.Save();

            canvas.Scale((float)state.DpiScale, (float)state.DpiScale);

            if (state.ClipBoundsWorld.HasValue)
            {
                canvas.ClipRect(ToSkRect(state.ClipBoundsWorld.Value));
            }

            canvas.Concat(ToSkMatrix(state.Transform));

            using var paint = state.GlobalAlpha < 1f
                ? new SKPaint
                {
                    Color = new SKColor(
                        255,
                        255,
                        255,
                        (byte)Math.Clamp((int)Math.Round(state.GlobalAlpha * 255f), 0, 255))
                }
                : null;

            canvas.DrawImage(_image, ToSkRect(bounds), paint);
            canvas.RestoreToCount(restoreCount);

            surface.Flush(submit: true, synchronous: false);
            grContext.Flush(submit: true, synchronous: false);
        }
        finally
        {
            metalContext.EndExternalComposite();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseSurface();
    }

    private void EnsureSurface()
    {
        if (!_needsRecreate)
        {
            return;
        }

        ReleaseSurface();

        _texture = _resources.CreateSharedTexture(PixelWidth, PixelHeight);
        if (_texture == 0)
        {
            _needsRecreate = false;
            return;
        }

        var grContext = MewVGSkiaGpuContexts.GetOrCreate(_resources);
        _backendTexture = new GRBackendTexture(
            PixelWidth,
            PixelHeight,
            mipmapped: false,
            new GRMtlTextureInfo(_texture));

        _surface = SKSurface.Create(
            grContext,
            _backendTexture,
            GRSurfaceOrigin.TopLeft,
            sampleCount: 0,
            ColorType);

        _image = _surface != null
            ? SKImage.FromTexture(
                grContext,
                _backendTexture,
                GRSurfaceOrigin.TopLeft,
                ColorType,
                AlphaType)
            : null;

        _needsRecreate = false;
    }

    private void ReleaseSurface()
    {
        _image?.Dispose();
        _image = null;
        _surface?.Dispose();
        _surface = null;
        _backendTexture?.Dispose();
        _backendTexture = null;
        _resources.ReleaseSharedTexture(ref _texture);
    }

    private static SKRect ToSkRect(Rect rect)
        => new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    private static SKMatrix ToSkMatrix(System.Numerics.Matrix3x2 matrix)
        => new()
        {
            ScaleX = matrix.M11,
            SkewX = matrix.M21,
            TransX = matrix.M31,
            SkewY = matrix.M12,
            ScaleY = matrix.M22,
            TransY = matrix.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };
}

internal sealed class MewVGGlSkiaControlSurface : ISkiaGpuControlSurface
{
    private const uint GlRgba8 = 0x8058;

    private readonly IMewVGGlWindowInterop _resources;
    private bool _disposed;
    private bool _needsRecreate = true;
    private uint _texture;
    private uint _framebuffer;
    private uint _stencilRenderbuffer;
    private int _imageId;
    private SKSurface? _surface;

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public double DpiScale { get; private set; }

    public SKColorType ColorType => SKColorType.Rgba8888;

    public SKAlphaType AlphaType => SKAlphaType.Premul;

    public MewVGGlSkiaControlSurface(
        IMewVGGlWindowInterop resources,
        int pixelWidth,
        int pixelHeight,
        double dpiScale)
    {
        _resources = resources;
        Resize(pixelWidth, pixelHeight, dpiScale);
    }

    public void Resize(int pixelWidth, int pixelHeight, double dpiScale)
    {
        PixelWidth = Math.Max(1, pixelWidth);
        PixelHeight = Math.Max(1, pixelHeight);
        DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        _needsRecreate = true;
    }

    public void Draw(IGraphicsContext context, Rect bounds, bool redraw, Action<SKSurface> painter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(painter);

        EnsureGpuResources();

        if (_surface == null || _imageId == 0)
        {
            return;
        }

        if (redraw)
        {
            var grContext = MewVGSkiaGpuContexts.GetOrCreateCurrent(_resources);
            grContext.ResetContext(GRBackendState.All);
            painter(_surface);
            _surface.Flush();
            grContext.Flush();
        }

        if (context is not IMewVGGlExternalImageContext glContext)
        {
            throw new ArgumentException("OpenGL Skia surfaces require an active MewVG OpenGL graphics context.", nameof(context));
        }

        glContext.DrawExternalImage(_imageId, bounds, PixelWidth, PixelHeight);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resources.RunWithCurrentContext(ReleaseGpuResourcesCurrent);
    }

    private void EnsureGpuResources()
    {
        if (!_needsRecreate)
        {
            return;
        }

        ReleaseGpuResourcesCurrent();
        CreateGpuResourcesCurrent();
        _needsRecreate = false;
    }

    private unsafe void CreateGpuResourcesCurrent()
    {
        MewVGSkiaOpenGL.GenTextures(1, out _texture);
        if (_texture == 0)
        {
            throw new InvalidOperationException("Failed to create the OpenGL texture for the Skia GPU surface.");
        }

        MewVGSkiaOpenGL.BindTexture(MewVGSkiaOpenGL.GL_TEXTURE_2D, _texture);
        MewVGSkiaOpenGL.TexParameteri(MewVGSkiaOpenGL.GL_TEXTURE_2D, MewVGSkiaOpenGL.GL_TEXTURE_MIN_FILTER, (int)MewVGSkiaOpenGL.GL_LINEAR);
        MewVGSkiaOpenGL.TexParameteri(MewVGSkiaOpenGL.GL_TEXTURE_2D, MewVGSkiaOpenGL.GL_TEXTURE_MAG_FILTER, (int)MewVGSkiaOpenGL.GL_LINEAR);
        MewVGSkiaOpenGL.TexParameteri(MewVGSkiaOpenGL.GL_TEXTURE_2D, MewVGSkiaOpenGL.GL_TEXTURE_WRAP_S, (int)MewVGSkiaOpenGL.GL_CLAMP_TO_EDGE);
        MewVGSkiaOpenGL.TexParameteri(MewVGSkiaOpenGL.GL_TEXTURE_2D, MewVGSkiaOpenGL.GL_TEXTURE_WRAP_T, (int)MewVGSkiaOpenGL.GL_CLAMP_TO_EDGE);
        MewVGSkiaOpenGL.TexImage2D(
            MewVGSkiaOpenGL.GL_TEXTURE_2D,
            0,
            (int)MewVGSkiaOpenGL.GL_RGBA,
            PixelWidth,
            PixelHeight,
            0,
            MewVGSkiaOpenGL.GL_RGBA,
            MewVGSkiaOpenGL.GL_UNSIGNED_BYTE,
            0);

        uint framebuffer = 0;
        MewVGSkiaOpenGL.GenFramebuffers(1, &framebuffer);
        _framebuffer = framebuffer;
        if (_framebuffer == 0)
        {
            throw new InvalidOperationException("Failed to create the OpenGL framebuffer for the Skia GPU surface.");
        }

        MewVGSkiaOpenGL.BindFramebuffer(MewVGSkiaOpenGL.GL_FRAMEBUFFER, _framebuffer);
        MewVGSkiaOpenGL.FramebufferTexture2D(
            MewVGSkiaOpenGL.GL_FRAMEBUFFER,
            MewVGSkiaOpenGL.GL_COLOR_ATTACHMENT0,
            MewVGSkiaOpenGL.GL_TEXTURE_2D,
            _texture,
            0);

        int stencilBits = Math.Max(0, GraphicsRuntimeOptions.PreferredMewVGStencilBits);
        if (stencilBits > 0)
        {
            uint renderbuffer = 0;
            MewVGSkiaOpenGL.GenRenderbuffers(1, &renderbuffer);
            _stencilRenderbuffer = renderbuffer;

            if (_stencilRenderbuffer != 0)
            {
                MewVGSkiaOpenGL.BindRenderbuffer(MewVGSkiaOpenGL.GL_RENDERBUFFER, _stencilRenderbuffer);
                MewVGSkiaOpenGL.RenderbufferStorage(
                    MewVGSkiaOpenGL.GL_RENDERBUFFER,
                    MewVGSkiaOpenGL.GL_DEPTH24_STENCIL8,
                    PixelWidth,
                    PixelHeight);
                MewVGSkiaOpenGL.FramebufferRenderbuffer(
                    MewVGSkiaOpenGL.GL_FRAMEBUFFER,
                    MewVGSkiaOpenGL.GL_DEPTH_STENCIL_ATTACHMENT,
                    MewVGSkiaOpenGL.GL_RENDERBUFFER,
                    _stencilRenderbuffer);
                MewVGSkiaOpenGL.BindRenderbuffer(MewVGSkiaOpenGL.GL_RENDERBUFFER, 0);
            }
        }

        uint status = MewVGSkiaOpenGL.CheckFramebufferStatus(MewVGSkiaOpenGL.GL_FRAMEBUFFER);
        if (status != MewVGSkiaOpenGL.GL_FRAMEBUFFER_COMPLETE)
        {
            throw new InvalidOperationException($"OpenGL framebuffer for the Skia GPU surface is incomplete (0x{status:X8}).");
        }

        using var renderTarget = new GRBackendRenderTarget(
            PixelWidth,
            PixelHeight,
            sampleCount: 0,
            stencilBits: _stencilRenderbuffer != 0 ? 8 : 0,
            new GRGlFramebufferInfo(_framebuffer, GlRgba8));

        _surface = SKSurface.Create(
            MewVGSkiaGpuContexts.GetOrCreateCurrent(_resources),
            renderTarget,
            GRSurfaceOrigin.BottomLeft,
            ColorType)
            ?? throw new InvalidOperationException("SkiaSharp could not create an OpenGL surface for the MewVG Skia control.");

        _imageId = _resources.CreateExternalImage((int)_texture, PixelWidth, PixelHeight);
        if (_imageId == 0)
        {
            throw new InvalidOperationException("MewVG could not wrap the Skia GPU texture as an image.");
        }

        MewVGSkiaOpenGL.BindFramebuffer(MewVGSkiaOpenGL.GL_FRAMEBUFFER, 0);
        MewVGSkiaOpenGL.BindTexture(MewVGSkiaOpenGL.GL_TEXTURE_2D, 0);
    }

    private unsafe void ReleaseGpuResourcesCurrent()
    {
        _surface?.Dispose();
        _surface = null;

        if (_imageId != 0)
        {
            _resources.DeleteExternalImage(_imageId);
            _imageId = 0;
        }

        if (_stencilRenderbuffer != 0)
        {
            uint renderbuffer = _stencilRenderbuffer;
            MewVGSkiaOpenGL.DeleteRenderbuffers(1, &renderbuffer);
            _stencilRenderbuffer = 0;
        }

        if (_framebuffer != 0)
        {
            uint framebuffer = _framebuffer;
            MewVGSkiaOpenGL.DeleteFramebuffers(1, &framebuffer);
            _framebuffer = 0;
        }

        if (_texture != 0)
        {
            uint texture = _texture;
            MewVGSkiaOpenGL.DeleteTextures(1, ref texture);
            _texture = 0;
        }
    }
}

internal static class MewVGSkiaGpuContexts
{
    private static readonly ConditionalWeakTable<IMewVGGlWindowInterop, GlContextHolder> s_glContexts = new();
    private static readonly ConditionalWeakTable<IMewVGMetalWindowInterop, MetalContextHolder> s_metalContexts = new();

    public static GRContext GetOrCreateCurrent(IMewVGGlWindowInterop resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (!s_glContexts.TryGetValue(resources, out var holder))
        {
            var glInterface = GRGlInterface.Create()
                ?? throw new InvalidOperationException("SkiaSharp could not create a GL interface for the active MewVG context.");
            var context = GRContext.CreateGl(glInterface)
                ?? throw new InvalidOperationException("SkiaSharp could not create a GL GPU context for the active MewVG context.");
            holder = s_glContexts.GetValue(resources, _ => new GlContextHolder(glInterface, context));
        }

        return holder.Context;
    }

    public static GRContext GetOrCreate(IMewVGMetalWindowInterop resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return s_metalContexts.GetValue(resources, static key =>
        {
            using var backendContext = new GRMtlBackendContext
            {
                DeviceHandle = key.DeviceHandle,
                QueueHandle = key.CommandQueueHandle
            };

            var context = GRContext.CreateMetal(backendContext)
                ?? throw new InvalidOperationException("SkiaSharp could not create a Metal GPU context for the active MewVG window.");

            return new MetalContextHolder(context);
        }).Context;
    }

    private sealed class GlContextHolder : IDisposable
    {
        public GRGlInterface Interface { get; }

        public GRContext Context { get; }

        public GlContextHolder(GRGlInterface glInterface, GRContext context)
        {
            Interface = glInterface;
            Context = context;
        }

        public void Dispose()
        {
            Context.Dispose();
            Interface.Dispose();
        }
    }

    private sealed class MetalContextHolder : IDisposable
    {
        public GRContext Context { get; }

        public MetalContextHolder(GRContext context)
        {
            Context = context;
        }

        public void Dispose()
        {
            Context.Dispose();
        }
    }
}
