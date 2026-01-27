using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// Represents an off-screen DIB section used for anti-aliased rendering.
/// Wraps the memory DC, bitmap, and pixel buffer for easy management.
/// </summary>
internal sealed class AaSurface : IDisposable
{
    private nint _bitmap;
    private nint _oldBitmap;
    private nint _bits;
    private bool _disposed;

    /// <summary>
    /// Gets the memory device context handle.
    /// </summary>
    public nint MemDc { get; private set; }

    /// <summary>
    /// Gets the pointer to the raw pixel data (32-bit BGRA, top-down).
    /// </summary>
    public nint Bits => _bits;

    /// <summary>
    /// Gets the width of the surface in pixels.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets the height of the surface in pixels.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets the stride (bytes per row) of the surface.
    /// </summary>
    public int Stride => Width * 4;

    /// <summary>
    /// Gets a value indicating whether this surface is valid and usable.
    /// </summary>
    public bool IsValid => MemDc != 0 && _bitmap != 0 && _bits != 0;

    /// <summary>
    /// Creates a new AaSurface with the specified dimensions.
    /// </summary>
    /// <param name="sourceDc">Source DC for creating compatible resources.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public AaSurface(nint sourceDc, int width, int height)
    {
        Create(sourceDc, width, height);
    }

    /// <summary>
    /// Creates an uninitialized surface (for pooling).
    /// </summary>
    internal AaSurface()
    {
    }

    private void Create(nint sourceDc, int width, int height)
    {
        Width = Math.Max(1, Math.Min(width, GdiRenderingConstants.MaxAaSurfaceSize));
        Height = Math.Max(1, Math.Min(height, GdiRenderingConstants.MaxAaSurfaceSize));

        MemDc = Gdi32.CreateCompatibleDC(sourceDc);
        if (MemDc == 0)
        {
            return;
        }

        var bmi = BITMAPINFO.Create32bpp(Width, Height);
        _bitmap = Gdi32.CreateDIBSection(sourceDc, ref bmi, 0, out _bits, 0, 0);

        if (_bitmap == 0 || _bits == 0)
        {
            Gdi32.DeleteDC(MemDc);
            MemDc = 0;
            return;
        }

        _oldBitmap = Gdi32.SelectObject(MemDc, _bitmap);
    }

    /// <summary>
    /// Resizes the surface if the requested size is different.
    /// </summary>
    /// <param name="sourceDc">Source DC for creating compatible resources.</param>
    /// <param name="width">New width in pixels.</param>
    /// <param name="height">New height in pixels.</param>
    /// <returns>True if resize was successful or not needed.</returns>
    public bool EnsureSize(nint sourceDc, int width, int height)
    {
        width = Math.Max(1, Math.Min(width, GdiRenderingConstants.MaxAaSurfaceSize));
        height = Math.Max(1, Math.Min(height, GdiRenderingConstants.MaxAaSurfaceSize));

        if (Width == width && Height == height && IsValid)
        {
            return true;
        }

        Release();
        Create(sourceDc, width, height);
        return IsValid;
    }

    /// <summary>
    /// Clears the pixel buffer to zero (transparent).
    /// </summary>
    public unsafe void Clear()
    {
        if (_bits == 0 || Width <= 0 || Height <= 0)
        {
            return;
        }

        var count = Width * Height * 4;
        new Span<byte>((void*)_bits, count).Clear();
    }

    /// <summary>
    /// Clears a specific region of the pixel buffer to zero.
    /// </summary>
    public unsafe void ClearRegion(int x, int y, int width, int height)
    {
        if (_bits == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        // Clamp to surface bounds
        if (x < 0) { width += x; x = 0; }
        if (y < 0) { height += y; y = 0; }
        if (x + width > Width) width = Width - x;
        if (y + height > Height) height = Height - y;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        int stride = Stride;
        byte* basePtr = (byte*)_bits;

        for (int py = 0; py < height; py++)
        {
            byte* row = basePtr + (y + py) * stride + x * 4;
            new Span<byte>(row, width * 4).Clear();
        }
    }

    /// <summary>
    /// Alpha blends this surface onto the target DC.
    /// </summary>
    public void AlphaBlendTo(nint targetDc, int destX, int destY)
    {
        AlphaBlendTo(targetDc, destX, destY, Width, Height, 0, 0);
    }

    /// <summary>
    /// Alpha blends a region of this surface onto the target DC.
    /// </summary>
    public void AlphaBlendTo(nint targetDc, int destX, int destY, int width, int height, int srcX, int srcY)
    {
        if (!IsValid || targetDc == 0)
        {
            return;
        }

        var blend = BLENDFUNCTION.SourceOver(255);
        Gdi32.AlphaBlend(targetDc, destX, destY, width, height, MemDc, srcX, srcY, width, height, blend);
    }

    /// <summary>
    /// Alpha blends and stretches a region of this surface onto the target DC.
    /// </summary>
    public void AlphaBlendToStretch(nint targetDc, int destX, int destY, int destWidth, int destHeight)
    {
        AlphaBlendToStretch(targetDc, destX, destY, destWidth, destHeight, 0, 0, Width, Height);
    }

    /// <summary>
    /// Alpha blends and stretches a region of this surface onto the target DC.
    /// </summary>
    public void AlphaBlendToStretch(
        nint targetDc,
        int destX,
        int destY,
        int destWidth,
        int destHeight,
        int srcX,
        int srcY,
        int srcWidth,
        int srcHeight)
    {
        if (!IsValid || targetDc == 0)
        {
            return;
        }

        if (destWidth <= 0 || destHeight <= 0 || srcWidth <= 0 || srcHeight <= 0)
        {
            return;
        }

        var blend = BLENDFUNCTION.SourceOver(255);
        Gdi32.AlphaBlend(targetDc, destX, destY, destWidth, destHeight, MemDc, srcX, srcY, srcWidth, srcHeight, blend);
    }

    /// <summary>
    /// Gets a span over the pixel data for direct manipulation.
    /// </summary>
    public unsafe Span<byte> GetPixelSpan()
    {
        if (_bits == 0)
        {
            return Span<byte>.Empty;
        }

        return new Span<byte>((void*)_bits, Width * Height * 4);
    }

    /// <summary>
    /// Gets a span over a single row of pixel data.
    /// </summary>
    public unsafe Span<byte> GetRowSpan(int y)
    {
        if (_bits == 0 || y < 0 || y >= Height)
        {
            return Span<byte>.Empty;
        }

        byte* rowPtr = (byte*)_bits + y * Stride;
        return new Span<byte>(rowPtr, Width * 4);
    }

    /// <summary>
    /// Gets a pointer to the start of a row.
    /// </summary>
    public unsafe byte* GetRowPointer(int y)
    {
        if (_bits == 0 || y < 0 || y >= Height)
        {
            return null;
        }

        return (byte*)_bits + y * Stride;
    }

    private void Release()
    {
        if (MemDc != 0)
        {
            if (_oldBitmap != 0)
            {
                Gdi32.SelectObject(MemDc, _oldBitmap);
                _oldBitmap = 0;
            }

            if (_bitmap != 0)
            {
                Gdi32.DeleteObject(_bitmap);
                _bitmap = 0;
            }

            Gdi32.DeleteDC(MemDc);
            MemDc = 0;
        }

        _bits = 0;
        Width = 0;
        Height = 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Release();
            _disposed = true;
        }
    }
}
