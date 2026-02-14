using System.Buffers;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
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
    [SkipLocalsInit]
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

        // Fast path: opaque fill. Use direct GDI fills for the solid area and alpha-blend only corner tiles.
        // This avoids a full-surface AlphaBlend for large rounded rectangles.
        if (srcA == 255 && TryFillRoundedRectangleCornerBlend(targetDc, destX, destY, width, height, rx, ry, srcB, srcG, srcR))
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
                Span<uint> premulTable = stackalloc uint[256];
                GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

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
    /// Renders a stroked rounded rectangle with anti-aliasing.
    /// Uses inside stroke alignment: stroke is entirely within bounds.
    /// For bounds 10x10 with stroke 1, the interior is 8x8.
    /// </summary>
    [SkipLocalsInit]
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

        // Fast path: opaque stroke. Use direct GDI fills for straight stroke bands and alpha-blend only corner tiles.
        if (srcA == 255 && TryDrawRoundedRectangleCornerBlend(targetDc, destX, destY, width, height, rx, ry, strokePx, srcB, srcG, srcR))
        {
            return;
        }

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
                Span<uint> premulTable = stackalloc uint[256];
                GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

                for (int py = 0; py < surfaceH; py++)
                {
                    alphaRow.Clear();

                    float y = py + 0.5f - halfSurfaceH;

                    outerSdf.GetSpanAtY(y, out float xLeftOuter, out float xRightOuter);
                    if (xLeftOuter == 0 && xRightOuter == 0)
                    {
                        byte* rowPtr0 = basePtr + py * stride;
                        GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr0, alphaRow, premulTable);
                        continue;
                    }

                    float pxLeftOuter = xLeftOuter + halfSurfaceW;
                    float pxRightOuter = xRightOuter + halfSurfaceW;

                    int outerSolidStart = (int)MathF.Ceiling(pxLeftOuter);
                    int outerSolidEnd = (int)MathF.Floor(pxRightOuter);
                    outerSolidStart = Math.Clamp(outerSolidStart, 0, surfaceW);
                    outerSolidEnd = Math.Clamp(outerSolidEnd, 0, surfaceW);

                    int edgePad = _supersampleFactor == 1 ? 1 : 2;
                    int outerEdgeL0 = Math.Max(0, outerSolidStart - edgePad);
                    int outerEdgeL1 = Math.Min(surfaceW, outerSolidStart + edgePad);
                    int outerEdgeR0 = Math.Max(0, outerSolidEnd - edgePad);
                    int outerEdgeR1 = Math.Min(surfaceW, outerSolidEnd + edgePad);

                    bool hasInnerSpan = false;
                    int innerLeftEdge = 0;
                    int innerRightEdge = 0;
                    int innerEdgeL0 = 0;
                    int innerEdgeL1 = 0;
                    int innerEdgeR0 = 0;
                    int innerEdgeR1 = 0;

                    if (innerSdf != null)
                    {
                        innerSdf.GetSpanAtY(y, out float xLeftInner, out float xRightInner);
                        if (!(xLeftInner == 0 && xRightInner == 0))
                        {
                            hasInnerSpan = true;
                            float pxLeftInner = xLeftInner + halfSurfaceW;
                            float pxRightInner = xRightInner + halfSurfaceW;

                            innerLeftEdge = Math.Clamp((int)MathF.Floor(pxLeftInner), 0, surfaceW);
                            innerRightEdge = Math.Clamp((int)MathF.Ceiling(pxRightInner), 0, surfaceW);

                            innerEdgeL0 = Math.Max(0, innerLeftEdge - edgePad);
                            innerEdgeL1 = Math.Min(surfaceW, innerLeftEdge + edgePad);
                            innerEdgeR0 = Math.Max(0, innerRightEdge - edgePad);
                            innerEdgeR1 = Math.Min(surfaceW, innerRightEdge + edgePad);
                        }
                    }

                    if (!hasInnerSpan)
                    {
                        int solidStart = Math.Min(outerSolidStart, outerSolidEnd);
                        int solidEnd = Math.Max(outerSolidStart, outerSolidEnd);
                        if (solidStart < solidEnd)
                        {
                            alphaRow.Slice(solidStart, solidEnd - solidStart).Fill(srcA);
                        }
                    }
                    else
                    {
                        int leftSolidStart = outerSolidStart;
                        int leftSolidEnd = Math.Min(outerSolidEnd, innerLeftEdge);
                        if (leftSolidStart < leftSolidEnd)
                        {
                            alphaRow.Slice(leftSolidStart, leftSolidEnd - leftSolidStart).Fill(srcA);
                        }

                        int rightSolidStart = Math.Max(outerSolidStart, innerRightEdge);
                        int rightSolidEnd = outerSolidEnd;
                        if (rightSolidStart < rightSolidEnd)
                        {
                            alphaRow.Slice(rightSolidStart, rightSolidEnd - rightSolidStart).Fill(srcA);
                        }
                    }

                    // Outer edge AA
                    for (int px = outerEdgeL0; px < outerEdgeL1; px++)
                    {
                        alphaRow[px] = innerSdf != null
                            ? sampler.SampleRoundedRectStrokeEdgeSsaa(px, py, outerSdf, innerSdf, halfSurfaceW, halfSurfaceH)
                            : sampler.SampleRoundedRectEdge(px, py, outerSdf, halfSurfaceW, halfSurfaceH);
                    }

                    for (int px = outerEdgeR0; px < outerEdgeR1; px++)
                    {
                        alphaRow[px] = innerSdf != null
                            ? sampler.SampleRoundedRectStrokeEdgeSsaa(px, py, outerSdf, innerSdf, halfSurfaceW, halfSurfaceH)
                            : sampler.SampleRoundedRectEdge(px, py, outerSdf, halfSurfaceW, halfSurfaceH);
                    }

                    // Inner edge AA
                    if (hasInnerSpan)
                    {
                        for (int px = innerEdgeL0; px < innerEdgeL1; px++)
                        {
                            alphaRow[px] = sampler.SampleRoundedRectStrokeEdgeSsaa(px, py, outerSdf, innerSdf, halfSurfaceW, halfSurfaceH);
                        }

                        for (int px = innerEdgeR0; px < innerEdgeR1; px++)
                        {
                            alphaRow[px] = sampler.SampleRoundedRectStrokeEdgeSsaa(px, py, outerSdf, innerSdf, halfSurfaceW, halfSurfaceH);
                        }
                    }

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

            // Stroke is inside bounds. AA padding is used only for sampling; do not blend outside the requested rect.
            surface.AlphaBlendTo(targetDc, destX, destY, width, height, aaPad, aaPad);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    #endregion

    private static uint ToColorRef(byte b, byte g, byte r) => ((uint)b << 16) | ((uint)g << 8) | r;

    private static void FillRect(nint dc, nint brush, int x, int y, int width, int height)
    {
        if (dc == 0 || brush == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var rect = RECT.FromXYWH(x, y, width, height);
        Gdi32.FillRect(dc, ref rect, brush);
    }

    private bool TryFillRoundedRectangleCornerBlend(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        float rx,
        float ry,
        byte srcB,
        byte srcG,
        byte srcR)
    {
        int rxPx = Math.Clamp((int)MathF.Round(rx), 0, width / 2);
        int ryPx = Math.Clamp((int)MathF.Round(ry), 0, height / 2);

        if (rxPx <= 0 || ryPx <= 0)
        {
            return false;
        }

        uint colorRef = ToColorRef(srcB, srcG, srcR);
        nint brush = Gdi32.CreateSolidBrush(colorRef);
        if (brush == 0)
        {
            return false;
        }

        try
        {
            // Fill the solid (non-corner) area directly with GDI.
            FillRect(targetDc, brush, destX + rxPx, destY, width - rxPx * 2, height);
            FillRect(targetDc, brush, destX, destY + ryPx, rxPx, height - ryPx * 2);
            FillRect(targetDc, brush, destX + width - rxPx, destY + ryPx, rxPx, height - ryPx * 2);

            // Alpha-blend only the four corner tiles.
            FillRoundedCornerTiles(targetDc, destX, destY, width, height, rx, ry, rxPx, ryPx, srcB, srcG, srcR, 255, fillOnly: true, strokePx: 0);
            return true;
        }
        finally
        {
            Gdi32.DeleteObject(brush);
        }
    }

    private bool TryDrawRoundedRectangleCornerBlend(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        float rx,
        float ry,
        int strokePx,
        byte srcB,
        byte srcG,
        byte srcR)
    {
        int rxPx = Math.Clamp((int)MathF.Round(rx), 0, width / 2);
        int ryPx = Math.Clamp((int)MathF.Round(ry), 0, height / 2);

        if (strokePx <= 0 || rxPx <= 0 || ryPx <= 0)
        {
            return false;
        }

        // If the stroke consumes the radius, fall back to the full AA path.
        if (strokePx >= rxPx || strokePx >= ryPx)
        {
            return false;
        }

        uint colorRef = ToColorRef(srcB, srcG, srcR);
        nint brush = Gdi32.CreateSolidBrush(colorRef);
        if (brush == 0)
        {
            return false;
        }

        try
        {
            // Fill straight stroke bands (inside alignment). Leave corners for AA tile blending.
            FillRect(targetDc, brush, destX + rxPx, destY, width - rxPx * 2, strokePx);
            FillRect(targetDc, brush, destX + rxPx, destY + height - strokePx, width - rxPx * 2, strokePx);
            FillRect(targetDc, brush, destX, destY + ryPx, strokePx, height - ryPx * 2);
            FillRect(targetDc, brush, destX + width - strokePx, destY + ryPx, strokePx, height - ryPx * 2);

            FillRoundedCornerTiles(targetDc, destX, destY, width, height, rx, ry, rxPx, ryPx, srcB, srcG, srcR, 255, fillOnly: false, strokePx: strokePx);
            return true;
        }
        finally
        {
            Gdi32.DeleteObject(brush);
        }
    }

    private void FillRoundedCornerTiles(
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height,
        float rx,
        float ry,
        int rxPx,
        int ryPx,
        byte srcB,
        byte srcG,
        byte srcR,
        byte srcA,
        bool fillOnly,
        int strokePx)
    {
        var surface = _surfacePool.Rent(targetDc, rxPx, ryPx);
        if (!surface.IsValid)
        {
            return;
        }

        try
        {
            surface.Clear();

            var sampler = new SupersampleEdgeSampler(_supersampleFactor, srcA);

            float halfW = width / 2f;
            float halfH = height / 2f;

            var outerSdf = new RoundedRectSdf(width, height, rx, ry);
            RoundedRectSdf? innerSdf = null;
            if (!fillOnly)
            {
                float wIn = Math.Max(0, width - strokePx * 2);
                float hIn = Math.Max(0, height - strokePx * 2);
                float rxIn = Math.Max(0, rx - strokePx);
                float ryIn = Math.Max(0, ry - strokePx);
                if (wIn > 0 && hIn > 0)
                {
                    innerSdf = new RoundedRectSdf(wIn, hIn, rxIn, ryIn);
                }
            }

            Span<uint> premulTable = stackalloc uint[256];
            GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

            byte[]? rented = null;
            Span<byte> alphaRow = rxPx <= GdiRenderingConstants.StackAllocAlphaRowThreshold
                ? stackalloc byte[rxPx]
                : (rented = ArrayPool<byte>.Shared.Rent(rxPx)).AsSpan(0, rxPx);

            try
            {
                RenderCornerTile(surface, 0, 0, rxPx, ryPx, alphaRow, premulTable, outerSdf, innerSdf, sampler, halfW, halfH, fillOnly, srcA);
                surface.AlphaBlendTo(targetDc, destX, destY, rxPx, ryPx, 0, 0);

                surface.Clear();
                RenderCornerTile(surface, width - rxPx, 0, rxPx, ryPx, alphaRow, premulTable, outerSdf, innerSdf, sampler, halfW, halfH, fillOnly, srcA);
                surface.AlphaBlendTo(targetDc, destX + width - rxPx, destY, rxPx, ryPx, 0, 0);

                surface.Clear();
                RenderCornerTile(surface, 0, height - ryPx, rxPx, ryPx, alphaRow, premulTable, outerSdf, innerSdf, sampler, halfW, halfH, fillOnly, srcA);
                surface.AlphaBlendTo(targetDc, destX, destY + height - ryPx, rxPx, ryPx, 0, 0);

                surface.Clear();
                RenderCornerTile(surface, width - rxPx, height - ryPx, rxPx, ryPx, alphaRow, premulTable, outerSdf, innerSdf, sampler, halfW, halfH, fillOnly, srcA);
                surface.AlphaBlendTo(targetDc, destX + width - rxPx, destY + height - ryPx, rxPx, ryPx, 0, 0);
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    private static unsafe void RenderCornerTile(
        AaSurface surface,
        int tileOriginX,
        int tileOriginY,
        int tileW,
        int tileH,
        Span<byte> alphaRow,
        ReadOnlySpan<uint> premulTable,
        RoundedRectSdf outerSdf,
        RoundedRectSdf? innerSdf,
        SupersampleEdgeSampler sampler,
        float halfW,
        float halfH,
        bool fillOnly,
        byte srcA)
    {
        byte* basePtr = (byte*)surface.Bits;
        int stride = surface.Stride;

        for (int y = 0; y < tileH; y++)
        {
            int py = tileOriginY + y;

            for (int x = 0; x < tileW; x++)
            {
                int px = tileOriginX + x;
                alphaRow[x] = fillOnly
                    ? sampler.SampleRoundedRectEdge(px, py, outerSdf, halfW, halfH)
                    : sampler.SampleRoundedRectStrokeEdgeSsaa(px, py, outerSdf, innerSdf, halfW, halfH);
            }

            byte* rowPtr = basePtr + y * stride;
            GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr, alphaRow, premulTable);
        }
    }

    #region Ellipse

    /// <summary>
    /// Renders a filled ellipse with anti-aliasing.
    /// </summary>
    [SkipLocalsInit]
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
                Span<uint> premulTable = stackalloc uint[256];
                GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

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
    /// Renders a stroked ellipse with anti-aliasing.
    /// Uses inside stroke alignment: stroke is entirely within bounds.
    /// For bounds 10x10 with stroke 1, the interior is 8x8.
    /// </summary>
    [SkipLocalsInit]
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
                Span<uint> premulTable = stackalloc uint[256];
                GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

                for (int py = 0; py < surfaceH; py++)
                {
                    alphaRow.Clear();

                    float yCenter = py + 0.5f;
                    float dy = MathF.Abs(yCenter - cy);

                    float tOut = 1f - (dy * dy) / (ryOut * ryOut);
                    if (tOut <= 0)
                    {
                        byte* rowPtr0 = basePtr + py * stride;
                        GdiSimdDispatcher.WritePremultipliedBgraRow(rowPtr0, alphaRow, premulTable);
                        continue;
                    }

                    float xOffOut = rxOut * MathF.Sqrt(tOut);
                    float xLeftOut = cx - xOffOut;
                    float xRightOut = cx + xOffOut;

                    // Sample only where the stroke can exist.
                    // When an inner ellipse exists, the stroke cross-section is two narrow bands:
                    // [outerLeft .. innerLeft] and [innerRight .. outerRight]. Sampling the whole outer span is
                    // unnecessarily expensive for wide ellipses.
                    if (innerSdf != null && rxIn > 0 && ryIn > 0)
                    {
                        float tIn = 1f - (dy * dy) / (ryIn * ryIn);
                        if (tIn > 0)
                        {
                            float xOffIn = rxIn * MathF.Sqrt(tIn);
                            float xLeftIn = cx - xOffIn;
                            float xRightIn = cx + xOffIn;

                            int leftBandStart = Math.Max(0, (int)MathF.Floor(xLeftOut) - 1);
                            int leftBandEnd = Math.Min(surfaceW - 1, (int)MathF.Ceiling(xLeftIn) + 1);

                            int rightBandStart = Math.Max(0, (int)MathF.Floor(xRightIn) - 1);
                            int rightBandEnd = Math.Min(surfaceW - 1, (int)MathF.Ceiling(xRightOut) + 1);

                            if (leftBandEnd >= leftBandStart)
                            {
                                for (int px = leftBandStart; px <= leftBandEnd; px++)
                                {
                                    alphaRow[px] = sampler.SampleStrokeEdge(px, py, outerSdf, innerSdf);
                                }
                            }

                            if (rightBandEnd >= rightBandStart)
                            {
                                for (int px = rightBandStart; px <= rightBandEnd; px++)
                                {
                                    alphaRow[px] = sampler.SampleStrokeEdge(px, py, outerSdf, innerSdf);
                                }
                            }
                        }
                        else
                        {
                            // No inner hole at this Y; fall back to sampling the outer span (typically narrow here).
                            int left = Math.Max(0, (int)MathF.Floor(xLeftOut) - 1);
                            int right = Math.Min(surfaceW - 1, (int)MathF.Ceiling(xRightOut) + 1);

                            for (int px = left; px <= right; px++)
                            {
                                alphaRow[px] = sampler.SampleStrokeEdge(px, py, outerSdf, innerSdf);
                            }
                        }
                    }
                    else
                    {
                        // No inner ellipse; sample the outer span.
                        int left = Math.Max(0, (int)MathF.Floor(xLeftOut) - 1);
                        int right = Math.Min(surfaceW - 1, (int)MathF.Ceiling(xRightOut) + 1);

                        for (int px = left; px <= right; px++)
                        {
                            alphaRow[px] = sampler.SampleStrokeEdge(px, py, outerSdf, innerSdf);
                        }
                    }

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
    [SkipLocalsInit]
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
                Span<uint> premulTable = stackalloc uint[256];
                GdiSimdDispatcher.BuildPremultipliedBgraTable(premulTable, srcB, srcG, srcR);

                for (int py = 0; py < height; py++)
                {
                    alphaRow.Clear();

                    for (int px = 0; px < width; px++)
                    {
                        alphaRow[px] = sampler.SampleLineEdge(px, py, surfaceLineSdf, halfThickSq);
                    }

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

            surface.AlphaBlendTo(targetDc, left, top);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    #endregion
}
