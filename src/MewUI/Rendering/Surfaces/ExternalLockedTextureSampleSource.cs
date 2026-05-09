using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public sealed class ExternalLockedTextureSampleSource : IExternalSampleSource
{
    private readonly IExternalLockedTexture _texture;
    private readonly bool _ownsTexture;
    private readonly ExternalSamplePlane[] _planes;
    private bool _disposed;

    public ExternalLockedTextureSampleSource(
        IExternalLockedTexture texture,
        ExternalSampleSourceKind kind = ExternalSampleSourceKind.Unknown,
        bool ownsTexture = false)
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        _ownsTexture = ownsTexture;
        Kind = kind;
        Format = ToRenderPixelFormat(texture.AlphaMode);
        _planes =
        [
            new ExternalSamplePlane(
                0,
                texture.NativeHandle,
                texture.PixelWidth,
                texture.PixelHeight,
                0,
                Format)
        ];
    }

    public IExternalLockedTexture Texture => _texture;

    public ExternalSampleSourceKind Kind { get; }

    public int PixelWidth => _texture.PixelWidth;

    public int PixelHeight => _texture.PixelHeight;

    public RenderPixelFormat Format { get; }

    public BitmapAlphaMode AlphaMode => _texture.AlphaMode;

    public bool YFlipped => _texture.YFlipped;

    public SurfaceCapabilities Capabilities =>
        SurfaceCapabilities.ExternalHandle |
        SurfaceCapabilities.ExternallySynchronized |
        SurfaceCapabilities.GpuSampleable |
        SurfaceCapabilities.AsyncCompletion |
        (_texture.AlphaMode == BitmapAlphaMode.Ignore ? SurfaceCapabilities.None : SurfaceCapabilities.Alpha) |
        (_texture.AlphaMode == BitmapAlphaMode.Premultiplied ? SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None);

    public IReadOnlyList<ExternalSamplePlane> Planes => _planes;

    public nint NativeHandle => _texture.NativeHandle;

    public IDisposable AcquireForSampling()
    {
        _texture.Acquire();
        return new ReleaseScope(_texture);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsTexture)
        {
            _texture.Dispose();
        }
    }

    private static RenderPixelFormat ToRenderPixelFormat(BitmapAlphaMode alphaMode)
        => alphaMode == BitmapAlphaMode.Premultiplied
            ? RenderPixelFormat.Bgra8888Premultiplied
            : RenderPixelFormat.Bgra8888;

    private sealed class ReleaseScope : IDisposable
    {
        private IExternalLockedTexture? _texture;

        public ReleaseScope(IExternalLockedTexture texture)
        {
            _texture = texture;
        }

        public void Dispose()
        {
            var texture = Interlocked.Exchange(ref _texture, null);
            texture?.Release();
        }
    }
}
