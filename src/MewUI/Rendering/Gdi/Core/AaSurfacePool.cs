namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// Pool for AaSurface instances to reduce allocation overhead.
/// Maintains separate pools for small and large surfaces.
/// </summary>
internal sealed class AaSurfacePool : IDisposable
{
    private readonly List<AaSurface> _smallPool = new();
    private readonly List<AaSurface> _largePool = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Rents a surface with at least the specified dimensions.
    /// </summary>
    /// <param name="sourceDc">Source DC for creating new surfaces if needed.</param>
    /// <param name="width">Minimum required width.</param>
    /// <param name="height">Minimum required height.</param>
    /// <returns>A surface that is at least the requested size.</returns>
    public AaSurface Rent(nint sourceDc, int width, int height)
    {
        if (_disposed)
        {
            return new AaSurface(sourceDc, width, height);
        }

        width = Math.Max(1, Math.Min(width, GdiRenderingConstants.MaxAaSurfaceSize));
        height = Math.Max(1, Math.Min(height, GdiRenderingConstants.MaxAaSurfaceSize));

        bool isSmall = width <= GdiRenderingConstants.SmallBufferThreshold &&
                       height <= GdiRenderingConstants.SmallBufferThreshold;

        lock (_lock)
        {
            var pool = isSmall ? _smallPool : _largePool;

            // Try to find a surface that matches exactly or is larger
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                var surface = pool[i];
                if (surface.Width >= width && surface.Height >= height)
                {
                    pool.RemoveAt(i);
                    // Resize to exact dimensions if significantly larger
                    if (surface.Width > width * 2 || surface.Height > height * 2)
                    {
                        surface.EnsureSize(sourceDc, width, height);
                    }
                    return surface;
                }
            }

            // No suitable surface found, create new one
            return new AaSurface(sourceDc, width, height);
        }
    }

    /// <summary>
    /// Returns a surface to the pool for reuse.
    /// </summary>
    /// <param name="surface">The surface to return.</param>
    public void Return(AaSurface surface)
    {
        if (_disposed || surface == null || !surface.IsValid)
        {
            surface?.Dispose();
            return;
        }

        bool isSmall = surface.Width <= GdiRenderingConstants.SmallBufferThreshold &&
                       surface.Height <= GdiRenderingConstants.SmallBufferThreshold;

        lock (_lock)
        {
            var pool = isSmall ? _smallPool : _largePool;
            int maxSize = isSmall ? GdiRenderingConstants.MaxSmallBufferCacheSize : GdiRenderingConstants.MaxLargeBufferPoolSize;

            if (pool.Count >= maxSize)
            {
                // Pool is full, dispose the smallest surface or the returned one
                int smallestIdx = -1;
                int smallestArea = int.MaxValue;

                for (int i = 0; i < pool.Count; i++)
                {
                    int area = pool[i].Width * pool[i].Height;
                    if (area < smallestArea)
                    {
                        smallestArea = area;
                        smallestIdx = i;
                    }
                }

                int returnedArea = surface.Width * surface.Height;

                if (smallestIdx >= 0 && smallestArea < returnedArea)
                {
                    // Keep the larger surface (the returned one)
                    pool[smallestIdx].Dispose();
                    pool[smallestIdx] = surface;
                }
                else
                {
                    // Returned surface is smaller, just dispose it
                    surface.Dispose();
                }
            }
            else
            {
                pool.Add(surface);
            }
        }
    }

    /// <summary>
    /// Clears all pooled surfaces.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var surface in _smallPool)
            {
                surface.Dispose();
            }
            _smallPool.Clear();

            foreach (var surface in _largePool)
            {
                surface.Dispose();
            }
            _largePool.Clear();
        }
    }

    /// <summary>
    /// Trims the pool to reduce memory usage.
    /// </summary>
    /// <param name="keepCount">Number of surfaces to keep in each pool.</param>
    public void Trim(int keepCount = 1)
    {
        lock (_lock)
        {
            TrimPool(_smallPool, keepCount);
            TrimPool(_largePool, keepCount);
        }
    }

    private static void TrimPool(List<AaSurface> pool, int keepCount)
    {
        while (pool.Count > keepCount)
        {
            int idx = pool.Count - 1;
            pool[idx].Dispose();
            pool.RemoveAt(idx);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Clear();
            _disposed = true;
        }
    }
}
