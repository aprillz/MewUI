using Aprillz.MewUI.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Rendering;

/// <summary>
/// Hosts Skia GR GL rendering on top of a backend-owned offscreen render target. The backend's
/// <see cref="IRenderDevice.CreateSurface(RenderSurfaceDescriptor)"/> allocates the GL FBO +
/// color texture; this host wraps that texture as a Skia <see cref="GRBackendTexture"/> and
/// exposes the same surface as an <see cref="IImage"/> for zero-copy sampling.
/// </summary>
internal sealed class SkiaGLSurfaceHost : ISkiaSurfaceHost
{
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_RGBA8 = 0x8058;

    private readonly IGraphicsFactory _factory;

    private IExternalWritableGpuSurface? _surface;
    private IImage? _image;
    private GRGlInterface? _glInterface;
    private GRContext? _grContext;
    private GRBackendTexture? _backendTexture;
    private SKSurface? _skSurface;
    private GpuResourceAffinity? _writeAffinity;

    private int _pixelWidth;
    private int _pixelHeight;
    private bool _disposed;
    private bool _surfaceInvalidated;

    public SkiaGLSurfaceHost(IGraphicsFactory factory)
    {
        _factory = factory;
    }

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public bool SurfaceInvalidated => _surfaceInvalidated;
    public string Description => "GPU zero-copy (Skia GL → backend GL texture)";

    public bool EnsureSurface(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (pixelWidth <= 0 || pixelHeight <= 0) return false;
        if (_skSurface != null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight) return true;

        ReleaseSurfaceResources();
        _surfaceInvalidated = false;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;

        try
        {
            var surface = _factory.CreateSurface(RenderSurfaceDescriptor.ExternalGpuWritable(
                pixelWidth, pixelHeight, dpiScale: 1.0, hasAlpha: true,
                debugName: "SkiaGLSurfaceHost"));
            _surface = surface as IExternalWritableGpuSurface
                ?? throw new InvalidOperationException("Backend offscreen surface does not support external GPU writes.");

            using (var writeScope = _surface.BeginExternalWrite())
            {
                CaptureWriteAffinity(writeScope);
                EnsureGrContext();

                uint textureId = (uint)writeScope.NativeHandle;
                if (textureId == 0)
                {
                    throw new InvalidOperationException("Backend offscreen surface did not expose a GL texture handle.");
                }

                _backendTexture = new GRBackendTexture(
                    pixelWidth, pixelHeight, mipmapped: false,
                    new GRGlTextureInfo(GL_TEXTURE_2D, textureId, GL_RGBA8));

                _skSurface = SKSurface.Create(
                    _grContext!, _backendTexture,
                    // GL FBO color attachments are bottom-up; MewVG flips at sample time so the
                    // two conventions cancel out.
                    GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
                    ?? throw new InvalidOperationException("SKSurface.Create failed wrapping the backend GL texture.");
            }

            _image = _factory.CreateImageView(_surface);
            return _image != null;
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

        if (_surface == null || _skSurface == null || _image == null || _grContext == null) return null;

        using var writeScope = _surface.BeginExternalWrite();
        if (HasWriteAffinityChanged(writeScope))
        {
            ReleaseSurfaceResources();
            _surfaceInvalidated = true;
            return null;
        }

        // Skia tracks GL state from its previous frame; MewVG may have changed shader / texture
        // / blend / scissor bindings since then. ResetContext forces Skia to re-bind everything.
        _grContext.ResetContext(GRBackendState.All);

        painter(_skSurface);

        _skSurface.Flush();
        _grContext.Flush();
        writeScope.Flush();
        _surface.MarkExternalContentChanged();

        return _image;
    }

    private void EnsureGrContext()
    {
        if (_grContext != null) return;
        _glInterface = GRGlInterface.Create()
            ?? throw new InvalidOperationException("GRGlInterface.Create returned null — no GL context current on this thread.");
        _grContext = GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException("GRContext.CreateGl returned null for the current GL context.");
    }

    private void CaptureWriteAffinity(IExternalGpuWriteScope scope)
        => _writeAffinity = (scope as IGpuResourceAffinityProvider)?.Affinity;

    private bool HasWriteAffinityChanged(IExternalGpuWriteScope scope)
    {
        var current = (scope as IGpuResourceAffinityProvider)?.Affinity;
        return _writeAffinity is { } previous && current is { } next && previous != next;
    }

    private void ReleaseSurfaceResources()
    {
        _skSurface?.Dispose(); _skSurface = null;
        _backendTexture?.Dispose(); _backendTexture = null;
        _image?.Dispose(); _image = null;
        _surface?.Dispose(); _surface = null;
        _writeAffinity = null;
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
    }
}
