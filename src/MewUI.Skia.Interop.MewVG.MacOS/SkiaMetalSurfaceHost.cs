using Aprillz.MewUI.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Interop.MewVG.MacOS;

/// <summary>
/// macOS Metal GPU host. Skia GR Metal shares the system <c>MTLDevice</c> with MewVG's Metal
/// backend so the produced <c>MTLTexture</c> is sample-able by MewVG without a cross-resource
/// copy.
/// </summary>
internal sealed class SkiaMetalSurfaceHost : ISkiaSurfaceHost
{
    private readonly IGraphicsFactory _factory;

    private nint _device;
    private nint _commandQueue;
    private IExternalWritableGpuSurface? _renderSurface;
    private IImage? _image;
    private GRContext? _grContext;
    private SKSurface? _surface;
    private GRBackendTexture? _backendTexture;
    private GpuResourceAffinity? _writeAffinity;

    private int _pixelWidth;
    private int _pixelHeight;
    private bool _disposed;

    // One sync Paint after each size change to dodge the recreation-frame Skia/MewVG race.
    private bool _flushSyncForNextPaint;

    public SkiaMetalSurfaceHost(IGraphicsFactory factory)
    {
        _factory = factory;
    }

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public bool SurfaceInvalidated => false;
    public string Description => "GPU zero-copy (Skia Metal → MewVG Metal)";

    public bool EnsureSurface(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (pixelWidth <= 0 || pixelHeight <= 0) return false;
        if (_surface != null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight) return true;

        ReleaseSurfaceResources();
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _flushSyncForNextPaint = true;

        try
        {
            var surface = _factory.CreateSurface(RenderSurfaceDescriptor.ExternalGpuWritable(
                pixelWidth, pixelHeight, dpiScale: 1.0, hasAlpha: true,
                debugName: "SkiaMetalSurfaceHost"));
            _renderSurface = surface as IExternalWritableGpuSurface
                ?? throw new InvalidOperationException("Backend offscreen surface does not support external GPU writes.");

            using (var writeScope = _renderSurface.BeginExternalWrite())
            {
                CaptureWriteAffinity(writeScope);
                EnsureGrContext(writeScope);
                CreateSkSurface(writeScope, pixelWidth, pixelHeight);
            }

            _image = _factory.CreateImageView(_renderSurface);
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

        if (_renderSurface == null || _surface == null || _grContext == null) return null;

        using var writeScope = _renderSurface.BeginExternalWrite();
        if (HasWriteAffinityChanged(writeScope))
        {
            _surface?.Dispose(); _surface = null;
            _backendTexture?.Dispose(); _backendTexture = null;
            _image?.Dispose(); _image = null;
            _writeAffinity = null;
            return null;
        }

        painter(_surface);

        bool syncFlush = _flushSyncForNextPaint;
        _flushSyncForNextPaint = false;

        _surface.Flush(submit: true, synchronous: syncFlush);
        _grContext.Flush(submit: true, synchronous: syncFlush);
        writeScope.Flush();
        _renderSurface.MarkExternalContentChanged();

        return _image;
    }

    private void EnsureGrContext(IExternalGpuWriteScope scope)
    {
        nint device = scope.NativeDeviceHandle;
        nint commandQueue = scope.NativeAlternateHandle;
        if (device == 0)
            throw new InvalidOperationException("Backend write scope returned a null MTLDevice.");
        if (commandQueue == 0)
            throw new InvalidOperationException("Backend write scope returned a null MTLCommandQueue.");

        if (_grContext != null && _device == device && _commandQueue == commandQueue) return;

        _grContext?.Dispose();
        _grContext = null;
        _device = device;
        _commandQueue = commandQueue;

        using var backendContext = new GRMtlBackendContext
        {
            DeviceHandle = _device,
            QueueHandle = _commandQueue,
        };

        _grContext = GRContext.CreateMetal(backendContext)
            ?? throw new InvalidOperationException("GRContext.CreateMetal failed.");
    }

    private void CreateSkSurface(IExternalGpuWriteScope scope, int width, int height)
    {
        nint texture = scope.NativeHandle;
        if (texture == 0)
            throw new InvalidOperationException("Backend write scope returned a null MTLTexture.");

        _backendTexture = new GRBackendTexture(width, height, mipmapped: false, new GRMtlTextureInfo(texture));

        _surface = SKSurface.Create(_grContext!, _backendTexture, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888)
            ?? throw new InvalidOperationException("SKSurface.Create (Metal backend texture) returned null.");
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
        _image?.Dispose(); _image = null;
        _surface?.Dispose(); _surface = null;
        _backendTexture?.Dispose(); _backendTexture = null;
        _renderSurface?.Dispose(); _renderSurface = null;
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
        _device = 0;
        _commandQueue = 0;
    }
}
