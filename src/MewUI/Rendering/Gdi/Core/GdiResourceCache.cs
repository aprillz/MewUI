using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// Caches GDI pen and brush resources to avoid repeated creation/deletion.
/// Uses LRU eviction policy when cache limits are reached.
/// </summary>
internal sealed class GdiResourceCache : IDisposable
{
    private readonly Dictionary<PenKey, CachedResource> _pens = new();
    private readonly Dictionary<uint, CachedResource> _brushes = new();
    private readonly LinkedList<PenKey> _penLru = new();
    private readonly LinkedList<uint> _brushLru = new();
    private readonly object _lock = new();
    private bool _disposed;

    private readonly struct PenKey : IEquatable<PenKey>
    {
        public readonly uint ColorRef;
        public readonly int Width;

        public PenKey(uint colorRef, int width)
        {
            ColorRef = colorRef;
            Width = width;
        }

        public bool Equals(PenKey other) => ColorRef == other.ColorRef && Width == other.Width;
        public override bool Equals(object? obj) => obj is PenKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ColorRef, Width);
    }

    private sealed class CachedResource
    {
        public nint Handle;
        public LinkedListNode<PenKey>? PenNode;
        public LinkedListNode<uint>? BrushNode;
    }

    /// <summary>
    /// Gets or creates a cached pen with the specified color and width.
    /// </summary>
    public nint GetOrCreatePen(uint colorRef, int width)
    {
        if (_disposed)
        {
            return 0;
        }

        var key = new PenKey(colorRef, width);

        lock (_lock)
        {
            if (_pens.TryGetValue(key, out var cached))
            {
                // Move to front of LRU list
                if (cached.PenNode != null)
                {
                    _penLru.Remove(cached.PenNode);
                    _penLru.AddFirst(cached.PenNode);
                }
                return cached.Handle;
            }

            // Evict oldest if at capacity
            while (_pens.Count >= GdiRenderingConstants.MaxCachedPens && _penLru.Last != null)
            {
                var oldKey = _penLru.Last.Value;
                if (_pens.TryGetValue(oldKey, out var oldCached))
                {
                    if (oldCached.Handle != 0)
                    {
                        Gdi32.DeleteObject(oldCached.Handle);
                    }
                    _pens.Remove(oldKey);
                }
                _penLru.RemoveLast();
            }

            // Create new pen
            var handle = Gdi32.CreatePen(GdiConstants2.PS_SOLID, width, colorRef);
            if (handle == 0)
            {
                return 0;
            }

            var node = _penLru.AddFirst(key);
            _pens[key] = new CachedResource { Handle = handle, PenNode = node };

            return handle;
        }
    }

    /// <summary>
    /// Gets or creates a cached solid brush with the specified color.
    /// </summary>
    public nint GetOrCreateBrush(uint colorRef)
    {
        if (_disposed)
        {
            return 0;
        }

        lock (_lock)
        {
            if (_brushes.TryGetValue(colorRef, out var cached))
            {
                // Move to front of LRU list
                if (cached.BrushNode != null)
                {
                    _brushLru.Remove(cached.BrushNode);
                    _brushLru.AddFirst(cached.BrushNode);
                }
                return cached.Handle;
            }

            // Evict oldest if at capacity
            while (_brushes.Count >= GdiRenderingConstants.MaxCachedBrushes && _brushLru.Last != null)
            {
                var oldKey = _brushLru.Last.Value;
                if (_brushes.TryGetValue(oldKey, out var oldCached))
                {
                    if (oldCached.Handle != 0)
                    {
                        Gdi32.DeleteObject(oldCached.Handle);
                    }
                    _brushes.Remove(oldKey);
                }
                _brushLru.RemoveLast();
            }

            // Create new brush
            var handle = Gdi32.CreateSolidBrush(colorRef);
            if (handle == 0)
            {
                return 0;
            }

            var node = _brushLru.AddFirst(colorRef);
            _brushes[colorRef] = new CachedResource { Handle = handle, BrushNode = node };

            return handle;
        }
    }

    /// <summary>
    /// Clears all cached resources.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var (_, cached) in _pens)
            {
                if (cached.Handle != 0)
                {
                    Gdi32.DeleteObject(cached.Handle);
                }
            }
            _pens.Clear();
            _penLru.Clear();

            foreach (var (_, cached) in _brushes)
            {
                if (cached.Handle != 0)
                {
                    Gdi32.DeleteObject(cached.Handle);
                }
            }
            _brushes.Clear();
            _brushLru.Clear();
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

// Alias for avoiding conflict with existing GdiConstants in Native.Constants
file static class GdiConstants2
{
    public const int PS_SOLID = 0;
}
