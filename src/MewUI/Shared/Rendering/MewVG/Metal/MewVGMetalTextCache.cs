using Aprillz.MewUI.Rendering.CoreText;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGMetalTextCache : IDisposable
{
    private readonly NanoVGMetal _vg;
    private readonly Dictionary<TextCacheKey, CacheEntry> _cache = new();
    private readonly LinkedList<TextCacheKey> _lru = new();
    private readonly Dictionary<TextCacheKey, LinkedListNode<TextCacheKey>> _lruNodes = new();
    private bool _disposed;

    // Keep it conservative; text is the hottest path and Metal textures can accumulate quickly.
    private const int MaxEntries = 512;

    private sealed record CacheEntry(int ImageId, int WidthPx, int HeightPx);

    internal readonly record struct TextCacheKey(
        string Text,
        nint FontRef,
        uint ColorArgb,
        int WidthPx,
        int HeightPx,
        TextAlignment HorizontalAlignment,
        TextAlignment VerticalAlignment,
        TextWrapping Wrapping)
    {
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)ColorArgb;
                hash = (hash * 397) ^ FontRef.GetHashCode();
                hash = (hash * 397) ^ WidthPx;
                hash = (hash * 397) ^ HeightPx;
                hash = (hash * 397) ^ (int)HorizontalAlignment;
                hash = (hash * 397) ^ (int)VerticalAlignment;
                hash = (hash * 397) ^ (int)Wrapping;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Text);
                return hash;
            }
        }
    }

    public MewVGMetalTextCache(NanoVGMetal vg)
    {
        _vg = vg;
    }

    public bool TryGetOrCreate(
        CoreTextFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        uint dpi,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        out int imageId)
    {
        imageId = 0;

        var fontRef = font.GetFontRef(dpi);
        if (_disposed || fontRef == 0 || text.IsEmpty)
        {
            return false;
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        string s = text.ToString();
        uint argb = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

        var key = new TextCacheKey(
            s,
            fontRef,
            argb,
            widthPx,
            heightPx,
            horizontalAlignment,
            verticalAlignment,
            wrapping);

        if (_cache.TryGetValue(key, out var entry))
        {
            imageId = entry.ImageId;
            Touch(key);
            return imageId != 0;
        }

        var bmp = CoreTextText.Rasterize(font, text, widthPx, heightPx, dpi, color, horizontalAlignment, verticalAlignment, wrapping, widthPx);
        if (bmp.WidthPx <= 0 || bmp.HeightPx <= 0 || bmp.Data.Length == 0)
        {
            return false;
        }

        // CoreText produces BGRA premultiplied; NanoVG expects RGBA, and needs the Premultiplied flag to avoid double-premultiply.
        byte[] rgba = ImagePixelUtils.ConvertBgraToRgba(bmp.Data);
        imageId = _vg.CreateImageRGBA(bmp.WidthPx, bmp.HeightPx, NVGimageFlags.Premultiplied, rgba);
        if (imageId == 0)
        {
            return false;
        }

        Add(key, new CacheEntry(imageId, bmp.WidthPx, bmp.HeightPx));
        return true;
    }

    private void Add(TextCacheKey key, CacheEntry entry)
    {
        _cache[key] = entry;
        var node = _lru.AddLast(key);
        _lruNodes[key] = node;
        EvictIfNeeded();
    }

    private void Touch(TextCacheKey key)
    {
        if (_lruNodes.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddLast(node);
        }
    }

    private void EvictIfNeeded()
    {
        while (_cache.Count > MaxEntries && _lru.First != null)
        {
            var victimKey = _lru.First.Value;
            _lru.RemoveFirst();
            _lruNodes.Remove(victimKey);

            if (_cache.Remove(victimKey, out var entry))
            {
                if (entry.ImageId != 0)
                {
                    _vg.DeleteImage(entry.ImageId);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var entry in _cache.Values)
        {
            if (entry.ImageId != 0)
            {
                _vg.DeleteImage(entry.ImageId);
            }
        }

        _cache.Clear();
        _lru.Clear();
        _lruNodes.Clear();
    }
}
