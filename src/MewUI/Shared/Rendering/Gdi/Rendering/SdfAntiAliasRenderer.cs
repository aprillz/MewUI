using System.Buffers;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Rendering.Gdi.Core;
using Aprillz.MewUI.Rendering.Gdi.Sdf;
using Aprillz.MewUI.Rendering.Gdi.Simd;

namespace Aprillz.MewUI.Rendering.Gdi.Rendering;

/// <summary>
/// SDF-based anti-aliased shape renderer.
/// Uses signed distance fields for fast inside/outside determination
/// and falls back to SSAA for complex edge regions.
/// </summary>
internal sealed class SdfAntiAliasRenderer
{
    private readonly AaSurfacePool _surfacePool;
    private readonly int _supersampleFactor;

    /// <summary>
    /// Creates an SDF-based anti-alias renderer.
    /// </summary>
    /// <param name="surfacePool">Pool for AA surfaces.</param>
    /// <param name="supersampleFactor">Factor for edge supersampling (1-3).</param>
    public SdfAntiAliasRenderer(AaSurfacePool surfacePool, int supersampleFactor)
    {
        _surfacePool = surfacePool;
        _supersampleFactor = Math.Max(1, Math.Min(3, supersampleFactor));
    }

    /// <summary>
    /// Renders a filled shape using SDF-based anti-aliasing.
    /// </summary>
    [SkipLocalsInit]
    public unsafe void RenderFilledShape(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        ISdfCalculator sdf,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA,
        float aaWidth = 1.0f)
    {
        if (width <= 0 || height <= 0 || srcA == 0)
        {
            return;
        }

        var surface = _surfacePool.Rent(targetDc, width, height);
        if (!surface.IsValid)
        {
            return;
        }

        try
        {
            surface.Clear();

            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);
            int stride = surface.Stride;
            byte* basePtr = (byte*)surface.Bits;

            byte[]? rented = null;
            Span<byte> alphaRow = width <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[width]
                : (rented = ArrayPool<byte>.Shared.Rent(width)).AsSpan(0, width);

            try
            {
                Span<uint> premulTable = stackalloc uint[256];
                GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

                for (int py = 0; py < height; py++)
                {
                    RenderFilledRow(alphaRow, py, width, sdf, sampler, srcA, aaWidth);

                    byte* rowPtr = basePtr + py * stride;
                    GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, premulTable);
                }
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

            surface.AlphaBlendTo(targetDc, destX, destY);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    /// <summary>
    /// Renders a stroked shape using SDF-based anti-aliasing.
    /// </summary>
    [SkipLocalsInit]
    public unsafe void RenderStrokedShape(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        ISdfCalculator outerSdf,
        ISdfCalculator? innerSdf,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA,
        float aaWidth = 1.0f)
    {
        if (width <= 0 || height <= 0 || srcA == 0)
        {
            return;
        }

        var surface = _surfacePool.Rent(targetDc, width, height);
        if (!surface.IsValid)
        {
            return;
        }

        try
        {
            surface.Clear();

            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);
            int stride = surface.Stride;
            byte* basePtr = (byte*)surface.Bits;

            byte[]? rented = null;
            Span<byte> alphaRow = width <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[width]
                : (rented = ArrayPool<byte>.Shared.Rent(width)).AsSpan(0, width);

            try
            {
                Span<uint> premulTable = stackalloc uint[256];
                GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

                for (int py = 0; py < height; py++)
                {
                    RenderStrokeRow(alphaRow, py, width, outerSdf, innerSdf, sampler, srcA, aaWidth);

                    byte* rowPtr = basePtr + py * stride;
                    GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, premulTable);
                }
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

            surface.AlphaBlendTo(targetDc, destX, destY);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    /// <summary>
    /// Renders a single row for a filled shape.
    /// Uses SDF for fast inside/outside detection and SSAA for edges.
    /// </summary>
    private void RenderFilledRow(
        Span<byte> alphaRow,
        int py,
        int width,
        ISdfCalculator sdf,
        SupersampleEdgeSampler sampler,
        byte srcA,
        float aaWidth)
    {
        alphaRow.Clear();

        for (int px = 0; px < width; px++)
        {
            // Sample at pixel center
            float x = px + 0.5f;
            float y = py + 0.5f;

            float dist = sdf.GetSignedDistance(x, y);

            // Fast path: clearly inside
            if (dist <= -aaWidth)
            {
                alphaRow[px] = srcA;
                continue;
            }

            // Fast path: clearly outside
            if (dist >= aaWidth)
            {
                alphaRow[px] = 0;
                continue;
            }

            // Edge region: use supersampling for accuracy
            alphaRow[px] = sampler.SampleEdge(px, py, sdf, aaWidth);
        }
    }

    /// <summary>
    /// Renders a single row for a stroked shape.
    /// </summary>
    private void RenderStrokeRow(
        Span<byte> alphaRow,
        int py,
        int width,
        ISdfCalculator outerSdf,
        ISdfCalculator? innerSdf,
        SupersampleEdgeSampler sampler,
        byte srcA,
        float aaWidth)
    {
        alphaRow.Clear();

        for (int px = 0; px < width; px++)
        {
            float x = px + 0.5f;
            float y = py + 0.5f;

            float outerDist = outerSdf.GetSignedDistance(x, y);

            // Fast path: outside outer shape
            if (outerDist >= aaWidth)
            {
                alphaRow[px] = 0;
                continue;
            }

            if (innerSdf != null)
            {
                float innerDist = innerSdf.GetSignedDistance(x, y);

                // Fast path: inside inner shape (hollow)
                if (innerDist <= -aaWidth)
                {
                    alphaRow[px] = 0;
                    continue;
                }

                // Fast path: clearly in stroke area
                if (outerDist <= -aaWidth && innerDist >= aaWidth)
                {
                    alphaRow[px] = srcA;
                    continue;
                }
            }
            else
            {
                // No inner shape: filled area
                if (outerDist <= -aaWidth)
                {
                    alphaRow[px] = srcA;
                    continue;
                }
            }

            // Edge region: use supersampling
            alphaRow[px] = sampler.SampleStrokeEdge(px, py, outerSdf, innerSdf);
        }
    }

    /// <summary>
    /// Optimized fill for rows that are entirely inside the shape.
    /// </summary>
    public static void FillSolidRow(Span<byte> alphaRow, byte srcA)
    {
        alphaRow.Fill(srcA);
    }

    /// <summary>
    /// Optimized fill for a range of pixels within a row.
    /// </summary>
    public static void FillSolidRange(Span<byte> alphaRow, int start, int end, byte srcA)
    {
        if (start < 0) start = 0;
        if (end > alphaRow.Length) end = alphaRow.Length;
        if (start >= end) return;

        alphaRow.Slice(start, end - start).Fill(srcA);
    }
}
