using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// Caches rasterized per-pixel-alpha text (DIB surfaces) so repeated draws of the same text/font/color/size
/// skip re-rasterization every frame. Only used for the axis-aligned, non-pixel-surface draw path: rotated
/// text and offscreen pixel-surface targets keep rendering directly through <see cref="PerPixelAlphaTextRenderer"/>
/// (see call sites in GdiPlusGraphicsContext for rationale). Owned by a per-HWND long-lived object, not the
/// per-frame graphics context.
/// </summary>
internal sealed class GdiTextCache : IDisposable
{
    // Raw premultiplied DIBs (no atlas packing), so a smaller budget than MewVGTextCache's 16MB atlas cache.
    private const long DefaultMaxBytes = 8L * 1024 * 1024;

    private readonly AaSurfacePool _surfacePool;
    private readonly Dictionary<TextCacheKey, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private long _currentBytes;
    private bool _disposed;

    public long MaxBytes
    {
        get;
        set => field = Math.Max(0, value);
    } = DefaultMaxBytes;

    public GdiTextCache(AaSurfacePool surfacePool)
    {
        _surfacePool = surfacePool;
    }

    /// <summary>
    /// Draws text through the cache: composites a cached rasterization on hit, otherwise rasterizes
    /// once via the same coverage-to-premultiplied pipeline as <see cref="PerPixelAlphaTextRenderer"/>
    /// and caches the result before compositing. Caller guarantees no world transform and no pixel-surface
    /// target (both keep using PerPixelAlphaTextRenderer directly).
    /// </summary>
    public unsafe void DrawCached(
        nint hdc,
        ReadOnlySpan<char> text,
        RECT targetRect,
        GdiFont font,
        Color color,
        uint format,
        int yOffsetPx,
        int textHeightPx,
        TextWrapping wrapping,
        TextTrimming trimming,
        TextAlignment hAlign,
        TextAlignment vAlign)
    {
        int width = targetRect.Width;
        int height = targetRect.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (width > GdiRenderingConstants.MaxAaSurfaceSize || height > GdiRenderingConstants.MaxAaSurfaceSize)
        {
            // Oversized bounds: fall back to the uncached renderer rather than growing the cache unbounded.
            PerPixelAlphaTextRenderer.DrawText(hdc, null, _surfacePool, text, targetRect, font, color, format,
                yOffsetPx, textHeightPx, wrapping, trimming, hAlign, vAlign);
            return;
        }

        var fontHandle = font.GetHandle(GdiFontRenderMode.Coverage);
        var key = new TextCacheKey(
            string.GetHashCode(text), fontHandle, string.Empty, 0, color.ToArgb(),
            width, height, (int)hAlign, (int)vAlign, (int)wrapping, (int)trimming);

        if (TryGet(key, text, out var cached))
        {
            cached.AlphaBlendTo(hdc, targetRect.left, targetRect.top, width, height, 0, 0);
            return;
        }

        var surface = _surfacePool.Rent(hdc, width, height);
        if (!surface.IsValid)
        {
            _surfacePool.Return(surface);
            return;
        }

        surface.Clear();

        var oldFont = Gdi32.SelectObject(surface.MemDc, fontHandle);
        var oldColor = Gdi32.SetTextColor(surface.MemDc, 0x00FFFFFF);
        int oldBkMode = Gdi32.SetBkMode(surface.MemDc, GdiConstants.TRANSPARENT);
        try
        {
            var localRect = RECT.FromLTRB(0, 0, width, height);
            if (yOffsetPx != 0)
            {
                localRect.top += yOffsetPx;
                localRect.bottom += yOffsetPx;
            }
            if (textHeightPx > 0)
            {
                localRect.bottom = localRect.top + textHeightPx;
            }

            bool drawn = false;
            if (trimming == TextTrimming.CharacterEllipsis && wrapping != TextWrapping.NoWrap)
            {
                drawn = GdiWrappedEllipsisHelper.TryDrawWrappedWithEllipsis(
                    surface.MemDc, text, localRect, width, height, hAlign, vAlign);
            }

            if (!drawn)
            {
                fixed (char* pText = text)
                {
                    Gdi32.DrawText(surface.MemDc, pText, text.Length, ref localRect, format);
                }
            }
        }
        finally
        {
            Gdi32.SetBkMode(surface.MemDc, oldBkMode);
            Gdi32.SetTextColor(surface.MemDc, oldColor);
            Gdi32.SelectObject(surface.MemDc, oldFont);
        }

        CoverageToPremultiplied(surface, width, height, color);
        Insert(key, text, surface);
        surface.AlphaBlendTo(hdc, targetRect.left, targetRect.top, width, height, 0, 0);
    }

    // Mirrors PerPixelAlphaTextRenderer.CoverageToPremultiplied: that method is private to a file outside this
    // task's scope, so the (small) coverage->premultiplied-color conversion is duplicated here rather than
    // widening that file's API surface.
    private static unsafe void CoverageToPremultiplied(AaSurface surface, int width, int height, Color color)
    {
        byte aColor = color.A;
        for (int y = 0; y < height; y++)
        {
            byte* row = surface.GetRowPointer(y);
            if (row == null)
            {
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                int pixelOffset = x * 4;
                byte blueCoverage = row[pixelOffset + 0];
                byte greenCoverage = row[pixelOffset + 1];
                byte redCoverage = row[pixelOffset + 2];
                byte coverage = blueCoverage;
                if (greenCoverage > coverage) coverage = greenCoverage;
                if (redCoverage > coverage) coverage = redCoverage;

                if (coverage == 0 || aColor == 0)
                {
                    row[pixelOffset + 0] = 0;
                    row[pixelOffset + 1] = 0;
                    row[pixelOffset + 2] = 0;
                    row[pixelOffset + 3] = 0;
                    continue;
                }

                coverage = (byte)((coverage * coverage + 127) / 255);
                byte alpha = (byte)((coverage * aColor + 127) / 255);
                row[pixelOffset + 0] = (byte)((color.B * alpha + 127) / 255);
                row[pixelOffset + 1] = (byte)((color.G * alpha + 127) / 255);
                row[pixelOffset + 2] = (byte)((color.R * alpha + 127) / 255);
                row[pixelOffset + 3] = alpha;
            }
        }
    }

    private bool TryGet(TextCacheKey key, ReadOnlySpan<char> text, out AaSurface surface)
    {
        if (_disposed)
        {
            surface = null!;
            return false;
        }

        // TextHash only narrows the dictionary bucket; a mismatch on the actual text is treated as a miss
        // so two different strings that happen to collide never draw the wrong cached bitmap.
        if (_map.TryGetValue(key, out var node) && text.SequenceEqual(node.Value.Text))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            surface = node.Value.Surface;
            return true;
        }

        surface = null!;
        return false;
    }

    private void Insert(TextCacheKey key, ReadOnlySpan<char> text, AaSurface surface)
    {
        if (_disposed)
        {
            _surfacePool.Return(surface);
            return;
        }

        if (_map.TryGetValue(key, out var replaced))
        {
            _lru.Remove(replaced);
            _currentBytes -= replaced.Value.Bytes;
            _surfacePool.Return(replaced.Value.Surface);
        }

        long bytes = (long)surface.Width * surface.Height * 4;
        var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, text.ToString(), surface, bytes));
        _lru.AddFirst(node);
        _map[key] = node;
        _currentBytes += bytes;

        EvictIfNeeded();
    }

    private void EvictIfNeeded()
    {
        if (MaxBytes <= 0)
        {
            Clear();
            return;
        }

        while (_currentBytes > MaxBytes && _lru.Last != null)
        {
            var last = _lru.Last;
            _lru.RemoveLast();
            if (_map.TryGetValue(last.Value.Key, out var mapped) && ReferenceEquals(mapped, last))
            {
                _map.Remove(last.Value.Key);
            }
            _currentBytes -= last.Value.Bytes;

            // Hand the surface back to the shared scratch pool instead of destroying its DIB outright.
            _surfacePool.Return(last.Value.Surface);
        }
    }

    public void Clear()
    {
        var node = _lru.First;
        while (node != null)
        {
            _surfacePool.Return(node.Value.Surface);
            node = node.Next;
        }

        _lru.Clear();
        _map.Clear();
        _currentBytes = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }

    private readonly record struct CacheEntry(TextCacheKey Key, string Text, AaSurface Surface, long Bytes);
}
