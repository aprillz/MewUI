using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal readonly record struct OpenGLTextCacheKey(
    int TextHash,
    nint FontHandle,
    string FontId,
    int FontSizePx,
    uint ColorArgb,
    int WidthPx,
    int HeightPx,
    int HAlign,
    int VAlign,
    int Wrapping
);

internal readonly record struct OpenGLTextureEntry(uint TextureId, int WidthPx, int HeightPx);

internal sealed class OpenGLTextCache : IDisposable
{
    // Resizing can generate many unique (text,width,height) combinations. Without eviction this can balloon
    // GPU memory / committed memory quickly (often observed as Private Bytes spikes).
    private const long DefaultMaxBytes = 16L * 1024 * 1024; // 128 MiB

    private readonly Dictionary<OpenGLTextCacheKey, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private long _currentBytes;
    private bool _disposed;

    public long MaxBytes
    {
        get;
        set => field = Math.Max(0, value);
    } = DefaultMaxBytes;

    public bool TryGet(
        bool supportsBgra,
        nint hdc,
        OpenGLTextCacheKey key,
        out OpenGLTextureEntry entry)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OpenGLTextCache));
        }

        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            entry = node.Value.Entry;

            return true;
        }

        entry = default;
        return false;
    }

    public OpenGLTextureEntry CreateTexture(
        bool supportsBgra,
        nint hdc,
        OpenGLTextCacheKey key,
        ref TextBitmap bmp)
    {
        uint tex = UploadTexture(supportsBgra, bmp);
        var entry = new OpenGLTextureEntry(tex, bmp.WidthPx, bmp.HeightPx);

        long bytes = EstimateBytes(bmp.WidthPx, bmp.HeightPx);
        var newNode = new LinkedListNode<CacheEntry>(new CacheEntry(key, entry, bytes));
        _lru.AddFirst(newNode);
        _map[key] = newNode;
        _currentBytes += bytes;

        EvictIfNeeded();
        return entry;
    }

    private static uint UploadTexture(bool supportsBgra, TextBitmap bmp)
    {
        GL.GenTextures(1, out uint tex);
        GL.BindTexture(GL.GL_TEXTURE_2D, tex);
        // Keep text crisp. We render at device-pixel size, so nearest avoids blur.
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_NEAREST);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_NEAREST);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, (int)GL.GL_CLAMP_TO_EDGE);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, (int)GL.GL_CLAMP_TO_EDGE);

        var data = bmp.Data;
        uint format = supportsBgra ? GL.GL_BGRA_EXT : GL.GL_RGBA;

        // If BGRA is not supported, we convert.
        if (!supportsBgra)
        {
            data = OpenGLPixelUtils.ConvertBgraToRgba(data);
        }

        unsafe
        {
            fixed (byte* p = data)
            {
                GL.TexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, bmp.WidthPx, bmp.HeightPx, 0,
                    format, GL.GL_UNSIGNED_BYTE, (nint)p);
            }
        }

        return tex;
    }

    private static long EstimateBytes(int widthPx, int heightPx)
    {
        if (widthPx <= 0 || heightPx <= 0)
        {
            return 0;
        }

        return (long)widthPx * heightPx * 4;
    }

    private void EvictIfNeeded()
    {
        if (MaxBytes <= 0)
        {
            Clear();
            return;
        }

        while (_currentBytes > MaxBytes && _lru.Last is { } last)
        {
            _lru.RemoveLast();
            _map.Remove(last.Value.Key);

            uint tex = last.Value.Entry.TextureId;
            if (tex != 0)
            {
                GL.DeleteTextures(1, ref tex);
            }

            _currentBytes -= last.Value.Bytes;
        }
    }

    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        var node = _lru.First;
        while (node != null)
        {
            uint tex = node.Value.Entry.TextureId;
            if (tex != 0)
            {
                GL.DeleteTextures(1, ref tex);
            }

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

        _disposed = true;

        Clear();
    }

    private readonly record struct CacheEntry(OpenGLTextCacheKey Key, OpenGLTextureEntry Entry, long Bytes);
}
