using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using BitMiracle.LibJpeg.Classic;

namespace Aprillz.MewUI.Resources;

internal sealed class JpegDecoder : IImageDecoder, IByteArrayImageDecoder
{
    public string Id => "jpeg";

    public bool CanDecode(ReadOnlySpan<byte> encoded) =>
        encoded.Length >= 3 && encoded[0] == 0xFF && encoded[1] == 0xD8 && encoded[2] == 0xFF;

    public bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap)
    {
        bitmap = default;

        if (!CanDecode(encoded))
        {
            return false;
        }

        // LibJpeg expects a Stream; Span may not be backed by an array, so we must materialize here.
        return TryDecode(encoded.ToArray(), out bitmap);
    }

    public bool TryDecode(byte[] encoded, out DecodedBitmap bitmap)
    {
        bitmap = default;

        if (!CanDecode(encoded))
        {
            return false;
        }

        var cinfo = new jpeg_decompress_struct(new jpeg_error_mgr());
        bool started = false;
        try
        {
            using var ms = new MemoryStream(encoded, 0, encoded.Length, writable: false, publiclyVisible: true);
            cinfo.jpeg_stdio_src(ms);
            cinfo.jpeg_read_header(true);

            // Always request RGB output. This avoids having to support CMYK/YCbCr/etc here.
            cinfo.Out_color_space = J_COLOR_SPACE.JCS_RGB;

            cinfo.jpeg_start_decompress();
            started = true;

            int width = cinfo.Output_width;
            int height = cinfo.Output_height;
            int components = cinfo.Output_components;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            // With JCS_RGB, output should be 3 (R,G,B). Some JPEGs may still yield 1 (grayscale).
            if (components != 3 && components != 1)
            {
                ImageDecoders.DebugLog?.Invoke($"JpegDecoder: Unexpected output components={components}.");
                return false;
            }

            byte[] dst = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
            int dstStride = width * 4;

            int srcStride = width * components;
            const int batch = 8;

            // Use contiguous Buffer2D for better cache locality
            var srcBuffer = jpeg_common_struct.AllocSampleBuffer(srcStride, batch);

            int y = 0;
            while (y < height && cinfo.Output_scanline < cinfo.Output_height)
            {
                int remaining = height - y;
                int want = remaining < batch ? remaining : batch;
                int read = cinfo.jpeg_read_scanlines(srcBuffer, 0, want);
                if (read <= 0)
                {
                    break;
                }

                if (components == 3)
                {
                    for (int r = 0; r < read; r++)
                    {
                        ConvertRgbRowToBgra(srcBuffer.GetRow(r), dst, y + r, width, dstStride);
                    }
                }
                else
                {
                    for (int r = 0; r < read; r++)
                    {
                        ConvertGrayRowToBgra(srcBuffer.GetRow(r), dst, y + r, width, dstStride);
                    }
                }

                y += read;
            }

            cinfo.jpeg_finish_decompress();
            bitmap = new DecodedBitmap(width, height, BitmapPixelFormat.Bgra32, dst);
            return true;
        }
        catch (Exception ex)
        {
            ImageDecoders.DebugLog?.Invoke($"JpegDecoder: decode failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            if (started)
            {
                try { cinfo.jpeg_abort(); } catch { }
            }

            try { cinfo.jpeg_destroy(); } catch { }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConvertRgbRowToBgra(ReadOnlySpan<byte> src, byte[] dst, int y, int width, int dstStride)
    {
        fixed (byte* pSrc = src)
        fixed (byte* pDst0 = dst)
        {
            byte* pDst = pDst0 + (nint)(y * dstStride);
            byte* s = pSrc;
            int x = 0;

            // SSSE3 path: process 16 pixels at a time (48 bytes in, 64 bytes out)
            if (Ssse3.IsSupported && width >= 16)
            {
                // Shuffle mask for RGB->BGRA: reorder bytes and insert 0xFF for alpha
                // Input:  R0 G0 B0 R1 G1 B1 R2 G2 B2 R3 G3 B3 R4 G4 B4 R5 G5 B5 (18 bytes from 2 loads)
                // Output: B0 G0 R0 FF B1 G1 R1 FF B2 G2 R2 FF B3 G3 R3 FF (16 bytes)
                var shuffleMask = Vector128.Create(
                    (byte)2, 1, 0, 0x80,   // pixel 0: B G R (0x80 = zero, will be OR'd with alpha)
                    5, 4, 3, 0x80,         // pixel 1
                    8, 7, 6, 0x80,         // pixel 2
                    11, 10, 9, 0x80        // pixel 3
                );
                var alphaMask = Vector128.Create(0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF,
                                                  0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF).AsByte();

                int simdWidth = width - 3; // Leave room for last partial iteration
                while (x < simdWidth)
                {
                    // Load 16 bytes (covers 5+ pixels worth of RGB data)
                    var rgb0 = Sse2.LoadVector128(s);        // bytes 0-15
                    var rgb1 = Sse2.LoadVector128(s + 12);   // bytes 12-27 (overlapping load for next 4 pixels)

                    // Shuffle RGB to BGR and prepare for alpha
                    var bgra0 = Ssse3.Shuffle(rgb0, shuffleMask);
                    var bgra1 = Ssse3.Shuffle(rgb1, shuffleMask);

                    // OR with alpha mask to set alpha to 0xFF
                    bgra0 = Sse2.Or(bgra0, alphaMask);
                    bgra1 = Sse2.Or(bgra1, alphaMask);

                    // Store results
                    Sse2.Store(pDst, bgra0);
                    Sse2.Store(pDst + 16, bgra1);

                    s += 24;      // 8 pixels * 3 bytes
                    pDst += 32;   // 8 pixels * 4 bytes
                    x += 8;
                }
            }

            // Scalar fallback for remaining pixels
            for (; x < width; x++)
            {
                pDst[0] = s[2]; // B
                pDst[1] = s[1]; // G
                pDst[2] = s[0]; // R
                pDst[3] = 0xFF;
                pDst += 4;
                s += 3;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ConvertGrayRowToBgra(ReadOnlySpan<byte> src, byte[] dst, int y, int width, int dstStride)
    {
        fixed (byte* pSrc = src)
        fixed (byte* pDst0 = dst)
        {
            byte* pDst = pDst0 + (nint)(y * dstStride);
            byte* s = pSrc;
            int x = 0;

            // SSE2 path: process 16 pixels at a time
            if (Sse2.IsSupported && width >= 16)
            {
                var alphaMask = Vector128.Create(0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF,
                                                  0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF).AsByte();

                int simdWidth = width & ~15; // Round down to 16
                while (x < simdWidth)
                {
                    // Load 16 grayscale bytes
                    var gray = Sse2.LoadVector128(s);

                    // Unpack to 16-bit: low 8 pixels
                    var grayLo = Sse2.UnpackLow(gray, gray);   // G0 G0 G1 G1 G2 G2 G3 G3 G4 G4 G5 G5 G6 G6 G7 G7
                    var grayHi = Sse2.UnpackHigh(gray, gray);  // G8 G8 G9 G9 ...

                    // Unpack to 32-bit (BGRA)
                    var bgra0 = Sse2.UnpackLow(grayLo, grayLo);   // G0 G0 G0 G0 G1 G1 G1 G1 G2 G2 G2 G2 G3 G3 G3 G3
                    var bgra1 = Sse2.UnpackHigh(grayLo, grayLo);  // G4 G4 G4 G4 ...
                    var bgra2 = Sse2.UnpackLow(grayHi, grayHi);   // G8 G8 G8 G8 ...
                    var bgra3 = Sse2.UnpackHigh(grayHi, grayHi);  // G12 G12 G12 G12 ...

                    // Set alpha to 0xFF
                    bgra0 = Sse2.Or(bgra0, alphaMask);
                    bgra1 = Sse2.Or(bgra1, alphaMask);
                    bgra2 = Sse2.Or(bgra2, alphaMask);
                    bgra3 = Sse2.Or(bgra3, alphaMask);

                    // Store 64 bytes (16 pixels)
                    Sse2.Store(pDst, bgra0);
                    Sse2.Store(pDst + 16, bgra1);
                    Sse2.Store(pDst + 32, bgra2);
                    Sse2.Store(pDst + 48, bgra3);

                    s += 16;
                    pDst += 64;
                    x += 16;
                }
            }

            // Scalar fallback for remaining pixels
            for (; x < width; x++)
            {
                byte v = *s++;
                pDst[0] = v;
                pDst[1] = v;
                pDst[2] = v;
                pDst[3] = 0xFF;
                pDst += 4;
            }
        }
    }
}
