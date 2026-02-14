using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Aprillz.MewUI.Rendering.Gdi.Simd;

/// <summary>
/// AVX2 optimized pixel processing operations.
/// Processes 32 pixels at a time using 256-bit vectors.
/// </summary>
internal static class Avx2Processor
{
    /// <summary>
    /// Premultiplies a BGRA buffer (per-pixel alpha) using AVX2 shuffle.
    /// Processes 8 pixels (32 bytes) per iteration.
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

        var zero = Vector256<byte>.Zero;
        var bias128 = Vector256.Create((ushort)128);

        var alphaShuffle = Vector256.Create(
            (byte)3, (byte)3, (byte)3, (byte)3,
            (byte)7, (byte)7, (byte)7, (byte)7,
            (byte)11, (byte)11, (byte)11, (byte)11,
            (byte)15, (byte)15, (byte)15, (byte)15,
            (byte)3, (byte)3, (byte)3, (byte)3,
            (byte)7, (byte)7, (byte)7, (byte)7,
            (byte)11, (byte)11, (byte)11, (byte)11,
            (byte)15, (byte)15, (byte)15, (byte)15);

        var alphaMask255 = Vector256.Create(
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF,
            (byte)0, (byte)0, (byte)0, (byte)0xFF);

        for (; i + 8 <= pixels; i += 8)
        {
            var v = Avx.LoadVector256(srcBgra + i * 4);

            var aRep = Avx2.Shuffle(v, alphaShuffle);
            aRep = Avx2.Or(aRep, alphaMask255);

            var vLo = Avx2.UnpackLow(v, zero).AsUInt16();
            var vHi = Avx2.UnpackHigh(v, zero).AsUInt16();
            var aLo = Avx2.UnpackLow(aRep, zero).AsUInt16();
            var aHi = Avx2.UnpackHigh(aRep, zero).AsUInt16();

            var outLo = Premultiply16(vLo, aLo, bias128);
            var outHi = Premultiply16(vHi, aHi, bias128);

            // NOTE:
            // AVX2 unpack/pack operate independently on each 128-bit lane.
            // vLo/vHi are already arranged as:
            // - lane0: pixels 0-1 (low) and pixels 2-3 (high)
            // - lane1: pixels 4-5 (low) and pixels 6-7 (high)
            // Therefore PackUnsignedSaturate produces correct pixel order:
            //   lane0: p0 p1 p2 p3, lane1: p4 p5 p6 p7
            // Do NOT permute 64-bit lanes here (it would scramble pixels).
            var packed = Avx2.PackUnsignedSaturate(outLo.AsInt16(), outHi.AsInt16());

            Avx.Store(dstBgra + i * 4, packed);
        }

        // Tail with SSE2 if possible
        if (i + 4 <= pixels && Sse2.IsSupported && Ssse3.IsSupported)
        {
            Sse2Processor.PremultiplyBgra(srcBgra + i * 4, dstBgra + i * 4, (pixels - i) * 4);
            return;
        }

        // Scalar tail
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
    /// Processes 32 pixels per iteration (2x faster than SSE2).
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

        var zero = Vector256<byte>.Zero;
        var bias128 = Vector256.Create((ushort)128);
        var bConst = Vector256.Create((ushort)srcB);
        var gConst = Vector256.Create((ushort)srcG);
        var rConst = Vector256.Create((ushort)srcR);

        fixed (byte* pAlpha0 = alphaRow)
        {
            // Process 32 pixels at a time
            for (; i + 32 <= width; i += 32)
            {
                var a = Avx.LoadVector256(pAlpha0 + i);

                // Split into low and high 128-bit halves for unpacking
                // AVX2 unpack works on 128-bit lanes independently
                var aLo128 = a.GetLower();
                var aHi128 = a.GetUpper();

                var zero128 = Vector128<byte>.Zero;

                // Expand to 16-bit (4 vectors of 16 elements each)
                var aLo16_0 = Sse2.UnpackLow(aLo128, zero128).AsUInt16();
                var aLo16_1 = Sse2.UnpackHigh(aLo128, zero128).AsUInt16();
                var aHi16_0 = Sse2.UnpackLow(aHi128, zero128).AsUInt16();
                var aHi16_1 = Sse2.UnpackHigh(aHi128, zero128).AsUInt16();

                // Combine into 256-bit vectors
                var aLoVec = Vector256.Create(aLo16_0, aLo16_1);
                var aHiVec = Vector256.Create(aHi16_0, aHi16_1);

                // Premultiply
                var pb = PackPremultiply256(aLoVec, aHiVec, bConst, bias128);
                var pg = PackPremultiply256(aLoVec, aHiVec, gConst, bias128);
                var pr = PackPremultiply256(aLoVec, aHiVec, rConst, bias128);

                // Interleave into BGRA and store
                StoreBgra32(dstBgra + i * 4, pb, pg, pr, a);
            }
        }

        // Fall back to SSE2 for remaining 16-31 pixels
        if (i + 16 <= width && Sse2.IsSupported)
        {
            var subSpan = alphaRow.Slice(i);
            Sse2Processor.WritePremultipliedBgraRow(dstBgra + i * 4, subSpan.Slice(0, Math.Min(16, width - i)), srcB, srcG, srcR);
            i += 16;
        }

        // Process remaining pixels
        ProcessTail(dstBgra, alphaRow, srcB, srcG, srcR, i, width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<byte> PackPremultiply256(
        Vector256<ushort> aLo,
        Vector256<ushort> aHi,
        Vector256<ushort> c,
        Vector256<ushort> bias128)
    {
        // t = a*c + 128
        var tLo = Avx2.Add(Avx2.MultiplyLow(aLo, c), bias128);
        var tHi = Avx2.Add(Avx2.MultiplyLow(aHi, c), bias128);

        // t = t + (t >> 8)
        tLo = Avx2.Add(tLo, Avx2.ShiftRightLogical(tLo, 8));
        tHi = Avx2.Add(tHi, Avx2.ShiftRightLogical(tHi, 8));

        // t = t >> 8
        tLo = Avx2.ShiftRightLogical(tLo, 8);
        tHi = Avx2.ShiftRightLogical(tHi, 8);

        // Pack 16-bit lanes to bytes
        // Note: AVX2 PackUnsignedSaturate works on 128-bit lanes
        var packed = Avx2.PackUnsignedSaturate(tLo.AsInt16(), tHi.AsInt16());

        // AVX2 pack interleaves lanes, need to permute
        return Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsByte();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ushort> Premultiply16(
        Vector256<ushort> v,
        Vector256<ushort> a,
        Vector256<ushort> bias128)
    {
        var t = Avx2.Add(Avx2.MultiplyLow(v, a), bias128);
        t = Avx2.Add(t, Avx2.ShiftRightLogical(t, 8));
        return Avx2.ShiftRightLogical(t, 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void StoreBgra32(
        byte* dst,
        Vector256<byte> b,
        Vector256<byte> g,
        Vector256<byte> r,
        Vector256<byte> a)
    {
        // Interleave B,G,R,A into BGRA format
        // This is complex due to AVX2's lane behavior

        // Get 128-bit halves
        var bLo = b.GetLower();
        var bHi = b.GetUpper();
        var gLo = g.GetLower();
        var gHi = g.GetUpper();
        var rLo = r.GetLower();
        var rHi = r.GetUpper();
        var aLo = a.GetLower();
        var aHi = a.GetUpper();

        // Interleave BG and RA pairs
        var bgLo0 = Sse2.UnpackLow(bLo, gLo);
        var bgLo1 = Sse2.UnpackHigh(bLo, gLo);
        var bgHi0 = Sse2.UnpackLow(bHi, gHi);
        var bgHi1 = Sse2.UnpackHigh(bHi, gHi);

        var raLo0 = Sse2.UnpackLow(rLo, aLo);
        var raLo1 = Sse2.UnpackHigh(rLo, aLo);
        var raHi0 = Sse2.UnpackLow(rHi, aHi);
        var raHi1 = Sse2.UnpackHigh(rHi, aHi);

        // Final interleave to BGRA
        var bgra0 = Sse2.UnpackLow(bgLo0.AsUInt16(), raLo0.AsUInt16()).AsByte();
        var bgra1 = Sse2.UnpackHigh(bgLo0.AsUInt16(), raLo0.AsUInt16()).AsByte();
        var bgra2 = Sse2.UnpackLow(bgLo1.AsUInt16(), raLo1.AsUInt16()).AsByte();
        var bgra3 = Sse2.UnpackHigh(bgLo1.AsUInt16(), raLo1.AsUInt16()).AsByte();
        var bgra4 = Sse2.UnpackLow(bgHi0.AsUInt16(), raHi0.AsUInt16()).AsByte();
        var bgra5 = Sse2.UnpackHigh(bgHi0.AsUInt16(), raHi0.AsUInt16()).AsByte();
        var bgra6 = Sse2.UnpackLow(bgHi1.AsUInt16(), raHi1.AsUInt16()).AsByte();
        var bgra7 = Sse2.UnpackHigh(bgHi1.AsUInt16(), raHi1.AsUInt16()).AsByte();

        // Store 8 x 16 bytes = 128 bytes = 32 pixels
        Sse2.Store(dst + 0, bgra0);
        Sse2.Store(dst + 16, bgra1);
        Sse2.Store(dst + 32, bgra2);
        Sse2.Store(dst + 48, bgra3);
        Sse2.Store(dst + 64, bgra4);
        Sse2.Store(dst + 80, bgra5);
        Sse2.Store(dst + 96, bgra6);
        Sse2.Store(dst + 112, bgra7);
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

        byte pb = Premultiply8(b, a);
        byte pg = Premultiply8(g, a);
        byte pr = Premultiply8(r, a);

        uint pixel = (uint)(pb | (pg << 8) | (pr << 16) | (a << 24));
        var pixelVec = Vector256.Create(pixel);

        int i = 0;

        // Process 8 pixels (32 bytes) at a time
        for (; i + 8 <= count; i += 8)
        {
            Avx.Store((uint*)(dst + i * 4), pixelVec);
        }

        // Tail with SSE2 or scalar
        if (i + 4 <= count && Sse2.IsSupported)
        {
            var pixelVec128 = Vector128.Create(pixel);
            Sse2.Store((uint*)(dst + i * 4), pixelVec128);
            i += 4;
        }

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

        var zero = Vector256<byte>.Zero;
        int i = 0;

        // Process 32 bytes at a time
        for (; i + 32 <= byteCount; i += 32)
        {
            Avx.Store(dst + i, zero);
        }

        // Tail
        if (i + 16 <= byteCount && Sse2.IsSupported)
        {
            Sse2.Store(dst + i, Vector128<byte>.Zero);
            i += 16;
        }

        for (; i < byteCount; i++)
        {
            dst[i] = 0;
        }
    }
}
