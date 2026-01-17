using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI bitmap image implementation.
/// </summary>
internal sealed class GdiImage : IImage
{
    private nint _bits;
    private bool _disposed;
    private readonly Dictionary<ScaledKey, nint> _scaled = new();
    private readonly IPixelBufferSource? _source;
    private int _sourceVersion = -1;

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

            CopyToDibPremultiplied(l.Buffer);
            _sourceVersion = v;

            foreach (var kvp in _scaled)
            {
                Gdi32.DeleteObject(kvp.Value);
            }

            _scaled.Clear();
        }
    }

    public void Dispose()
    {
        if (!_disposed && Handle != 0)
        {
            foreach (var kvp in _scaled)
            {
                Gdi32.DeleteObject(kvp.Value);
            }

            _scaled.Clear();

            Gdi32.DeleteObject(Handle);
            Handle = 0;
            _bits = 0;
            _disposed = true;
        }
    }

    private readonly record struct ScaledKey(int SrcX, int SrcY, int SrcW, int SrcH, int DestW, int DestH);

    public bool TryGetOrCreateScaledBitmap(int srcX, int srcY, int srcW, int srcH, int destW, int destH, out nint scaledBitmap)
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

        var key = new ScaledKey(srcX, srcY, srcW, srcH, destW, destH);
        if (_scaled.TryGetValue(key, out var existing) && existing != 0)
        {
            scaledBitmap = existing;
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
                else
                {
                    // Fallback: bilinear sampling (premultiplied BGRA).
                    float scaleX = (float)srcW / destW;
                    float scaleY = (float)srcH / destH;

                    for (int dy = 0; dy < destH; dy++)
                    {
                        float sy = (dy + 0.5f) * scaleY - 0.5f;
                        int y0 = (int)MathF.Floor(sy);
                        float wy = sy - y0;
                        if (y0 < 0) { y0 = 0; wy = 0; }
                        int y1 = Math.Min(srcH - 1, y0 + 1);

                        byte* dstRow = dstBase + dy * destW * 4;
                        byte* row0 = srcBase + y0 * PixelWidth * 4;
                        byte* row1 = srcBase + y1 * PixelWidth * 4;

                        for (int dx = 0; dx < destW; dx++)
                        {
                            float sx = (dx + 0.5f) * scaleX - 0.5f;
                            int x0 = (int)MathF.Floor(sx);
                            float wx = sx - x0;
                            if (x0 < 0) { x0 = 0; wx = 0; }
                            int x1 = Math.Min(srcW - 1, x0 + 1);

                            byte* p00 = row0 + x0 * 4;
                            byte* p10 = row0 + x1 * 4;
                            byte* p01 = row1 + x0 * 4;
                            byte* p11 = row1 + x1 * 4;

                            float w00 = (1 - wx) * (1 - wy);
                            float w10 = wx * (1 - wy);
                            float w01 = (1 - wx) * wy;
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
                    }
                }
            }

            _scaled[key] = dstBitmap;
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

    private void CopyToDibPremultiplied(ReadOnlySpan<byte> pixelData)
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
        bool needsPremultiply = false;
        for (int i = 3; i < pixelData.Length; i += 4)
        {
            if (pixelData[i] != 0xFF)
            {
                needsPremultiply = true;
                break;
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

                byte* dst = (byte*)_bits;
                int count = pixelData.Length;

                for (int i = 0; i < count; i += 4)
                {
                    byte b = src[i + 0];
                    byte g = src[i + 1];
                    byte r = src[i + 2];
                    byte a = src[i + 3];

                    uint alpha = a;
                    dst[i + 3] = a;
                    dst[i + 0] = (byte)((b * alpha + 127) / 255);
                    dst[i + 1] = (byte)((g * alpha + 127) / 255);
                    dst[i + 2] = (byte)((r * alpha + 127) / 255);
                }
            }
        }
    }
}
