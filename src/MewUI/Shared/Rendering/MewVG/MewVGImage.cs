using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Resources;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGImage : IImage
{
    private readonly IPixelBufferSource? _source;
    private int _sourceVersion = -1;
    private byte[]? _rgbaCache;
    private int _rgbaCacheVersion = -1;
    private readonly Dictionary<ImageKey, ImageEntry> _images = new();
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    private readonly record struct ImageEntry(int ImageId, int Version);
    private readonly record struct ImageKey(NanoVG Vg, NVGimageFlags Flags);

    public MewVGImage(int widthPx, int heightPx, byte[] bgra)
    {
        PixelWidth = widthPx;
        PixelHeight = heightPx;
        ArgumentNullException.ThrowIfNull(bgra);
        _source = new StaticPixelBufferSource(widthPx, heightPx, bgra);
        _sourceVersion = 0;
    }

    public MewVGImage(IPixelBufferSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {source.PixelFormat}");
        }

        PixelWidth = source.PixelWidth;
        PixelHeight = source.PixelHeight;
        _source = source;
        _sourceVersion = source.Version;
    }

    public int GetOrCreateImageId(NanoVG vg)
        => GetOrCreateImageId(vg, NVGimageFlags.None);

    public int GetOrCreateImageId(NanoVG vg, NVGimageFlags flags)
    {
        if (_disposed)
        {
            return 0;
        }

        int version = _source?.Version ?? 0;
        if (_sourceVersion != version)
        {
            _sourceVersion = version;
            _rgbaCache = null;
            _rgbaCacheVersion = -1;
            // Drop cached images for all flags on version change.
            if (_images.Count != 0)
            {
                var keys = _images.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    if (ReferenceEquals(key.Vg, vg))
                    {
                        int cachedId = _images[key].ImageId;
                        if (cachedId != 0)
                        {
                            vg.DeleteImage(cachedId);
                        }
                        _images.Remove(key);
                    }
                }
            }
        }

        var imageKey = new ImageKey(vg, flags);
        if (_images.TryGetValue(imageKey, out var entry) && entry.ImageId != 0 && entry.Version == version)
        {
            return entry.ImageId;
        }

        if (entry.ImageId != 0)
        {
            vg.DeleteImage(entry.ImageId);
        }

        byte[] rgba = GetRgba(version);
        int imageId = vg.CreateImageRGBA(PixelWidth, PixelHeight, flags, rgba);
        _images[imageKey] = new ImageEntry(imageId, version);
        return imageId;
    }

    private byte[] GetRgba(int version)
    {
        if (_rgbaCache != null && _rgbaCacheVersion == version)
        {
            return _rgbaCache;
        }

        if (_source == null)
        {
            return _rgbaCache ?? Array.Empty<byte>();
        }

        using var l = _source.Lock();
        byte[] bgra = l.Buffer;
        _rgbaCache = ImagePixelUtils.ConvertBgraToRgba(bgra);
        _rgbaCacheVersion = version;
        return _rgbaCache;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var pair in _images)
        {
            int imageId = pair.Value.ImageId;
            if (imageId != 0)
            {
                pair.Key.Vg.DeleteImage(imageId);
            }
        }

        _images.Clear();
    }
}
