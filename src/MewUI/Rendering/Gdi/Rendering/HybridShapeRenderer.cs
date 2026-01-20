using System.Buffers;

using Aprillz.MewUI.Rendering.Gdi.Core;
using Aprillz.MewUI.Rendering.Gdi.Sdf;
using Aprillz.MewUI.Rendering.Gdi.Simd;

namespace Aprillz.MewUI.Rendering.Gdi.Rendering;

/// <summary>
/// Hybrid shape renderer that combines SDF-based fast paths with SSAA for edges.
/// Optimized for common shapes like rounded rectangles and ellipses.
/// </summary>
internal sealed class HybridShapeRenderer
{
    private readonly AaSurfacePool _surfacePool;
    private readonly int _supersampleFactor;

    public HybridShapeRenderer(AaSurfacePool surfacePool, int supersampleFactor)
    {
        _surfacePool = surfacePool;
        _supersampleFactor = Math.Max(1, Math.Min(3, supersampleFactor));
    }

    #region Rounded Rectangle

    /// <summary>
    /// Renders a filled rounded rectangle with anti-aliasing.
    /// Uses span-based optimization for rows.
    /// </summary>
    public unsafe void FillRoundedRectangle(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        float rx,
        float ry,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA)
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

            // Create SDF centered at origin, translate coordinates
            var sdf = new RoundedRectSdf(width, height, rx, ry);
            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);

            int stride = surface.Stride;
            byte* basePtr = (byte*)surface.Bits;

            float halfW = width / 2f;
            float halfH = height / 2f;

            byte[]? rented = null;
            Span<byte> alphaRow = width <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[width]
                : (rented = ArrayPool<byte>.Shared.Rent(width)).AsSpan(0, width);

            try
            {
                for (int py = 0; py < height; py++)
                {
                    // Convert to shape coordinates (centered)
                    float y = py + 0.5f - halfH;

                    // Get span at this Y level
                    sdf.GetSpanAtY(y, out float xLeft, out float xRight);

                    // Convert back to pixel coordinates
                    float pxLeft = xLeft + halfW;
                    float pxRight = xRight + halfW;

                    // Check if row is fully inside (no AA needed)
                    if (pxLeft <= 0 && pxRight >= width)
                    {
                        alphaRow.Fill(srcA);
                    }
                    else
                    {
                        alphaRow.Clear();

                        int solidStart = (int)MathF.Ceiling(pxLeft);
                        int solidEnd = (int)MathF.Floor(pxRight);

                        // Fill solid middle
                        if (solidStart < solidEnd)
                        {
                            solidStart = Math.Max(0, solidStart);
                            solidEnd = Math.Min(width, solidEnd);

                            if (solidStart < solidEnd)
                            {
                                alphaRow.Slice(solidStart, solidEnd - solidStart).Fill(srcA);
                            }
                        }

                        // Sample edges
                        int edgeLeft = Math.Max(0, (int)MathF.Floor(pxLeft) - 1);
                        int edgeRight = Math.Min(width - 1, (int)MathF.Ceiling(pxRight) + 1);

                        for (int px = edgeLeft; px < solidStart && px < width; px++)
                        {
                            alphaRow[px] = sampler.SampleRoundedRectEdge(px, py, sdf, halfW, halfH);
                        }

                        for (int px = Math.Max(solidEnd, 0); px <= edgeRight && px < width; px++)
                        {
                            alphaRow[px] = sampler.SampleRoundedRectEdge(px, py, sdf, halfW, halfH);
                        }
                    }

                    byte* rowPtr = basePtr + py * stride;
                    GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, srcB, srcG, srcR);
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
    /// Renders a stroked rounded rectangle with anti-aliasing.
    /// Uses inside stroke alignment: stroke is entirely within bounds.
    /// For bounds 10x10 with stroke 1, the interior is 8x8.
    /// </summary>
    public unsafe void DrawRoundedRectangle(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        float rx,
        float ry,
        float strokeWidth,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA)
    {
        if (width <= 0 || height <= 0 || srcA == 0 || strokeWidth <= 0)
        {
            return;
        }

        // Snap stroke width to integer for pixel-perfect straight edges
        int strokePx = Math.Max(1, (int)MathF.Round(strokeWidth));

        // Surface dimensions with AA padding (1px each side for edge anti-aliasing)
        const int aaPad = 1;
        int surfaceW = width + aaPad * 2;
        int surfaceH = height + aaPad * 2;

        var surface = _surfacePool.Rent(targetDc, surfaceW, surfaceH);
        if (!surface.IsValid)
        {
            return;
        }

        try
        {
            surface.Clear();

            // Inside stroke alignment:
            // - Outer shape = bounds exactly (width x height)
            // - Inner shape = bounds - 2*stroke
            // - Stroke is entirely inside the bounds
            float wOut = width;
            float hOut = height;
            float rxOut = rx;
            float ryOut = ry;

            float wIn = Math.Max(0, width - strokePx * 2);
            float hIn = Math.Max(0, height - strokePx * 2);
            float rxIn = Math.Max(0, rx - strokePx);
            float ryIn = Math.Max(0, ry - strokePx);

            var outerSdf = new RoundedRectSdf(wOut, hOut, rxOut, ryOut);
            var innerSdf = wIn > 0 && hIn > 0 ? new RoundedRectSdf(wIn, hIn, rxIn, ryIn) : null;

            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);

            int stride = surface.Stride;
            byte* basePtr = (byte*)surface.Bits;

            // Surface center for coordinate transformation
            float halfSurfaceW = surfaceW / 2f;
            float halfSurfaceH = surfaceH / 2f;

            byte[]? rented = null;
            Span<byte> alphaRow = surfaceW <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[surfaceW]
                : (rented = ArrayPool<byte>.Shared.Rent(surfaceW)).AsSpan(0, surfaceW);

            try
            {
                const float aaWidth = 1.0f;

                for (int py = 0; py < surfaceH; py++)
                {
                    for (int px = 0; px < surfaceW; px++)
                    {
                        float x = px + 0.5f - halfSurfaceW;
                        float y = py + 0.5f - halfSurfaceH;

                        float outerDist = outerSdf.GetSignedDistance(x, y);
                        if (outerDist >= aaWidth)
                        {
                            alphaRow[px] = 0;
                            continue;
                        }

                        if (innerSdf != null)
                        {
                            float innerDist = innerSdf.GetSignedDistance(x, y);

                            if (innerDist <= -aaWidth)
                            {
                                alphaRow[px] = 0;
                                continue;
                            }

                            if (outerDist <= -aaWidth && innerDist >= aaWidth)
                            {
                                alphaRow[px] = srcA;
                                continue;
                            }

                            alphaRow[px] = sampler.SampleStrokeEdgeCentered(px, py, outerSdf, innerSdf, halfSurfaceW, halfSurfaceH);
                            continue;
                        }

                        if (outerDist <= -aaWidth)
                        {
                            alphaRow[px] = srcA;
                            continue;
                        }

                        alphaRow[px] = sampler.SampleEdgeCentered(px, py, outerSdf, halfSurfaceW, halfSurfaceH);
                    }

                    byte* rowPtr = basePtr + py * stride;
                    GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, srcB, srcG, srcR);
                }
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

            // Stroke is inside bounds. AA padding is used only for sampling; do not blend outside the requested rect.
            surface.AlphaBlendTo(targetDc, destX, destY, width, height, aaPad, aaPad);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    #endregion

    #region Ellipse

    /// <summary>
    /// Renders a filled ellipse with anti-aliasing.
    /// </summary>
    public unsafe void FillEllipse(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA)
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

            var sdf = EllipseSdf.FromBounds(0, 0, width, height);
            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);

            int stride = surface.Stride;
            byte* basePtr = (byte*)surface.Bits;

            float cx = width / 2f;
            float cy = height / 2f;
            float rx = cx;
            float ry = cy;

            byte[]? rented = null;
            Span<byte> alphaRow = width <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[width]
                : (rented = ArrayPool<byte>.Shared.Rent(width)).AsSpan(0, width);

            try
            {
                for (int py = 0; py < height; py++)
                {
                    float yCenter = py + 0.5f;
                    float dy = MathF.Abs(yCenter - cy);

                    float t = 1f - (dy * dy) / (ry * ry);
                    if (t <= 0)
                    {
                        alphaRow.Clear();
                    }
                    else
                    {
                        float xOff = rx * MathF.Sqrt(t);
                        float xLeft = cx - xOff;
                        float xRight = cx + xOff;

                        if (xLeft <= 0 && xRight >= width)
                        {
                            alphaRow.Fill(srcA);
                        }
                        else
                        {
                            alphaRow.Clear();

                            int solidStart = (int)MathF.Ceiling(xLeft);
                            int solidEnd = (int)MathF.Floor(xRight);

                            solidStart = Math.Clamp(solidStart, 0, width);
                            solidEnd = Math.Clamp(solidEnd, 0, width);

                            if (solidStart < solidEnd)
                            {
                                alphaRow.Slice(solidStart, solidEnd - solidStart).Fill(srcA);
                            }

                            // Sample edges
                            int edgeLeft = Math.Max(0, solidStart - 2);
                            int edgeRight = Math.Min(width - 1, solidEnd + 1);

                            for (int px = edgeLeft; px < solidStart; px++)
                            {
                                alphaRow[px] = sampler.SampleEllipseEdge(px, py, sdf);
                            }

                            for (int px = solidEnd; px <= edgeRight; px++)
                            {
                                alphaRow[px] = sampler.SampleEllipseEdge(px, py, sdf);
                            }
                        }
                    }

                    byte* rowPtr = basePtr + py * stride;
                    GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, srcB, srcG, srcR);
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
    /// Renders a stroked ellipse with anti-aliasing.
    /// Uses inside stroke alignment: stroke is entirely within bounds.
    /// For bounds 10x10 with stroke 1, the interior is 8x8.
    /// </summary>
    public unsafe void DrawEllipse(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        float strokeWidth,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA)
    {
        if (width <= 0 || height <= 0 || srcA == 0 || strokeWidth <= 0)
        {
            return;
        }

        // Snap stroke width to integer for consistency
        int strokePx = Math.Max(1, (int)MathF.Round(strokeWidth));

        // Surface dimensions with AA padding (1px each side for edge anti-aliasing)
        const int aaPad = 1;
        int surfaceW = width + aaPad * 2;
        int surfaceH = height + aaPad * 2;

        var surface = _surfacePool.Rent(targetDc, surfaceW, surfaceH);
        if (!surface.IsValid)
        {
            return;
        }

        try
        {
            surface.Clear();

            // Inside stroke alignment:
            // - Outer ellipse = bounds exactly (width x height)
            // - Inner ellipse = bounds - 2*stroke
            float cx = surfaceW / 2f;
            float cy = surfaceH / 2f;

            float rxOut = width / 2f;
            float ryOut = height / 2f;

            float rxIn = Math.Max(0, (width - strokePx * 2) / 2f);
            float ryIn = Math.Max(0, (height - strokePx * 2) / 2f);

            var outerSdf = new EllipseSdf(cx, cy, rxOut, ryOut);
            var innerSdf = rxIn > 0 && ryIn > 0 ? new EllipseSdf(cx, cy, rxIn, ryIn) : null;

            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);

            int stride = surface.Stride;
            byte* basePtr = (byte*)surface.Bits;

            byte[]? rented = null;
            Span<byte> alphaRow = surfaceW <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[surfaceW]
                : (rented = ArrayPool<byte>.Shared.Rent(surfaceW)).AsSpan(0, surfaceW);

            try
            {
                for (int py = 0; py < surfaceH; py++)
                {
                    alphaRow.Clear();

                    float yCenter = py + 0.5f;
                    float dy = MathF.Abs(yCenter - cy);

                    float tOut = 1f - (dy * dy) / (ryOut * ryOut);
                    if (tOut <= 0)
                    {
                        byte* rowPtr0 = basePtr + py * stride;
                        GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr0, alphaRow, srcB, srcG, srcR);
                        continue;
                    }

                    float xOffOut = rxOut * MathF.Sqrt(tOut);
                    float xLeftOut = cx - xOffOut;
                    float xRightOut = cx + xOffOut;

                    // Sample stroke pixels
                    int left = Math.Max(0, (int)MathF.Floor(xLeftOut) - 1);
                    int right = Math.Min(surfaceW - 1, (int)MathF.Ceiling(xRightOut) + 1);

                    for (int px = left; px <= right; px++)
                    {
                        alphaRow[px] = sampler.SampleStrokeEdge(px, py, outerSdf, innerSdf);
                    }

                    byte* rowPtr = basePtr + py * stride;
                    GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, srcB, srcG, srcR);
                }
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

            // Stroke is inside bounds. AA padding is used only for sampling; do not blend outside the requested rect.
            surface.AlphaBlendTo(targetDc, destX, destY, width, height, aaPad, aaPad);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    #endregion

    #region Line

    /// <summary>
    /// Renders an anti-aliased line.
    /// </summary>
    public unsafe void DrawLine(
        nint targetDc,
        float ax,
        float ay,
        float bx,
        float by,
        float thickness,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA)
    {
        if (srcA == 0 || thickness <= 0)
        {
            return;
        }

        var lineSdf = new LineSdf(ax, ay, bx, by, thickness);

        // Axis-aligned lines don't need AA
        if (lineSdf.IsAxisAligned)
        {
            return; // Let caller handle with simple GDI
        }

        lineSdf.GetPixelBounds(1f, out int left, out int top, out int right, out int bottom);

        int width = right - left;
        int height = bottom - top;

        if (width <= 0 || height <= 0 || width > GdiRenderingConstants.MaxAaSurfaceSize || height > GdiRenderingConstants.MaxAaSurfaceSize)
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

            // Create SDF in surface coordinates
            var surfaceLineSdf = new LineSdf(ax - left, ay - top, bx - left, by - top, thickness);
            float halfThickSq = (thickness / 2f) * (thickness / 2f);

            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);

            int stride = surface.Stride;
            byte* basePtr = (byte*)surface.Bits;

            byte[]? rented = null;
            Span<byte> alphaRow = width <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[width]
                : (rented = ArrayPool<byte>.Shared.Rent(width)).AsSpan(0, width);

            try
            {
                for (int py = 0; py < height; py++)
                {
                    alphaRow.Clear();

                    for (int px = 0; px < width; px++)
                    {
                        alphaRow[px] = sampler.SampleLineEdge(px, py, surfaceLineSdf, halfThickSq);
                    }

                    byte* rowPtr = basePtr + py * stride;
                    GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, srcB, srcG, srcR);
                }
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

            surface.AlphaBlendTo(targetDc, left, top);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    #endregion
}
