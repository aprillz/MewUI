using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Aprillz.MewUI.Rendering.Gdi.Simd;

/// <summary>
/// SSE2 optimized pixel processing operations.
/// Processes 16 pixels at a time using 128-bit vectors.
/// </summary>
internal static class Sse2Processor
{
    /// <summary>
    /// Premultiplies a BGRA buffer (per-pixel alpha) using SSSE3 shuffle.
    /// Processes 4 pixels (16 bytes) per iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void PremultiplyBgra(byte* srcBgra, byte* dstBgra, int byteCount)
    {
        if (srcBgra == null || dstBgra == null || byteCount <= 0)
        {
            return;
        }

        int pixels = byteCount >> 2;
        int i = 0;

        var zero = Vector128<byte>.Zero;
        var bias128 = Vector128.Create((ushort)128);

        var alphaShuffle = Vector128.Create(
            (byte)3, (byte)3, (byte)3, (byte)3,
            (byte)7, (byte)7, (byte)7, (byte)7,
            (byte)11, (byte)11, (byte)11, (byte)11,
            (byte)15, (byte)15, (byte)15, (byte)15);

        var alphaMask255 = Vector128.Create(
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF);

        for (; i + 4 <= pixels; i += 4)
        {
            var v = Sse2.LoadVector128(srcBgra + i * 4);

            var aRep = Ssse3.Shuffle(v, alphaShuffle);
            aRep = Sse2.Or(aRep, alphaMask255);

            var vLo = Sse2.UnpackLow(v, zero).AsUInt16();
            var vHi = Sse2.UnpackHigh(v, zero).AsUInt16();
            var aLo = Sse2.UnpackLow(aRep, zero).AsUInt16();
            var aHi = Sse2.UnpackHigh(aRep, zero).AsUInt16();

            var outLo = Premultiply16(vLo, aLo, bias128);
            var outHi = Premultiply16(vHi, aHi, bias128);

            var packed = Sse2.PackUnsignedSaturate(outLo.AsInt16(), outHi.AsInt16());
            Sse2.Store(dstBgra + i * 4, packed);
        }

        // Tail
        byte* dst = dstBgra + i * 4;
        byte* src = srcBgra + i * 4;
        for (int p = i; p < pixels; p++)
        {
            byte b = src[0];
            byte g = src[1];
            byte r = src[2];
            byte a = src[3];

            int t = b * a + 128;
            t += t >> 8;
            dst[0] = (byte)(t >> 8);

            t = g * a + 128;
            t += t >> 8;
            dst[1] = (byte)(t >> 8);

            t = r * a + 128;
            t += t >> 8;
            dst[2] = (byte)(t >> 8);

            dst[3] = a;

            src += 4;
            dst += 4;
        }
    }

    /// <summary>
    /// Writes a row of premultiplied BGRA pixels from alpha values.
    /// Processes 16 pixels per iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void WritePremultipliedBgraRow(
        byte* dstBgra,
        ReadOnlySpan<byte> alphaRow,
        byte srcB,
        byte srcG,
        byte srcR)
    {
        if (dstBgra == null || alphaRow.Length == 0)
        {
            return;
        }

        int width = alphaRow.Length;
        int i = 0;

        var zero = Vector128<byte>.Zero;
        var bias128 = Vector128.Create((ushort)128);
        var bConst = Vector128.Create((ushort)srcB);
        var gConst = Vector128.Create((ushort)srcG);
        var rConst = Vector128.Create((ushort)srcR);

        fixed (byte* pAlpha0 = alphaRow)
        {
            // Process 16 pixels at a time
            for (; i + 16 <= width; i += 16)
            {
                var a = Sse2.LoadVector128(pAlpha0 + i);

                // Expand alpha bytes to 16-bit lanes (low/high 8)
                var aLo = Sse2.UnpackLow(a, zero).AsUInt16();
                var aHi = Sse2.UnpackHigh(a, zero).AsUInt16();

                var pb = PackPremultiply(aLo, aHi, bConst, bias128);
                var pg = PackPremultiply(aLo, aHi, gConst, bias128);
                var pr = PackPremultiply(aLo, aHi, rConst, bias128);

                // Interleave into BGRA
                var bgLo = Sse2.UnpackLow(pb, pg);
                var bgHi = Sse2.UnpackHigh(pb, pg);
                var raLo = Sse2.UnpackLow(pr, a);
                var raHi = Sse2.UnpackHigh(pr, a);

                var bgLoW = bgLo.AsUInt16();
                var bgHiW = bgHi.AsUInt16();
                var raLoW = raLo.AsUInt16();
                var raHiW = raHi.AsUInt16();

                // 16 pixels -> 64 bytes
                StoreBgra8(dstBgra + i * 4, bgLoW, raLoW);
                StoreBgra8(dstBgra + (i + 8) * 4, bgHiW, raHiW);
            }
        }

        // Process remaining pixels
        ProcessTail(dstBgra, alphaRow, srcB, srcG, srcR, i, width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> PackPremultiply(
        Vector128<ushort> aLo,
        Vector128<ushort> aHi,
        Vector128<ushort> c,
        Vector128<ushort> bias128)
    {
        // t = a*c + 128
        var tLo = Sse2.Add(Sse2.MultiplyLow(aLo, c), bias128);
        var tHi = Sse2.Add(Sse2.MultiplyLow(aHi, c), bias128);

        // t = t + (t >> 8)
        tLo = Sse2.Add(tLo, Sse2.ShiftRightLogical(tLo, 8));
        tHi = Sse2.Add(tHi, Sse2.ShiftRightLogical(tHi, 8));

        // t = t >> 8
        tLo = Sse2.ShiftRightLogical(tLo, 8);
        tHi = Sse2.ShiftRightLogical(tHi, 8);

        // Pack 16-bit lanes to bytes
        return Sse2.PackUnsignedSaturate(tLo.AsInt16(), tHi.AsInt16());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> Premultiply16(
        Vector128<ushort> v,
        Vector128<ushort> a,
        Vector128<ushort> bias128)
    {
        var t = Sse2.Add(Sse2.MultiplyLow(v, a), bias128);
        t = Sse2.Add(t, Sse2.ShiftRightLogical(t, 8));
        return Sse2.ShiftRightLogical(t, 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void StoreBgra8(byte* dst, Vector128<ushort> bg, Vector128<ushort> ra)
    {
        // bg/ra contain 8 16-bit words each. Interleave words => BGRA bytes for 4 pixels in 16 bytes.
        var lo = Sse2.UnpackLow(bg, ra).AsByte();
        var hi = Sse2.UnpackHigh(bg, ra).AsByte();

        Sse2.Store(dst, lo);
        Sse2.Store(dst + 16, hi);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ProcessTail(
        byte* dstBgra,
        ReadOnlySpan<byte> alphaRow,
        byte srcB,
        byte srcG,
        byte srcR,
        int start,
        int end)
    {
        byte* p = dstBgra + start * 4;
        for (int i = start; i < end; i++)
        {
            byte a = alphaRow[i];
            if (a == 0)
            {
                p[0] = 0;
                p[1] = 0;
                p[2] = 0;
                p[3] = 0;
            }
            else
            {
                p[0] = Premultiply8(srcB, a);
                p[1] = Premultiply8(srcG, a);
                p[2] = Premultiply8(srcR, a);
                p[3] = a;
            }
            p += 4;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Premultiply8(byte c, byte a)
    {
        // Exact 8-bit premultiply with rounding:
        // (c*a + 128 + ((c*a + 128) >> 8)) >> 8
        int t = c * a + 128;
        t += t >> 8;
        return (byte)(t >> 8);
    }

    /// <summary>
    /// Fills a row of BGRA pixels with a solid premultiplied color.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FillBgraRow(byte* dst, int count, byte b, byte g, byte r, byte a)
    {
        if (dst == null || count <= 0)
        {
            return;
        }

        // Create premultiplied BGRA value
        byte pb = Premultiply8(b, a);
        byte pg = Premultiply8(g, a);
        byte pr = Premultiply8(r, a);

        uint pixel = (uint)(pb | (pg << 8) | (pr << 16) | (a << 24));
        var pixelVec = Vector128.Create(pixel);

        int i = 0;

        // Process 4 pixels (16 bytes) at a time
        for (; i + 4 <= count; i += 4)
        {
            Sse2.Store((uint*)(dst + i * 4), pixelVec);
        }

        // Tail
        uint* p = (uint*)(dst + i * 4);
        for (; i < count; i++)
        {
            *p++ = pixel;
        }
    }

    /// <summary>
    /// Clears a row of pixels to zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ClearRow(byte* dst, int byteCount)
    {
        if (dst == null || byteCount <= 0)
        {
            return;
        }

        var zero = Vector128<byte>.Zero;
        int i = 0;

        // Process 16 bytes at a time
        for (; i + 16 <= byteCount; i += 16)
        {
            Sse2.Store(dst + i, zero);
        }

        // Tail
        for (; i < byteCount; i++)
        {
            dst[i] = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Downsample2xBoxPremultipliedBgra(
        byte* srcBgra,
        int srcStrideBytes,
        int srcWidth,
        int srcHeight,
        byte* dstBgra,
        int dstStrideBytes,
        int dstWidth,
        int dstHeight)
    {
        if (srcBgra == null || dstBgra == null || srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
        {
            return;
        }

        // Mask to keep only the first 4 ushort lanes (BGRA) after pair summing.
        var keepFirst4 = Vector128.Create(
            (ushort)0xFFFF, (ushort)0xFFFF, (ushort)0xFFFF, (ushort)0xFFFF,
            (ushort)0, (ushort)0, (ushort)0, (ushort)0);

        var zeroBytes = Vector128<byte>.Zero;
        var zeroShorts = Vector128<short>.Zero;

        for (int y = 0; y < dstHeight; y++)
        {
            int sy0 = y * 2;
            int sy1 = sy0 + 1;
            if (sy1 >= srcHeight)
            {
                sy1 = sy0; // duplicate last row
            }

            byte* row0 = srcBgra + sy0 * srcStrideBytes;
            byte* row1 = srcBgra + sy1 * srcStrideBytes;
            byte* dstRow = dstBgra + y * dstStrideBytes;

            int x = 0;
            // Each iteration produces 2 destination pixels from 4 source pixels per row (16 bytes).
            int maxVecDst = dstWidth - 2;

            for (; x <= maxVecDst; x += 2)
            {
                int sx0 = x * 2;
                int srcOffset = sx0 * 4;

                if (sx0 + 3 >= srcWidth)
                {
                    break; // handle tail/edge with scalar
                }

                var top = Sse2.LoadVector128(row0 + srcOffset);
                var bottom = Sse2.LoadVector128(row1 + srcOffset);

                // Low half: pixels 0 and 1, High half: pixels 2 and 3
                var topLo = Sse2.UnpackLow(top, zeroBytes).AsUInt16();
                var topHi = Sse2.UnpackHigh(top, zeroBytes).AsUInt16();
                var botLo = Sse2.UnpackLow(bottom, zeroBytes).AsUInt16();
                var botHi = Sse2.UnpackHigh(bottom, zeroBytes).AsUInt16();

                // Horizontal pair sums (pixel0+pixel1, pixel2+pixel3) for each row.
                var topSum0 = PairSumFirstPixel(topLo, keepFirst4);
                var topSum1 = PairSumFirstPixel(topHi, keepFirst4);
                var botSum0 = PairSumFirstPixel(botLo, keepFirst4);
                var botSum1 = PairSumFirstPixel(botHi, keepFirst4);

                // Vertical sum then average (/4).
                var sum0 = Sse2.Add(topSum0, botSum0);
                var sum1 = Sse2.Add(topSum1, botSum1);

                sum0 = Sse2.ShiftRightLogical(sum0, 2);
                sum1 = Sse2.ShiftRightLogical(sum1, 2);

                // Pack to BGRA bytes (first 4 bytes contain the pixel).
                var packed0 = Sse2.PackUnsignedSaturate(sum0.AsInt16(), zeroShorts).AsUInt32();
                var packed1 = Sse2.PackUnsignedSaturate(sum1.AsInt16(), zeroShorts).AsUInt32();

                ((uint*)dstRow)[x + 0] = packed0.GetElement(0);
                ((uint*)dstRow)[x + 1] = packed1.GetElement(0);
            }

            // Scalar tail for remaining pixels and right-edge duplication.
            for (; x < dstWidth; x++)
            {
                int sx0 = x * 2;
                int sx1 = Math.Min(srcWidth - 1, sx0 + 1);

                byte* p00 = row0 + sx0 * 4;
                byte* p10 = row0 + sx1 * 4;
                byte* p01 = row1 + sx0 * 4;
                byte* p11 = row1 + sx1 * 4;

                int b = p00[0] + p10[0] + p01[0] + p11[0];
                int g = p00[1] + p10[1] + p01[1] + p11[1];
                int r = p00[2] + p10[2] + p01[2] + p11[2];
                int a = p00[3] + p10[3] + p01[3] + p11[3];

                dstRow[x * 4 + 0] = (byte)((b + 2) >> 2);
                dstRow[x * 4 + 1] = (byte)((g + 2) >> 2);
                dstRow[x * 4 + 2] = (byte)((r + 2) >> 2);
                dstRow[x * 4 + 3] = (byte)((a + 2) >> 2);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> PairSumFirstPixel(Vector128<ushort> twoPixelsBytesAsU16, Vector128<ushort> keepFirst4)
    {
        // Input lanes represent: b0,g0,r0,a0,b1,g1,r1,a1 (as ushort).
        // Add shifted-by-8-bytes (pixel1) into pixel0, and keep only the first 4 lanes.
        var shifted = Sse2.ShiftRightLogical128BitLane(twoPixelsBytesAsU16.AsByte(), 8).AsUInt16();
        var sum = Sse2.Add(twoPixelsBytesAsU16, shifted);
        return Sse2.And(sum, keepFirst4);
    }
}
