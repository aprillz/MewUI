using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering.Simd;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI bitmap image implementation.
/// </summary>
internal sealed class GdiImage : IImage
{
    private readonly IPixelBufferSource? _source;
    private nint _bits;
    private bool _disposed;
    private (ScaledKey key, nint handle) _scaled;
    private int _sourceVersion = -1;
    private nint _gpBitmap;
    private int _gpBitmapVersion = -1;

    // Transformed image cache: stores a pre-rendered rotated/scaled DIB so that
    // subsequent frames can blit via fast GDI AlphaBlend instead of GDI+ DrawImage.
    private nint _txBitmap;     // cached HBITMAP (DIB section)
    private nint _txBits;       // pixel pointer of the cached DIB
    private int _txW, _txH;     // cached bitmap dimensions
    private TransformedKey _txKey;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    internal nint Handle { get; private set; }
    internal nint Bits => _bits;

    /// <summary>
    /// Creates a 32-bit ARGB bitmap.
    /// </summary>
    public GdiImage(int width, int height)
    {
        PixelWidth = width;
        PixelHeight = height;

        var bmi = BITMAPINFO.Create32bpp(width, height);

        nint screenDc = User32.GetDC(0);
        try
        {
            Handle = Gdi32.CreateDIBSection(screenDc, ref bmi, 0, out _bits, 0, 0);
            if (Handle == 0)
            {
                throw new InvalidOperationException("Failed to create DIB section");
            }
        }
        finally
        {
            User32.ReleaseDC(0, screenDc);
        }
    }

    public GdiImage(IPixelBufferSource source) : this(
        source?.PixelWidth ?? throw new ArgumentNullException(nameof(source)),
        source.PixelHeight)
    {
        if (source.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {source.PixelFormat}");
        }

        _source = source;
        _sourceVersion = -1;
        EnsureUpToDate();
    }

    /// <summary>
    /// Creates an image from raw pixel data (BGRA format).
    /// </summary>
    public GdiImage(int width, int height, byte[] pixelData) : this(width, height)
    {
        if (pixelData.Length != width * height * 4)
        {
            throw new ArgumentException("Invalid pixel data size", nameof(pixelData));
        }

        if (pixelData.Length == 0)
        {
            return;
        }

        CopyToDibPremultiplied(pixelData);
    }

    internal void EnsureUpToDate()
    {
        if (_disposed || Handle == 0 || _bits == 0 || _source == null)
        {
            return;
        }

        int v = _source.Version;
        if (_sourceVersion == v)
        {
            return;
        }

        using (var l = _source.Lock())
        {
            v = l.Version;
            if (_sourceVersion == v)
            {
                return;
            }

            if (l.Buffer.Length == 0)
            {
                return;
            }

            CopyToDibPremultiplied(l.Buffer, sourceIsPremultiplied: _source.IsPremultiplied);
            _sourceVersion = v;

            if (_gpBitmap != 0)
            {
                GdiPlusInterop.GdipDisposeImage(_gpBitmap);
                _gpBitmap = 0;
            }

            if (_txBitmap != 0)
            {
                Gdi32.DeleteObject(_txBitmap);
                _txBitmap = 0;
                _txBits = 0;
            }

            if (_scaled != default)
            {
                Gdi32.DeleteObject(_scaled.handle);
                _scaled = default;
            }
        }
    }

    ~GdiImage() => ReleaseNativeHandles();

    public void Dispose()
    {
        ReleaseNativeHandles();
        GC.SuppressFinalize(this);
    }

    private void ReleaseNativeHandles()
    {
        if (!_disposed && Handle != 0)
        {
            if (_gpBitmap != 0)
            {
                GdiPlusInterop.GdipDisposeImage(_gpBitmap);
                _gpBitmap = 0;
            }

            if (_txBitmap != 0)
            {
                Gdi32.DeleteObject(_txBitmap);
                _txBitmap = 0;
                _txBits = 0;
            }

            if (_scaled != default)
            {
                Gdi32.DeleteObject(_scaled.handle);
                _scaled = default;
            }

            Gdi32.DeleteObject(Handle);
            Handle = 0;
            _bits = 0;
            _disposed = true;
        }
    }

    /// <summary>
    /// Returns a cached GDI+ bitmap handle wrapping the current pixel buffer (PixelFormat32bppPARGB).
    /// Recreated when the source version changes. The caller must NOT dispose this handle.
    /// </summary>
    internal nint GetGdiPlusBitmap()
    {
        const int PixelFormat32bppPARGB = 0x000E200B;
        int version = _sourceVersion;
        if (_gpBitmap != 0 && _gpBitmapVersion == version)
            return _gpBitmap;

        if (_gpBitmap != 0)
        {
            GdiPlusInterop.GdipDisposeImage(_gpBitmap);
            _gpBitmap = 0;
        }

        int stride = PixelWidth * 4;
        if (GdiPlusInterop.GdipCreateBitmapFromScan0(
                PixelWidth, PixelHeight, stride,
                PixelFormat32bppPARGB, _bits, out _gpBitmap) != 0)
        {
            _gpBitmap = 0;
        }

        _gpBitmapVersion = version;
        return _gpBitmap;
    }

    private readonly record struct ScaledKey(int SrcX, int SrcY, int SrcW, int SrcH, int DestW, int DestH, ImageScaleQuality Quality);

    // Cache key for transformed (rotated/skewed) rendering.
    // Uses the linear part of the transform (M11-M22) rounded to avoid float noise.
    // Translation is excluded because it only affects blit position, not pixel content.
    private readonly record struct TransformedKey(
        int Version,
        short M11, short M12, short M21, short M22,
        int SrcX, int SrcY, int SrcW, int SrcH,
        int DstW, int DstH);

    private static short FloatToKey(float v) => (short)MathF.Round(v * 1024f);

    /// <summary>
    /// Returns a cached pre-rendered DIB of this image with the given transform applied.
    /// On cache hit, returns the existing HBITMAP. On miss, renders to an offscreen DIB via GDI+.
    /// </summary>
    /// <param name="linearM11">M11 of transform (rotation/scale only, no translation).</param>
    /// <param name="linearM12">M12 of transform.</param>
    /// <param name="linearM21">M21 of transform.</param>
    /// <param name="linearM22">M22 of transform.</param>
    /// <param name="sourceRect">Source region of the image.</param>
    /// <param name="destWPx">Destination width in device pixels (before transform).</param>
    /// <param name="destHPx">Destination height in device pixels (before transform).</param>
    /// <param name="quality">Interpolation quality.</param>
    /// <param name="bitmap">The cached HBITMAP.</param>
    /// <param name="bitmapW">Width of the cached bitmap.</param>
    /// <param name="bitmapH">Height of the cached bitmap.</param>
    /// <returns>True if the cached bitmap is valid.</returns>
    internal bool TryGetTransformedBitmap(
        float linearM11, float linearM12, float linearM21, float linearM22,
        Rect sourceRect, int destWPx, int destHPx,
        ImageScaleQuality quality,
        out nint bitmap, out int bitmapW, out int bitmapH)
    {
        bitmap = 0;
        bitmapW = 0;
        bitmapH = 0;

        if (_disposed || Handle == 0 || _bits == 0)
            return false;

        var key = new TransformedKey(
            _sourceVersion,
            FloatToKey(linearM11), FloatToKey(linearM12),
            FloatToKey(linearM21), FloatToKey(linearM22),
            (int)sourceRect.X, (int)sourceRect.Y,
            (int)sourceRect.Width, (int)sourceRect.Height,
            destWPx, destHPx);

        if (_txBitmap != 0 && _txKey == key)
        {
            bitmap = _txBitmap;
            bitmapW = _txW;
            bitmapH = _txH;
            return true;
        }

        // Cache miss — render to offscreen DIB.

        // Compute AABB of the transformed dest rect centered at origin.
        float hw = destWPx * 0.5f;
        float hh = destHPx * 0.5f;
        // Transform the 4 corners of (-hw,-hh)→(hw,hh) through the linear matrix.
        // Linear: x' = x*M11 + y*M21, y' = x*M12 + y*M22
        float ax = linearM11 * -hw + linearM21 * -hh;
        float ay = linearM12 * -hw + linearM22 * -hh;
        float bx = linearM11 * hw + linearM21 * -hh;
        float by = linearM12 * hw + linearM22 * -hh;
        float cx = linearM11 * hw + linearM21 * hh;
        float cy = linearM12 * hw + linearM22 * hh;
        float dx = linearM11 * -hw + linearM21 * hh;
        float dy = linearM12 * -hw + linearM22 * hh;

        float minX = MathF.Min(MathF.Min(ax, bx), MathF.Min(cx, dx));
        float minY = MathF.Min(MathF.Min(ay, by), MathF.Min(cy, dy));
        float maxX = MathF.Max(MathF.Max(ax, bx), MathF.Max(cx, dx));
        float maxY = MathF.Max(MathF.Max(ay, by), MathF.Max(cy, dy));

        int outW = Math.Max(1, (int)MathF.Ceiling(maxX - minX));
        int outH = Math.Max(1, (int)MathF.Ceiling(maxY - minY));

        // Create offscreen DIB section.
        var bmi = BITMAPINFO.Create32bpp(outW, outH);
        nint screenDc = User32.GetDC(0);
        nint dibBits;
        nint dibBitmap;
        try
        {
            dibBitmap = Gdi32.CreateDIBSection(screenDc, ref bmi, 0, out dibBits, 0, 0);
        }
        finally
        {
            User32.ReleaseDC(0, screenDc);
        }

        if (dibBitmap == 0 || dibBits == 0)
            return false;

        // Create GDI+ Graphics on the offscreen DIB.
        nint memDc = Gdi32.CreateCompatibleDC(0);
        nint oldBmp = Gdi32.SelectObject(memDc, dibBitmap);
        bool ok = false;

        try
        {
            if (GdiPlusInterop.GdipCreateFromHDC(memDc, out nint offGfx) != 0 || offGfx == 0)
                return false;

            try
            {
                GdiPlusInterop.GdipSetSmoothingMode(offGfx, GdiPlusInterop.SmoothingMode.AntiAlias);
                GdiPlusInterop.GdipSetCompositingMode(offGfx, GdiPlusInterop.CompositingMode.SourceOver);

                var interpMode = quality switch
                {
                    ImageScaleQuality.Fast => GdiPlusInterop.InterpolationMode.NearestNeighbor,
                    ImageScaleQuality.HighQuality => GdiPlusInterop.InterpolationMode.HighQualityBicubic,
                    _ => GdiPlusInterop.InterpolationMode.Bilinear,
                };
                GdiPlusInterop.GdipSetInterpolationMode(offGfx, interpMode);

                // Set transform: translate center of output → apply linear transform → place dest rect at center.
                float ocx = outW * 0.5f;
                float ocy = outH * 0.5f;
                // Combined: T(ocx,ocy) * Linear * T(-hw,-hh)
                // = translate so that (0,0)→(destW,destH) ends up centered and rotated in the output bitmap.
                // dx = -hw*M11 - hh*M21 + ocx, dy = -hw*M12 - hh*M22 + ocy
                float m31 = linearM11 * -hw + linearM21 * -hh + ocx;
                float m32 = linearM12 * -hw + linearM22 * -hh + ocy;

                if (GdiPlusInterop.GdipCreateMatrix2(
                        linearM11, linearM12,
                        linearM21, linearM22,
                        m31, m32, out nint matrix) == 0 && matrix != 0)
                {
                    GdiPlusInterop.GdipSetWorldTransform(offGfx, matrix);
                    GdiPlusInterop.GdipDeleteMatrix(matrix);
                }

                var gpBmp = GetGdiPlusBitmap();
                if (gpBmp == 0)
                    return false;

                GdiPlusInterop.GdipDrawImageRectRect(
                    offGfx, gpBmp,
                    0, 0, destWPx, destHPx,
                    (float)sourceRect.X, (float)sourceRect.Y,
                    (float)sourceRect.Width, (float)sourceRect.Height,
                    GdiPlusInterop.Unit.Pixel, 0, 0, 0);

                ok = true;
            }
            finally
            {
                GdiPlusInterop.GdipDeleteGraphics(offGfx);
            }
        }
        finally
        {
            Gdi32.SelectObject(memDc, oldBmp);
            Gdi32.DeleteDC(memDc);

            if (!ok)
            {
                Gdi32.DeleteObject(dibBitmap);
            }
        }

        // Store in cache (evict previous).
        if (_txBitmap != 0)
            Gdi32.DeleteObject(_txBitmap);

        _txBitmap = dibBitmap;
        _txBits = dibBits;
        _txW = outW;
        _txH = outH;
        _txKey = key;

        bitmap = dibBitmap;
        bitmapW = outW;
        bitmapH = outH;
        return true;
    }

    public bool TryGetOrCreateScaledBitmap(
        int srcX,
        int srcY,
        int srcW,
        int srcH,
        int destW,
        int destH,
        ImageScaleQuality quality,
        out nint scaledBitmap)
    {
        scaledBitmap = 0;
        if (_disposed || Handle == 0 || _bits == 0)
        {
            return false;
        }

        EnsureUpToDate();

        if (srcW <= 0 || srcH <= 0 || destW <= 0 || destH <= 0)
        {
            return false;
        }

        // This cache is only intended for resampling paths. Nearest-neighbor uses GDI stretch directly.
        if (quality == ImageScaleQuality.Fast)
        {
            return false;
        }

        if (quality == ImageScaleQuality.Default)
        {
            quality = ImageScaleQuality.Normal;
        }

        // Clamp to image bounds.
        if (srcX < 0)
        {
            srcX = 0;
        }

        if (srcY < 0)
        {
            srcY = 0;
        }

        if (srcX + srcW > PixelWidth)
        {
            srcW = PixelWidth - srcX;
        }

        if (srcY + srcH > PixelHeight)
        {
            srcH = PixelHeight - srcY;
        }

        if (srcW <= 0 || srcH <= 0)
        {
            return false;
        }

        var key = new ScaledKey(srcX, srcY, srcW, srcH, destW, destH, quality);
        if (_scaled.key == key)
        {
            scaledBitmap = _scaled.handle;
            return true;
        }

        var bmi = BITMAPINFO.Create32bpp(destW, destH);
        nint screenDc = User32.GetDC(0);
        nint dstBits = 0;
        nint dstBitmap = 0;
        try
        {
            dstBitmap = Gdi32.CreateDIBSection(screenDc, ref bmi, 0, out dstBits, 0, 0);
            if (dstBitmap == 0 || dstBits == 0)
            {
                return false;
            }

            unsafe
            {
                byte* srcBase = (byte*)_bits + (srcY * PixelWidth + srcX) * 4;
                byte* dstBase = (byte*)dstBits;

                // Fast path: integer downscale (e.g. 256 -> 32).
                if (srcW % destW == 0 && srcH % destH == 0 && destW <= srcW && destH <= srcH)
                {
                    int fx = srcW / destW;
                    int fy = srcH / destH;
                    int blockCount = fx * fy;

                    // Common case: 2x downsample in both dimensions.
                    if (fx == 2 && fy == 2)
                    {
                        SimdDispatcher.Downsample2xBoxPremultipliedBgra(
                            srcBase,
                            srcStrideBytes: PixelWidth * 4,
                            srcWidth: srcW,
                            srcHeight: srcH,
                            dstBase,
                            dstStrideBytes: destW * 4,
                            dstWidth: destW,
                            dstHeight: destH);
                    }
                    else
                    {
                        for (int dy = 0; dy < destH; dy++)
                        {
                            byte* dstRow = dstBase + dy * destW * 4;
                            byte* srcRow0 = srcBase + (dy * fy) * PixelWidth * 4;

                            for (int dx = 0; dx < destW; dx++)
                            {
                                uint sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                                byte* block = srcRow0 + dx * fx * 4;

                                for (int by = 0; by < fy; by++)
                                {
                                    byte* s = block + by * PixelWidth * 4;
                                    for (int bx = 0; bx < fx; bx++)
                                    {
                                        sumB += s[0];
                                        sumG += s[1];
                                        sumR += s[2];
                                        sumA += s[3];
                                        s += 4;
                                    }
                                }

                                dstRow[0] = (byte)(sumB / (uint)blockCount);
                                dstRow[1] = (byte)(sumG / (uint)blockCount);
                                dstRow[2] = (byte)(sumR / (uint)blockCount);
                                dstRow[3] = (byte)(sumA / (uint)blockCount);
                                dstRow += 4;
                            }
                        }
                    }
                }
                else
                {
                    byte* workSrc = srcBase;
                    int workStrideBytes = PixelWidth * 4;
                    int workW = srcW;
                    int workH = srcH;
                    nint workBuffer = 0;

                    try
                    {
                        // HighQuality downscale path: mipmap-like prefiltering (2x box filter)
                        // until the remaining downscale is small enough for bilinear.
                        if (quality == ImageScaleQuality.HighQuality && (destW < srcW || destH < srcH))
                        {
                            for (; ; )
                            {
                                int nextW = (workW + 1) / 2;
                                int nextH = (workH + 1) / 2;

                                if (nextW < destW || nextH < destH)
                                {
                                    break;
                                }

                                if (workW < destW * 2 && workH < destH * 2)
                                {
                                    break;
                                }

                                int nextStride = nextW * 4;
                                nint nextBuffer = (nint)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)(nextStride * nextH));
                                if (nextBuffer == 0)
                                {
                                    break;
                                }

                                byte* nextPtr = (byte*)nextBuffer;
                                SimdDispatcher.Downsample2xBoxPremultipliedBgra(
                                    workSrc,
                                    workStrideBytes,
                                    workW,
                                    workH,
                                    nextPtr,
                                    nextStride,
                                    nextW,
                                    nextH);

                                if (workBuffer != 0)
                                {
                                    System.Runtime.InteropServices.NativeMemory.Free((void*)workBuffer);
                                }

                                workBuffer = nextBuffer;
                                workSrc = nextPtr;
                                workStrideBytes = nextStride;
                                workW = nextW;
                                workH = nextH;
                            }
                        }

                        // Sampling (premultiplied BGRA):
                        // - Linear => bilinear
                        // - HighQuality => bilinear (downscale after prefilter) or bicubic (upscale)
                        float scaleX = (float)workW / destW;
                        float scaleY = (float)workH / destH;

                        bool useBicubic = quality == ImageScaleQuality.HighQuality && (destW > workW || destH > workH);

                        int[]? x0Arr = null;
                        int[]? x1Arr = null;
                        float[]? wxArr = null;

                        if (!useBicubic)
                        {
                            x0Arr = System.Buffers.ArrayPool<int>.Shared.Rent(destW);
                            x1Arr = System.Buffers.ArrayPool<int>.Shared.Rent(destW);
                            wxArr = System.Buffers.ArrayPool<float>.Shared.Rent(destW);
                        }

                        try
                        {
                            if (!useBicubic)
                            {
                                for (int dx = 0; dx < destW; dx++)
                                {
                                    float sx = (dx + 0.5f) * scaleX - 0.5f;
                                    int x0 = (int)MathF.Floor(sx);
                                    float wx = sx - x0;
                                    if (x0 < 0) { x0 = 0; wx = 0; }
                                    int x1 = Math.Min(workW - 1, x0 + 1);

                                    x0Arr![dx] = x0;
                                    x1Arr![dx] = x1;
                                    wxArr![dx] = wx;
                                }
                            }

                            for (int dy = 0; dy < destH; dy++)
                            {
                                float sy = (dy + 0.5f) * scaleY - 0.5f;
                                int y0 = (int)MathF.Floor(sy);
                                float wy = sy - y0;
                                if (y0 < 0) { y0 = 0; wy = 0; }
                                int y1 = Math.Min(workH - 1, y0 + 1);

                                byte* dstRow = dstBase + dy * destW * 4;
                                byte* row0 = workSrc + y0 * workStrideBytes;
                                byte* row1 = workSrc + y1 * workStrideBytes;

                                if (!useBicubic)
                                {
                                    float wy0 = 1 - wy;

                                    for (int dx = 0; dx < destW; dx++)
                                    {
                                        int x0 = x0Arr![dx];
                                        int x1 = x1Arr![dx];
                                        float wx = wxArr![dx];
                                        float wx0 = 1 - wx;

                                        byte* p00 = row0 + x0 * 4;
                                        byte* p10 = row0 + x1 * 4;
                                        byte* p01 = row1 + x0 * 4;
                                        byte* p11 = row1 + x1 * 4;

                                        float w00 = wx0 * wy0;
                                        float w10 = wx * wy0;
                                        float w01 = wx0 * wy;
                                        float w11 = wx * wy;

                                        float b = p00[0] * w00 + p10[0] * w10 + p01[0] * w01 + p11[0] * w11;
                                        float g = p00[1] * w00 + p10[1] * w10 + p01[1] * w01 + p11[1] * w11;
                                        float r = p00[2] * w00 + p10[2] * w10 + p01[2] * w01 + p11[2] * w11;
                                        float a = p00[3] * w00 + p10[3] * w10 + p01[3] * w01 + p11[3] * w11;

                                        dstRow[0] = (byte)Math.Clamp((int)MathF.Round(b), 0, 255);
                                        dstRow[1] = (byte)Math.Clamp((int)MathF.Round(g), 0, 255);
                                        dstRow[2] = (byte)Math.Clamp((int)MathF.Round(r), 0, 255);
                                        dstRow[3] = (byte)Math.Clamp((int)MathF.Round(a), 0, 255);
                                        dstRow += 4;
                                    }

                                    continue;
                                }

                                // Bicubic (Catmull-Rom, a = -0.5). More expensive, but produces smoother upscales.
                                static float Cubic(float x)
                                {
                                    x = MathF.Abs(x);
                                    const float a = -0.5f;

                                    if (x <= 1f)
                                    {
                                        return (a + 2f) * x * x * x - (a + 3f) * x * x + 1f;
                                    }

                                    if (x < 2f)
                                    {
                                        return a * x * x * x - 5f * a * x * x + 8f * a * x - 4f * a;
                                    }

                                    return 0f;
                                }

                                float syBase = (dy + 0.5f) * scaleY - 0.5f;
                                int iy = (int)MathF.Floor(syBase);
                                float ty = syBase - iy;

                                int y_1 = Math.Clamp(iy - 1, 0, workH - 1);
                                int y0c = Math.Clamp(iy + 0, 0, workH - 1);
                                int y1c = Math.Clamp(iy + 1, 0, workH - 1);
                                int y2c = Math.Clamp(iy + 2, 0, workH - 1);

                                float wy_1 = Cubic(1f + ty);
                                float wy0c = Cubic(0f + ty);
                                float wy1c = Cubic(1f - ty);
                                float wy2c = Cubic(2f - ty);

                                byte* row_1 = workSrc + y_1 * workStrideBytes;
                                byte* row0c = workSrc + y0c * workStrideBytes;
                                byte* row1c = workSrc + y1c * workStrideBytes;
                                byte* row2c = workSrc + y2c * workStrideBytes;

                                for (int dx = 0; dx < destW; dx++)
                                {
                                    float sxBase = (dx + 0.5f) * scaleX - 0.5f;
                                    int ix = (int)MathF.Floor(sxBase);
                                    float tx = sxBase - ix;

                                    int x_1 = Math.Clamp(ix - 1, 0, workW - 1);
                                    int x0c = Math.Clamp(ix + 0, 0, workW - 1);
                                    int x1c = Math.Clamp(ix + 1, 0, workW - 1);
                                    int x2c = Math.Clamp(ix + 2, 0, workW - 1);

                                    float wx_1 = Cubic(1f + tx);
                                    float wx0c = Cubic(0f + tx);
                                    float wx1c = Cubic(1f - tx);
                                    float wx2c = Cubic(2f - tx);

                                    float sumB = 0, sumG = 0, sumR = 0, sumA = 0;

                                    void Accumulate(byte* row, float wyw)
                                    {
                                        byte* p0 = row + x_1 * 4;
                                        byte* p1 = row + x0c * 4;
                                        byte* p2 = row + x1c * 4;
                                        byte* p3 = row + x2c * 4;

                                        float w0 = wx_1 * wyw;
                                        float w1 = wx0c * wyw;
                                        float w2 = wx1c * wyw;
                                        float w3 = wx2c * wyw;

                                        sumB += p0[0] * w0 + p1[0] * w1 + p2[0] * w2 + p3[0] * w3;
                                        sumG += p0[1] * w0 + p1[1] * w1 + p2[1] * w2 + p3[1] * w3;
                                        sumR += p0[2] * w0 + p1[2] * w1 + p2[2] * w2 + p3[2] * w3;
                                        sumA += p0[3] * w0 + p1[3] * w1 + p2[3] * w2 + p3[3] * w3;
                                    }

                                    Accumulate(row_1, wy_1);
                                    Accumulate(row0c, wy0c);
                                    Accumulate(row1c, wy1c);
                                    Accumulate(row2c, wy2c);

                                    dstRow[0] = (byte)Math.Clamp((int)MathF.Round(sumB), 0, 255);
                                    dstRow[1] = (byte)Math.Clamp((int)MathF.Round(sumG), 0, 255);
                                    dstRow[2] = (byte)Math.Clamp((int)MathF.Round(sumR), 0, 255);
                                    dstRow[3] = (byte)Math.Clamp((int)MathF.Round(sumA), 0, 255);
                                    dstRow += 4;
                                }
                            }
                        }
                        finally
                        {
                            if (x0Arr != null)
                            {
                                System.Buffers.ArrayPool<int>.Shared.Return(x0Arr);
                            }

                            if (x1Arr != null)
                            {
                                System.Buffers.ArrayPool<int>.Shared.Return(x1Arr);
                            }

                            if (wxArr != null)
                            {
                                System.Buffers.ArrayPool<float>.Shared.Return(wxArr);
                            }
                        }
                    }
                    finally
                    {
                        if (workBuffer != 0)
                        {
                            System.Runtime.InteropServices.NativeMemory.Free((void*)workBuffer);
                        }
                    }
                }
            }


            if (_scaled != default)
            {
                Gdi32.DeleteObject(_scaled.handle);
                _scaled = default;
            }

            _scaled = (key, dstBitmap);
            scaledBitmap = dstBitmap;
            dstBitmap = 0; // ownership transferred
            return true;
        }
        finally
        {
            if (dstBitmap != 0)
            {
                Gdi32.DeleteObject(dstBitmap);
            }

            User32.ReleaseDC(0, screenDc);
        }
    }

    private void CopyToDibPremultiplied(ReadOnlySpan<byte> pixelData, bool sourceIsPremultiplied = false)
    {
        if (_bits == 0 || pixelData.IsEmpty)
        {
            return;
        }

        if (pixelData.Length != PixelWidth * PixelHeight * 4)
        {
            throw new ArgumentException("Invalid pixel data size", nameof(pixelData));
        }

        // GDI AlphaBlend expects the source to be premultiplied alpha (AC_SRC_ALPHA).
        // Only premultiply if the source isn't already in that form — otherwise we'd
        // double-multiply RGB by alpha, dimming every semi-transparent pixel.
        bool needsPremultiply = false;
        if (!sourceIsPremultiplied)
        {
            for (int i = 3; i < pixelData.Length; i += 4)
            {
                if (pixelData[i] != 0xFF)
                {
                    needsPremultiply = true;
                    break;
                }
            }
        }

        unsafe
        {
            fixed (byte* src = pixelData)
            {
                if (!needsPremultiply)
                {
                    Buffer.MemoryCopy(src, (void*)_bits, pixelData.Length, pixelData.Length);
                    return;
                }

                var dstSpan = new Span<byte>((void*)_bits, pixelData.Length);
                SimdDispatcher.PremultiplyBgra(pixelData, dstSpan);
            }
        }
    }

    /// <summary>
    /// Saves the bitmap contents to a 32bpp BMP file (BGRA). Intended for debugging/diagnostics.
    /// </summary>
    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var ext = Path.GetExtension(path);
        if (!string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only .bmp is supported for now.");
        }

        if (_disposed || Handle == 0 || _bits == 0)
        {
            throw new ObjectDisposedException(nameof(GdiImage));
        }

        EnsureUpToDate();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        unsafe
        {
            var bytes = PixelWidth * PixelHeight * 4;
            var span = new ReadOnlySpan<byte>((void*)_bits, bytes);
            WriteBmp32(path, PixelWidth, PixelHeight, span);
        }
    }

    /// <summary>
    /// Saves a cached (or newly generated) scaled bitmap to a 32bpp BMP file (BGRA).
    /// </summary>
    public bool SaveScaled(string path, int srcX, int srcY, int srcW, int srcH, int destW, int destH)
    {
        if (!TryGetOrCreateScaledBitmap(srcX, srcY, srcW, srcH, destW, destH, ImageScaleQuality.HighQuality, out var scaledBmp) || scaledBmp == 0)
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        if (!string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only .bmp is supported for now.");
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        nint screenDc = User32.GetDC(0);
        try
        {
            // Request a top-down 32bpp BGRA DIB into a managed buffer.
            var bmi = BITMAPINFO.Create32bpp(destW, destH);
            var buf = new byte[destW * destH * 4];

            unsafe
            {
                fixed (byte* p = buf)
                {
                    int got = Gdi32.GetDIBits(screenDc, scaledBmp, 0, (uint)destH, (nint)p, ref bmi, 0);
                    if (got == 0)
                    {
                        return false;
                    }
                }
            }

            WriteBmp32(path, destW, destH, buf);
            return true;
        }
        finally
        {
            User32.ReleaseDC(0, screenDc);
        }
    }

    private static void WriteBmp32(string path, int width, int height, ReadOnlySpan<byte> bgraTopDown)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid bitmap size.");
        }

        int expected = width * height * 4;
        if (bgraTopDown.Length < expected)
        {
            throw new ArgumentException("Pixel buffer too small.", nameof(bgraTopDown));
        }

        // BITMAPFILEHEADER (14 bytes) + BITMAPINFOHEADER (40 bytes) + pixel data.
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        int pixelBytes = expected;
        int fileSize = fileHeaderSize + infoHeaderSize + pixelBytes;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var bw = new BinaryWriter(fs);

        // BITMAPFILEHEADER
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write((short)0);
        bw.Write((short)0);
        bw.Write(fileHeaderSize + infoHeaderSize);

        // BITMAPINFOHEADER
        bw.Write(infoHeaderSize);
        bw.Write(width);
        bw.Write(-height); // top-down
        bw.Write((short)1); // planes
        bw.Write((short)32); // bpp
        bw.Write(0); // BI_RGB
        bw.Write(pixelBytes);
        bw.Write(0); // x ppm
        bw.Write(0); // y ppm
        bw.Write(0); // clr used
        bw.Write(0); // clr important

        bw.Write(bgraTopDown.Slice(0, pixelBytes));
    }
}
