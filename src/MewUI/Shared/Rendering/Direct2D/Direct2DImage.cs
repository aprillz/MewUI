using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering.Simd;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed class Direct2DImage : IImage
{
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    private readonly IPixelBufferSource _pixels;
    private readonly bool _hasAlpha;  // false → skip premultiply scan, use ALPHA_MODE.IGNORE
    private int _pixelsVersion = -1;
    private byte[]? _premultiplied;
    private nint _renderTarget;
    private int _renderTargetGeneration;
    private nint _bitmap;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public Direct2DImage(DecodedBitmap bmp)
    {
        if (bmp.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {bmp.PixelFormat}");
        }

        PixelWidth = bmp.WidthPx;
        PixelHeight = bmp.HeightPx;
        _pixels = new StaticPixelBufferSource(bmp.WidthPx, bmp.HeightPx, bmp.Data, bmp.HasAlpha);
        _pixelsVersion = _pixels.Version;
        _hasAlpha = bmp.HasAlpha;
    }

    public Direct2DImage(IPixelBufferSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {source.PixelFormat}");
        }

        PixelWidth = source.PixelWidth;
        PixelHeight = source.PixelHeight;
        _pixels = source;
        _pixelsVersion = source.Version;
        _hasAlpha = source.HasAlpha;
    }

    public nint GetOrCreateBitmap(nint renderTarget, int renderTargetGeneration)
    {
        if (_disposed || renderTarget == 0)
        {
            return 0;
        }

        // GPU fast path via the ID2DTextureSource backend marker. When the source exposes
        // a native ID2D1Bitmap1* tied to the same device context as the consumer's render
        // target, we return that bitmap directly — no Lock readback, no CPU premultiply,
        // no CreateBitmap upload. Offscreen render surfaces and image filter
        // outputs hit this when both share the factory's shared filter device context
        // (filter cache -> offscreen render = pure GPU draw, zero CPU touch).
        //
        // Cross-backend sources (GL FBO, Metal texture, etc.) don't implement
        // ID2DTextureSource — the cast naturally fails and we fall through to the CPU
        // readback path below.
        if (_pixels is ID2DTextureSource d2dSource
            && d2dSource.OwningDeviceContext == renderTarget
            && d2dSource.NativeBitmap is var nativeBitmap and not 0)
        {
            return nativeBitmap;
        }

        int v = _pixels.Version;
        bool versionChanged = _pixelsVersion != v;
        if (versionChanged)
        {
            _pixelsVersion = v;
            _premultiplied = null;
            ReleaseBitmap();
            _renderTarget = 0;
            _renderTargetGeneration = 0;
        }

        if (_renderTarget != 0 && (_renderTarget != renderTarget || _renderTargetGeneration != renderTargetGeneration))
        {
            ReleaseBitmap();
            _renderTarget = 0;
            _renderTargetGeneration = 0;
        }

        if (_bitmap != 0 && _renderTarget == renderTarget && _renderTargetGeneration == renderTargetGeneration)
        {
            return _bitmap;
        }

        EnsurePremultiplied();
        if (_premultiplied is null || _premultiplied.Length == 0)
        {
            return 0;
        }

        _renderTarget = renderTarget;
        _renderTargetGeneration = renderTargetGeneration;

        // Opaque sources get ALPHA_MODE.IGNORE so the GPU skips the per-fragment blend math
        // (no source × srcAlpha + dst × (1 - srcAlpha)). Sources with alpha keep the standard
        // PREMULTIPLIED mode that D2D requires for straight blending.
        var alphaMode = _hasAlpha ? D2D1_ALPHA_MODE.PREMULTIPLIED : D2D1_ALPHA_MODE.IGNORE;
        var props = new D2D1_BITMAP_PROPERTIES(
            pixelFormat: new D2D1_PIXEL_FORMAT(DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode),
            dpiX: 96,
            dpiY: 96);

        unsafe
        {
            fixed (byte* p = _premultiplied)
            {
                int hr = D2D1VTable.CreateBitmap(
                    (ID2D1RenderTarget*)renderTarget,
                    new D2D1_SIZE_U((uint)PixelWidth, (uint)PixelHeight),
                    srcData: (nint)p,
                    pitch: (uint)(PixelWidth * 4),
                    props: props,
                    bitmap: out _bitmap);

                if (hr < 0 || _bitmap == 0)
                {
                    throw new InvalidOperationException($"ID2D1RenderTarget::CreateBitmap failed: 0x{hr:X8}");
                }
            }
        }

        return _bitmap;
    }

    private void EnsurePremultiplied()
    {
        if (_disposed || _premultiplied != null) return;

        using var l = _pixels.Lock();
        if (l.Buffer.Length == 0)
        {
            _premultiplied = Array.Empty<byte>();
            return;
        }

        // Opaque sources skip both the premultiply scan and the conservative "is every byte
        // 0xFF?" check — there's no alpha to multiply. The bitmap is created with
        // ALPHA_MODE.IGNORE, so D2D treats the high byte as undefined regardless of contents.
        if (!_hasAlpha)
        {
            _premultiplied = l.Buffer;
            return;
        }

        // D2D's BGRA8 format requires PREMULTIPLIED alpha mode (STRAIGHT is rejected by
        // ID2D1RenderTarget::CreateBitmap with WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT). If the
        // source is already premultiplied (offscreen RT — the hot path) we use the buffer
        // directly; only straight-alpha sources (PNG decode, raw bytes) pay the CPU
        // premultiply cost.
        _premultiplied = _pixels.IsPremultiplied ? l.Buffer : PremultiplyIfNeeded(l.Buffer);
    }

    private static byte[] PremultiplyIfNeeded(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0xFF) return Premultiply(bgra);
        }
        return bgra;
    }

    private static byte[] Premultiply(ReadOnlySpan<byte> bgra)
    {
        var dst = new byte[bgra.Length];
        SimdDispatcher.PremultiplyBgra(bgra, dst);
        return dst;
    }

    private void ReleaseBitmap()
    {
        if (_bitmap != 0)
        {
            ComHelpers.Release(_bitmap);
            _bitmap = 0;
        }
    }

    ~Direct2DImage() => ReleaseNativeHandles();

    public void Dispose()
    {
        ReleaseNativeHandles();
        GC.SuppressFinalize(this);
    }

    private void ReleaseNativeHandles()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseBitmap();
        _renderTarget = 0;
        _premultiplied = null;
    }
}
