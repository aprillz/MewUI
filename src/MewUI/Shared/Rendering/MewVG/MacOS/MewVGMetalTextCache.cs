using System.Runtime.CompilerServices;

using Aprillz.MewUI.Rendering.CoreText;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGMetalTextCache : IDisposable
{
    private readonly NanoVGMetal _vg;
    private readonly Dictionary<TextCacheKey, CacheEntry> _cache = new();
    private readonly LinkedList<TextCacheKey> _lru = new();
    private readonly Dictionary<TextCacheKey, LinkedListNode<TextCacheKey>> _lruNodes = new();
    private readonly Queue<int> _pendingDeletes = new();
    // Owner-keyed slot: one persistent (byte[], MTLTexture/imageId) pair per logical text source
    // (TextBlock instance, etc). Reused across renders even when the text content mutates,
    // so frequently-changing dynamic text (stats overlays, counters) doesn't allocate per
    // frame. ConditionalWeakTable lets the entry drop when the owner is GC'd; the stale
    // image-id is then leaked until the NVG context itself disposes - acceptable since
    // it's bounded by "ever-created TextBlock instances", not by render rate.
    private readonly ConditionalWeakTable<object, OwnerEntry> _ownerCache = new();
    private readonly List<OwnerRegistration> _ownerEntries = new();
    // Scratch entries for transient text, one per transient draw in a frame. A slot is only
    // repainted in place the frame after its last use, once ReleasePendingDeletes has reset the
    // index past the flush that consumed it.
    private readonly List<OwnerEntry> _transientSlots = new();
    private int _transientIndex;
    private bool _disposed;

    // Keep it conservative; text is the hottest path and Metal textures can accumulate quickly.
    private const int MaxEntries = 512;
    private const long DefaultMaxBytes = 16L * 1024 * 1024;

    public long MaxBytes
    {
        get;
        set => field = Math.Max(0, value);
    } = DefaultMaxBytes;

    // Text is kept so a hit can be confirmed: the key only carries the text's hash.
    private sealed record CacheEntry(int ImageId, int WidthPx, int HeightPx, string Text);

    private sealed class OwnerEntry
    {
        // Reused rasterization buffer. Sized to the largest text bitmap ever produced
        // for this owner (no shrink). New rasterizations fill the leading region only.
        public byte[]? Buffer;

        // Dimensions of the currently-allocated MTLTexture (NVG image). When the next
        // raster matches these, we reuse the texture via UpdateImageBGRA. When it differs,
        // we drop (queue for delete) and CreateImageBGRA at the new dims.
        public int TextureWidthPx;
        public int TextureHeightPx;
        public int ImageId;

        // Inputs of the last rasterization. Same-input calls reuse the texture without
        // re-rasterizing; same-frame calls with DIFFERENT inputs must not update the shared
        // texture in place (queued NVG draws sample it at encode time) and fall back to the
        // keyed cache instead.
        public long LastFrame = -1;
        public string? LastText;
        public nint LastFontRef;
        public uint LastArgb;
        public int LastWidthPx;
        public int LastHeightPx;
        public TextAlignment LastHorizontalAlignment;
        public TextAlignment LastVerticalAlignment;
        public TextWrapping LastWrapping;
        public TextTrimming LastTrimming;
        public long LastUse;
    }

    private sealed class OwnerRegistration(object owner, OwnerEntry entry)
    {
        public WeakReference<object> Owner { get; } = new(owner);
        public OwnerEntry Entry { get; } = entry;
    }

    private long _frameGeneration;
    private long _useStamp;

    internal readonly record struct TextCacheKey(
        int TextHash,
        nint FontRef,
        uint ColorArgb,
        int WidthPx,
        int HeightPx,
        TextAlignment HorizontalAlignment,
        TextAlignment VerticalAlignment,
        TextWrapping Wrapping,
        TextTrimming Trimming = TextTrimming.None)
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
                hash = (hash * 397) ^ (int)Trimming;
                hash = (hash * 397) ^ TextHash;
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
        TextTrimming trimming,
        out int imageId,
        out int bitmapWidthPx,
        out int bitmapHeightPx)
    {
        imageId = 0;
        bitmapWidthPx = widthPx;
        bitmapHeightPx = heightPx;

        var fontRef = font.GetFontRef(dpi);
        if (_disposed || fontRef == 0 || text.IsEmpty)
        {
            return false;
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        uint argb = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

        var key = new TextCacheKey(
            string.GetHashCode(text),
            fontRef,
            argb,
            widthPx,
            heightPx,
            horizontalAlignment,
            verticalAlignment,
            wrapping,
            trimming);

        if (_cache.TryGetValue(key, out var entry))
        {
            if (text.SequenceEqual(entry.Text))
            {
                imageId = entry.ImageId;
                bitmapWidthPx = entry.WidthPx;
                bitmapHeightPx = entry.HeightPx;
                Touch(key);
                return imageId != 0;
            }

            // Different text with the same hash: drop the entry so the new text takes the slot.
            Remove(key);
        }

        var bmp = CoreTextText.Rasterize(font, text, widthPx, heightPx, dpi, color, horizontalAlignment, verticalAlignment, wrapping, widthPx, trimming);
        if (bmp.WidthPx <= 0 || bmp.HeightPx <= 0 || bmp.Data.Length == 0)
        {
            return false;
        }

        // CoreText produces BGRA premultiplied. Hand it straight to NVG via the BGRA upload
        // path - Metal's BGRA8Unorm texture takes the bytes as-is. Premultiplied flag stays
        // so the shader doesn't double-multiply at sample.
        imageId = _vg.CreateImageBGRA(bmp.WidthPx, bmp.HeightPx, NVGimageFlags.Premultiplied, bmp.Data);
        if (imageId == 0)
        {
            return false;
        }

        bitmapWidthPx = bmp.WidthPx;
        bitmapHeightPx = bmp.HeightPx;
        Add(key, new CacheEntry(imageId, bmp.WidthPx, bmp.HeightPx, text.ToString()));
        return true;
    }

    // Texture bytes this cache has reported to RenderResourceMetrics, given back in full on Dispose.
    private long _accountedBytes;

    private void Account(long delta)
    {
        _accountedBytes += delta;
        RenderResourceMetrics.TextCacheBytesChanged(delta);
    }

    private static long TextureBytes(int widthPx, int heightPx) => (long)widthPx * heightPx * 4;

    private void Add(TextCacheKey key, CacheEntry entry)
    {
        Account(TextureBytes(entry.WidthPx, entry.HeightPx));
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

    private void EvictIfNeeded(OwnerEntry? keep = null)
    {
        while ((_cache.Count > MaxEntries || _accountedBytes > MaxBytes) && _lru.First != null)
        {
            Remove(_lru.First.Value);
        }

        if (_accountedBytes <= MaxBytes)
        {
            return;
        }

        var candidates = new List<OwnerRegistration>();
        for (int i = _ownerEntries.Count - 1; i >= 0; i--)
        {
            var registration = _ownerEntries[i];
            if (!registration.Owner.TryGetTarget(out _))
            {
                ReleaseOwnerRegistration(registration, removeOwnerKey: false);
            }
            else if (!ReferenceEquals(registration.Entry, keep))
            {
                candidates.Add(registration);
            }
        }

        candidates.Sort(static (left, right) => left.Entry.LastUse.CompareTo(right.Entry.LastUse));
        foreach (var candidate in candidates)
        {
            if (_accountedBytes <= MaxBytes)
            {
                break;
            }
            ReleaseOwnerRegistration(candidate, removeOwnerKey: true);
        }
    }

    private void Remove(TextCacheKey key)
    {
        if (_lruNodes.Remove(key, out var node))
        {
            _lru.Remove(node);
        }

        if (_cache.Remove(key, out var entry))
        {
            Account(-TextureBytes(entry.WidthPx, entry.HeightPx));
            if (entry.ImageId != 0)
            {
                // Defer deletion: removal happens during mid-frame text creation, but the main
                // NVG has already buffered draw calls referencing this imageId. Releasing it now
                // would leave the queued draws sampling a freed MTLTexture.
                _pendingDeletes.Enqueue(entry.ImageId);
            }
        }
    }

    /// <summary>
    /// Releases NVG image handles whose deletion was deferred by
    /// <see cref="EvictIfNeeded"/>. Must be called after the main NVG's flush.
    /// </summary>
    public void ReleasePendingDeletes()
    {
        if (_disposed) return;
        _frameGeneration++;
        _transientIndex = 0;
        SweepDeadOwners();
        while (_pendingDeletes.Count > 0)
        {
            int imageId = _pendingDeletes.Dequeue();
            if (imageId != 0) _vg.DeleteImage(imageId);
        }
    }

    /// <summary>
    /// Owner-keyed text rasterization: caches one (buffer, MTLTexture) pair per logical
    /// owner (typically the TextBlock instance) and reuses both even when the text content
    /// mutates. When the rasterized bitmap dimensions match the existing texture, the
    /// pixels are uploaded via <see cref="NanoVG.UpdateImageBGRA"/> - no new GPU allocation
    /// and no managed-heap byte[] allocation. When dimensions change, the old texture is
    /// queued for deferred deletion (same path as content-cache eviction) and a new one is
    /// created from the same (possibly grown) buffer.
    /// </summary>
    /// <remarks>
    /// Differs from the content-keyed <see cref="TryGetOrCreate"/> in that it never adds
    /// to <see cref="_cache"/> / <see cref="_lru"/> - owner entries are tracked exclusively
    /// in <see cref="_ownerCache"/>. This means stats overlays and other rapidly-mutating
    /// text never push the LRU into eviction churn.
    /// </remarks>
    public bool TryGetOrCreateOwned(
        object owner,
        CoreTextFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        uint dpi,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        TextTrimming trimming,
        out int imageId,
        out int bitmapWidthPx,
        out int bitmapHeightPx)
    {
        ArgumentNullException.ThrowIfNull(owner);

        imageId = 0;
        bitmapWidthPx = widthPx;
        bitmapHeightPx = heightPx;

        var fontRef = font.GetFontRef(dpi);
        if (_disposed || fontRef == 0 || text.IsEmpty)
        {
            return false;
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        if (!_ownerCache.TryGetValue(owner, out var entry))
        {
            entry = new OwnerEntry();
            _ownerCache.Add(owner, entry);
            _ownerEntries.Add(new OwnerRegistration(owner, entry));
        }

        uint ownedArgb = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        bool sameInputs = entry.ImageId != 0 &&
            entry.LastText is string lastText &&
            entry.LastFontRef == fontRef &&
            entry.LastArgb == ownedArgb &&
            entry.LastWidthPx == widthPx &&
            entry.LastHeightPx == heightPx &&
            entry.LastHorizontalAlignment == horizontalAlignment &&
            entry.LastVerticalAlignment == verticalAlignment &&
            entry.LastWrapping == wrapping &&
            entry.LastTrimming == trimming &&
            text.SequenceEqual(lastText);

        if (sameInputs)
        {
            // Identical inputs: the texture already holds these pixels; no re-rasterization needed.
            // The frame stamp must still advance, or a second owner draw this frame fails the
            // same-frame check below and repaints the texture this quad already queued against.
            entry.LastFrame = _frameGeneration;
            imageId = entry.ImageId;
            bitmapWidthPx = entry.TextureWidthPx;
            bitmapHeightPx = entry.TextureHeightPx;
            return true;
        }

        if (entry.ImageId != 0 && entry.LastFrame == _frameGeneration)
        {
            // A second draw of this owner within the same frame with different inputs (e.g. a
            // classifier span recolor over the base run). Updating the owner texture in place
            // would repaint every quad already queued this frame, so use the keyed cache.
            return TryGetOrCreate(
                font, text, widthPx, heightPx, dpi, color,
                horizontalAlignment, verticalAlignment, wrapping, trimming,
                out imageId, out bitmapWidthPx, out bitmapHeightPx);
        }

        if (!RasterizeIntoEntry(entry, font, text, widthPx, heightPx, dpi, color,
                horizontalAlignment, verticalAlignment, wrapping, trimming,
                out int actualW, out int actualH))
        {
            return false;
        }

        entry.LastFrame = _frameGeneration;
        entry.LastFontRef = fontRef;
        entry.LastArgb = ownedArgb;
        entry.LastWidthPx = widthPx;
        entry.LastHeightPx = heightPx;
        entry.LastHorizontalAlignment = horizontalAlignment;
        entry.LastVerticalAlignment = verticalAlignment;
        entry.LastWrapping = wrapping;
        entry.LastTrimming = trimming;
        entry.LastText = text.ToString();
        entry.LastUse = ++_useStamp;

        EvictIfNeeded(entry);

        imageId = entry.ImageId;
        bitmapWidthPx = actualW;
        bitmapHeightPx = actualH;
        EvictIfNeeded();
        return true;
    }

    /// <summary>
    /// Transient text rasterization: paints into the next scratch texture of this frame, which is
    /// neither keyed nor tied to an owner and is reused by a later frame's transient draws.
    /// </summary>
    public bool TryGetOrCreateTransient(
        CoreTextFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        uint dpi,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        TextTrimming trimming,
        out int imageId,
        out int bitmapWidthPx,
        out int bitmapHeightPx)
    {
        imageId = 0;
        bitmapWidthPx = widthPx;
        bitmapHeightPx = heightPx;

        if (_disposed || font.GetFontRef(dpi) == 0 || text.IsEmpty)
        {
            return false;
        }

        if (_transientIndex >= _transientSlots.Count)
        {
            _transientSlots.Add(new OwnerEntry());
        }

        var entry = _transientSlots[_transientIndex++];
        if (!RasterizeIntoEntry(entry, font, text, Math.Max(1, widthPx), Math.Max(1, heightPx), dpi, color,
                horizontalAlignment, verticalAlignment, wrapping, trimming,
                out int actualW, out int actualH))
        {
            return false;
        }

        imageId = entry.ImageId;
        bitmapWidthPx = actualW;
        bitmapHeightPx = actualH;
        return true;
    }

    // Rasterizes into the entry's buffer and uploads it: in place when the bitmap size is
    // unchanged, otherwise into a new texture with the old one deleted deferred.
    private bool RasterizeIntoEntry(
        OwnerEntry entry,
        CoreTextFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        uint dpi,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        TextTrimming trimming,
        out int actualW,
        out int actualH)
    {
        // The rasterized bitmap is widthPx + aaExtra × heightPx (matches CoreTextText.Rasterize).
        int aaExtra = (int)Math.Ceiling(dpi / 96.0 * 2);
        int aaWidthPx = checked(widthPx + aaExtra);
        int requiredBytes = checked(aaWidthPx * heightPx * 4);

        // Grow buffer if needed. No shrink - rare large rasterization shouldn't force
        // reallocation on every subsequent small one.
        if (entry.Buffer == null || entry.Buffer.Length < requiredBytes)
        {
            int previousBytes = entry.Buffer?.Length ?? 0;
            entry.Buffer = new byte[requiredBytes];
            Account(requiredBytes - previousBytes);
        }

        if (!CoreTextText.RasterizeInto(
                font, text, widthPx, heightPx, dpi, color,
                horizontalAlignment, verticalAlignment,
                wrapping, widthPx, trimming,
                entry.Buffer,
                out actualW, out actualH))
        {
            return false;
        }

        // The leading actualW * actualH * 4 bytes of entry.Buffer hold valid BGRA premul pixels.
        var pixels = entry.Buffer.AsSpan(0, checked(actualW * actualH * 4));

        if (entry.ImageId != 0 && entry.TextureWidthPx == actualW && entry.TextureHeightPx == actualH)
        {
            _vg.UpdateImageBGRA(entry.ImageId, pixels);
        }
        else
        {
            if (entry.ImageId != 0)
            {
                _pendingDeletes.Enqueue(entry.ImageId);
                entry.ImageId = 0;
                Account(-TextureBytes(entry.TextureWidthPx, entry.TextureHeightPx));
            }

            int newId = _vg.CreateImageBGRA(actualW, actualH, NVGimageFlags.Premultiplied, pixels);
            if (newId == 0)
            {
                return false;
            }

            entry.ImageId = newId;
            entry.TextureWidthPx = actualW;
            entry.TextureHeightPx = actualH;
            Account(TextureBytes(actualW, actualH));
        }

        return true;
    }

    /// <summary>
    /// Releases the owner's cached buffer and queues its NVG image for deferred deletion.
    /// Call from the owner's <c>OnDispose</c> to reclaim GPU memory eagerly without waiting
    /// for the GC to reclaim the owner. Safe to call multiple times.
    /// </summary>
    public void ReleaseOwner(object owner)
    {
        if (_disposed || owner == null) return;
        if (_ownerCache.TryGetValue(owner, out var entry))
        {
            var registration = _ownerEntries.FirstOrDefault(value => ReferenceEquals(value.Entry, entry));
            if (registration != null)
            {
                ReleaseOwnerRegistration(registration, removeOwnerKey: true);
            }
        }
    }

    private void SweepDeadOwners()
    {
        for (int i = _ownerEntries.Count - 1; i >= 0; i--)
        {
            var registration = _ownerEntries[i];
            if (!registration.Owner.TryGetTarget(out _))
            {
                ReleaseOwnerRegistration(registration, removeOwnerKey: false);
            }
        }
    }

    private void ReleaseOwnerRegistration(OwnerRegistration registration, bool removeOwnerKey)
    {
        var entry = registration.Entry;
        long bytes = entry.Buffer?.LongLength ?? 0;
        if (entry.ImageId != 0)
        {
            _pendingDeletes.Enqueue(entry.ImageId);
            bytes += TextureBytes(entry.TextureWidthPx, entry.TextureHeightPx);
            entry.ImageId = 0;
        }

        entry.Buffer = null;
        entry.TextureWidthPx = 0;
        entry.TextureHeightPx = 0;
        if (bytes != 0)
        {
            Account(-bytes);
        }

        if (removeOwnerKey && registration.Owner.TryGetTarget(out var owner))
        {
            _ownerCache.Remove(owner);
        }
        _ownerEntries.Remove(registration);
    }

    public void Trim()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var entry in _cache.Values)
        {
            if (entry.ImageId != 0)
            {
                _pendingDeletes.Enqueue(entry.ImageId);
            }
        }
        _cache.Clear();
        _lru.Clear();
        _lruNodes.Clear();

        foreach (var registration in _ownerEntries)
        {
            var entry = registration.Entry;
            if (entry.ImageId != 0)
            {
                _pendingDeletes.Enqueue(entry.ImageId);
                entry.ImageId = 0;
            }
            entry.Buffer = null;
        }
        _ownerEntries.Clear();
        _ownerCache.Clear();

        foreach (var entry in _transientSlots)
        {
            if (entry.ImageId != 0)
            {
                _pendingDeletes.Enqueue(entry.ImageId);
                entry.ImageId = 0;
            }
            entry.Buffer = null;
        }
        _transientSlots.Clear();
        _transientIndex = 0;

        Account(-_accountedBytes);
        ReleasePendingDeletes();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Trim();
        _disposed = true;
    }
}
