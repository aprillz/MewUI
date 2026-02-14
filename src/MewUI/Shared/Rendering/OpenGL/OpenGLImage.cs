using Aprillz.MewUI.Native;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed class OpenGLImage : IImage
{
    private readonly IPixelBufferSource _pixels;
    private int _pixelsVersion = -1;
    private byte[]? _rgbaCache;
    private int _rgbaCacheVersion = -1;
    private int _rgbaCacheLength = -1;
    private List<byte[]>? _mipBuffers;
    private int _mipBuffersVersion = -1;
    private bool _mipBuffersAreBgra;
    private int _mipBuffersWidth;
    private int _mipBuffersHeight;
    private readonly Dictionary<nint, TextureEntry> _texturesByWindow = new();
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public OpenGLImage(int widthPx, int heightPx, byte[] bgra)
    {
        PixelWidth = widthPx;
        PixelHeight = heightPx;
        ArgumentNullException.ThrowIfNull(bgra);
        _pixels = new StaticPixelBufferSource(widthPx, heightPx, bgra);
        _pixelsVersion = 0;
    }

    public OpenGLImage(IPixelBufferSource source)
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
    }

    public readonly record struct TextureInfo(uint TextureId, int TextureWidth, int TextureHeight, float UMax, float VMax, bool HasMipmaps);

    private readonly record struct TextureEntry(
        uint TextureId,
        int Version,
        bool HasMipmaps,
        int TextureWidth,
        int TextureHeight,
        float UMax,
        float VMax);

    public TextureInfo GetOrCreateTexture(IOpenGLWindowResources resources, nint hwnd, bool wantMipmaps)
    {
        if (_disposed)
        {
            return default;
        }

        int version = _pixels.Version;
        if (_pixelsVersion != version)
        {
            _pixelsVersion = version;
            _rgbaCache = null;
            _rgbaCacheVersion = -1;
            _mipBuffers = null;
            _mipBuffersVersion = -1;
            _mipBuffersAreBgra = false;
        }

        if (_texturesByWindow.TryGetValue(hwnd, out var entry) &&
            entry.TextureId != 0 &&
            entry.Version == version &&
            entry.HasMipmaps == wantMipmaps)
        {
            return new TextureInfo(entry.TextureId, entry.TextureWidth, entry.TextureHeight, entry.UMax, entry.VMax, entry.HasMipmaps);
        }

        uint tex = entry.TextureId;
        if (tex == 0)
        {
            GL.GenTextures(1, out tex);
            if (tex == 0)
            {
                return default;
            }

            resources.TrackTexture(tex);

            GL.BindTexture(GL.GL_TEXTURE_2D, tex);
            GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, (int)GL.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, (int)GL.GL_CLAMP_TO_EDGE);
        }
        else
        {
            GL.BindTexture(GL.GL_TEXTURE_2D, tex);
        }

        byte[] pixels;
        using (var l = _pixels.Lock())
        {
            pixels = l.Buffer;
        }

        if (pixels.Length == 0)
        {
            return default;
        }

        int uploadWidth = PixelWidth;
        int uploadHeight = PixelHeight;
        float uMax = 1f;
        float vMax = 1f;

        bool uploadAsBgra = resources.SupportsBgra;
        byte[] level0Bgra = pixels;

        // If the context doesn't support NPOT textures, use a padded power-of-two texture when we want mipmaps.
        // This allows trilinear sampling (mips) in legacy contexts (e.g. Win32 opengl32.dll 1.1).
        if (wantMipmaps && !resources.SupportsNpotTextures &&
            (!IsPowerOfTwo(PixelWidth) || !IsPowerOfTwo(PixelHeight)))
        {
            uploadWidth = NextPowerOfTwo(PixelWidth);
            uploadHeight = NextPowerOfTwo(PixelHeight);
            uMax = PixelWidth / (float)uploadWidth;
            vMax = PixelHeight / (float)uploadHeight;
            level0Bgra = PadToTextureSize(pixels, PixelWidth, PixelHeight, uploadWidth, uploadHeight);
        }

        var level0 = GetUploadBuffer(version, level0Bgra, uploadAsBgra);
        uint format = uploadAsBgra ? GL.GL_BGRA_EXT : GL.GL_RGBA;

        if (wantMipmaps)
        {
            EnsureMipBuffers(version, level0, uploadWidth, uploadHeight, uploadAsBgra);
            UploadMipChain(format, uploadWidth, uploadHeight, _mipBuffers ?? new List<byte[]> { level0 });
        }
        else
        {
            UploadLevel(format, level: 0, PixelWidth, PixelHeight, level0);
        }

        var err = GL.GetError();
        if (err != 0)
        {
            DiagLog.Write($"[OpenGLImage] texture upload error=0x{err:X} wantMipmaps={wantMipmaps} npot={resources.SupportsNpotTextures} size={PixelWidth}x{PixelHeight} texSize={uploadWidth}x{uploadHeight} bgra={uploadAsBgra}");
        }

        _texturesByWindow[hwnd] = new TextureEntry(tex, version, wantMipmaps, uploadWidth, uploadHeight, uMax, vMax);
        return new TextureInfo(tex, uploadWidth, uploadHeight, uMax, vMax, wantMipmaps);
    }

    private byte[] GetUploadBuffer(int version, byte[] bgra, bool uploadAsBgra)
    {
        if (uploadAsBgra)
        {
            return bgra;
        }

        if (_rgbaCache == null || _rgbaCacheVersion != version || _rgbaCacheLength != bgra.Length)
        {
            _rgbaCache = OpenGLPixelUtils.ConvertBgraToRgba(bgra);
            _rgbaCacheVersion = version;
            _rgbaCacheLength = bgra.Length;
        }

        return _rgbaCache;
    }

    private void EnsureMipBuffers(int version, byte[] level0, int baseWidth, int baseHeight, bool buffersAreBgra)
    {
        if (_mipBuffers != null &&
            _mipBuffersVersion == version &&
            _mipBuffersAreBgra == buffersAreBgra &&
            _mipBuffersWidth == baseWidth &&
            _mipBuffersHeight == baseHeight)
        {
            return;
        }

        _mipBuffersAreBgra = buffersAreBgra;
        _mipBuffersVersion = version;
        _mipBuffersWidth = baseWidth;
        _mipBuffersHeight = baseHeight;
        _mipBuffers = new List<byte[]>();
        _mipBuffers.Add(level0);

        int srcW = baseWidth;
        int srcH = baseHeight;
        while (srcW > 1 || srcH > 1)
        {
            var src = _mipBuffers[^1];
            var dst = Downsample2x(src, srcW, srcH, buffersAreBgra, out int dstW, out int dstH);
            _mipBuffers.Add(dst);
            srcW = dstW;
            srcH = dstH;
        }
    }

    private static byte[] Downsample2x(byte[] src, int srcWidth, int srcHeight, bool srcIsBgra, out int dstWidth, out int dstHeight)
    {
        // OpenGL mip level sizing uses floor(w/2) and floor(h/2) for each successive level.
        // If we use ceil here, the mip chain becomes incomplete for odd-sized textures (e.g. 3 -> 2 -> 1),
        // and sampling with mip filters can produce white results.
        dstWidth = Math.Max(1, srcWidth / 2);
        dstHeight = Math.Max(1, srcHeight / 2);

        var dst = new byte[dstWidth * dstHeight * 4];

        int bIndex = srcIsBgra ? 0 : 2;
        int gIndex = 1;
        int rIndex = srcIsBgra ? 2 : 0;
        int aIndex = 3;

        for (int y = 0; y < dstHeight; y++)
        {
            int sy = y * 2;
            int sy1 = sy + 1;
            bool hasY1 = sy1 < srcHeight;

            for (int x = 0; x < dstWidth; x++)
            {
                int sx = x * 2;
                int sx1 = sx + 1;
                bool hasX1 = sx1 < srcWidth;

                int count = 1;
                int idx00 = (sy * srcWidth + sx) * 4;

                int aSum = src[idx00 + aIndex];
                int rSum = src[idx00 + rIndex] * aSum;
                int gSum = src[idx00 + gIndex] * aSum;
                int bSum = src[idx00 + bIndex] * aSum;

                if (hasX1)
                {
                    count++;
                    int idx10 = (sy * srcWidth + sx1) * 4;
                    int a10 = src[idx10 + aIndex];
                    aSum += a10;
                    rSum += src[idx10 + rIndex] * a10;
                    gSum += src[idx10 + gIndex] * a10;
                    bSum += src[idx10 + bIndex] * a10;
                }

                if (hasY1)
                {
                    count++;
                    int idx01 = (sy1 * srcWidth + sx) * 4;
                    int a01 = src[idx01 + aIndex];
                    aSum += a01;
                    rSum += src[idx01 + rIndex] * a01;
                    gSum += src[idx01 + gIndex] * a01;
                    bSum += src[idx01 + bIndex] * a01;

                    if (hasX1)
                    {
                        count++;
                        int idx11 = (sy1 * srcWidth + sx1) * 4;
                        int a11 = src[idx11 + aIndex];
                        aSum += a11;
                        rSum += src[idx11 + rIndex] * a11;
                        gSum += src[idx11 + gIndex] * a11;
                        bSum += src[idx11 + bIndex] * a11;
                    }
                }

                int di = (y * dstWidth + x) * 4;
                int aAvg = (aSum + (count / 2)) / count;
                dst[di + aIndex] = (byte)aAvg;

                if (aSum > 0)
                {
                    dst[di + rIndex] = (byte)Math.Clamp((rSum + (aSum / 2)) / aSum, 0, 255);
                    dst[di + gIndex] = (byte)Math.Clamp((gSum + (aSum / 2)) / aSum, 0, 255);
                    dst[di + bIndex] = (byte)Math.Clamp((bSum + (aSum / 2)) / aSum, 0, 255);
                }
                else
                {
                    dst[di + rIndex] = 0;
                    dst[di + gIndex] = 0;
                    dst[di + bIndex] = 0;
                }
            }
        }

        return dst;
    }

    private void UploadMipChain(uint format, int baseWidth, int baseHeight, List<byte[]> mips)
    {
        int w = baseWidth;
        int h = baseHeight;

        int levels = mips.Count;
        for (int level = 0; level < levels; level++)
        {
            UploadLevel(format, level, w, h, mips[level]);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
    }

    private static void UploadLevel(uint format, int level, int width, int height, byte[] pixels)
    {
        unsafe
        {
            fixed (byte* p = pixels)
            {
                GL.TexImage2D(
                    GL.GL_TEXTURE_2D,
                    level: level,
                    internalformat: (int)GL.GL_RGBA,
                    width: width,
                    height: height,
                    border: 0,
                    format: format,
                    type: GL.GL_UNSIGNED_BYTE,
                    pixels: (nint)p);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _texturesByWindow.Clear();
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static int NextPowerOfTwo(int value)
    {
        if (value <= 1)
        {
            return 1;
        }

        uint v = (uint)(value - 1);
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return (int)(v + 1);
    }

    private static byte[] PadToTextureSize(byte[] src, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        if (srcWidth == dstWidth && srcHeight == dstHeight)
        {
            return src;
        }

        if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
        {
            return Array.Empty<byte>();
        }

        var dst = new byte[dstWidth * dstHeight * 4];

        int srcStride = srcWidth * 4;
        int dstStride = dstWidth * 4;
        int copyBytes = Math.Min(srcStride, dstStride);

        for (int y = 0; y < srcHeight; y++)
        {
            int srcRow = y * srcStride;
            int dstRow = y * dstStride;
            Buffer.BlockCopy(src, srcRow, dst, dstRow, copyBytes);

            // Extend last pixel to the right to avoid sampling transparent/undefined pixels near edges.
            int lastPxOffset = srcRow + (srcWidth - 1) * 4;
            byte b0 = src[lastPxOffset + 0];
            byte b1 = src[lastPxOffset + 1];
            byte b2 = src[lastPxOffset + 2];
            byte b3 = src[lastPxOffset + 3];
            for (int x = srcWidth; x < dstWidth; x++)
            {
                int o = dstRow + x * 4;
                dst[o + 0] = b0;
                dst[o + 1] = b1;
                dst[o + 2] = b2;
                dst[o + 3] = b3;
            }
        }

        // Extend last row downward.
        int lastSrcRow = (srcHeight - 1) * dstStride;
        for (int y = srcHeight; y < dstHeight; y++)
        {
            Buffer.BlockCopy(dst, lastSrcRow, dst, y * dstStride, dstStride);
        }

        return dst;
    }
}
