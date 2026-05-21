using System.Reflection;

using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Backend-agnostic image source. Accepts either encoded image bytes (PNG/JPG/BMP) or
/// pre-decoded BGRA pixel buffers.
///
/// For built-in backends, decoded pixels are shared and cached so rendering and pixel
/// sampling (e.g. <c>Image.TryPeekColor</c>) reuse a single buffer. After a successful
/// decode the encoded bytes are released — see <see cref="EncodedBytes"/>.
///
/// If the built-in decoders cannot decode the payload, creation falls back to
/// <see cref="IGraphicsFactory.CreateImageFromBytes(byte[])"/> so custom factories can
/// handle additional formats. Sources created from raw pixels skip the decoder path entirely.
/// </summary>
public sealed class ImageSource : IImageSource
{
    private readonly object _decodeLock = new();
    private byte[]? _encoded;
    private string? _cachedFormatId;
    private bool _formatIdComputed;
    private Bgra32PixelBuffer _decodedBitmap;
    private bool _decodedValid;
    private StaticPixelBufferSource? _decodedPixelSource;

    private ImageSource(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        _encoded = encoded;
    }

    private ImageSource(Bgra32PixelBuffer pixels)
    {
        if (pixels.WidthPx <= 0 || pixels.HeightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), "Buffer dimensions must be positive.");
        }
        if (pixels.Data is null || pixels.Data.Length != pixels.WidthPx * pixels.HeightPx * 4)
        {
            throw new ArgumentException("Invalid BGRA buffer length.", nameof(pixels));
        }

        _decodedBitmap = pixels;
        _decodedValid = true;
    }

    /// <summary>
    /// Gets the encoded image payload. Empty once the source has been successfully
    /// decoded — the encoded buffer is released to reclaim memory. Raw-pixel sources
    /// never carry encoded data.
    /// </summary>
    internal ReadOnlyMemory<byte> EncodedBytes => _encoded ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Best-effort detected format id from registered decoders (diagnostics only).
    /// Cached after first access — survives encoded-bytes release.
    /// </summary>
    public string? FormatId
    {
        get
        {
            if (!_formatIdComputed)
            {
                _cachedFormatId = _encoded is null ? null : ImageDecoders.DetectFormatId(_encoded);
                _formatIdComputed = true;
            }
            return _cachedFormatId;
        }
    }

    /// <summary>Pixel width. Returns 0 when neither decoded pixels nor encoded bytes are available.</summary>
    public int PixelWidth => _decodedValid ? _decodedBitmap.WidthPx : 0;

    /// <summary>Pixel height. Returns 0 when neither decoded pixels nor encoded bytes are available.</summary>
    public int PixelHeight => _decodedValid ? _decodedBitmap.HeightPx : 0;

    /// <summary>Whether the source carries a meaningful alpha channel.</summary>
    public bool HasAlpha => !_decodedValid || _decodedBitmap.HasAlpha;

    /// <summary>
    /// Creates an <see cref="ImageSource"/> from encoded image bytes.
    /// </summary>
    public static ImageSource FromBytes(byte[] data) => new(data);

    /// <summary>
    /// Loads an <see cref="ImageSource"/> from a file path.
    /// </summary>
    /// <param name="path">Path to an encoded image file.</param>
    public static ImageSource FromFile(string path) => new(File.ReadAllBytes(path));

    /// <summary>
    /// Loads an embedded resource from the specified assembly.
    /// AOT-friendly: avoids reflection-based discovery; the caller provides the assembly + name.
    /// </summary>
    public static ImageSource FromResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Resource name is required.", nameof(resourceName));
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new FileNotFoundException($"Embedded resource not found: '{resourceName}'", resourceName);
        }

        return FromStream(stream);
    }

    /// <summary>
    /// Loads an embedded resource using an anchor type's assembly (recommended for AOT).
    /// </summary>
    public static ImageSource FromResource<TAnchor>(string resourceName) =>
        FromResource(typeof(TAnchor).Assembly, resourceName);

    /// <summary>
    /// Attempts to load an embedded resource from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly that contains the resource.</param>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="source">The created image source on success.</param>
    /// <returns><see langword="true"/> if the resource was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryFromResource(Assembly assembly, string resourceName, out ImageSource? source)
    {
        source = null;
        if (assembly == null || string.IsNullOrWhiteSpace(resourceName))
        {
            return false;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return false;
        }

        source = FromStream(stream);
        return true;
    }

    /// <summary>
    /// Attempts to load an embedded resource using an anchor type's assembly (recommended for AOT).
    /// </summary>
    /// <typeparam name="TAnchor">An anchor type in the assembly that contains the resource.</typeparam>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="source">The created image source on success.</param>
    /// <returns><see langword="true"/> if the resource was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryFromResource<TAnchor>(string resourceName, out ImageSource? source) =>
        TryFromResource(typeof(TAnchor).Assembly, resourceName, out source);

    /// <summary>
    /// Wraps a pre-decoded BGRA32 buffer. The array is referenced (not copied) — caller must
    /// not mutate after handing it over.
    /// </summary>
    public static ImageSource FromBgraPixels(int width, int height, byte[] bgra, bool hasAlpha = true)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        return new(new Bgra32PixelBuffer(width, height, bgra, hasAlpha));
    }

    /// <summary>
    /// Copies BGRA32 pixels into a new buffer.
    /// </summary>
    public static ImageSource FromBgraPixels(int width, int height, ReadOnlySpan<byte> bgra, bool hasAlpha = true)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be positive.");
        }
        int expected = checked(width * height * 4);
        if (bgra.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} bytes (tight-packed BGRA32), got {bgra.Length}.", nameof(bgra));
        }
        var copy = GC.AllocateUninitializedArray<byte>(expected);
        bgra.CopyTo(copy);
        return new(new Bgra32PixelBuffer(width, height, copy, hasAlpha));
    }

    /// <summary>
    /// Wraps an existing <see cref="Bgra32PixelBuffer"/>. The buffer's array is referenced (not copied).
    /// </summary>
    public static ImageSource FromBgraPixels(Bgra32PixelBuffer buffer) => new(buffer);

    /// <summary>
    /// Copies the decoded pixels into the caller-provided destination buffer in tight-packed BGRA32 order.
    /// Triggers decode on first access if needed.
    /// </summary>
    /// <param name="destination">Destination buffer to receive the pixels.</param>
    /// <param name="strideBytes">Destination stride in bytes per row. Must be at least <c>PixelWidth*4</c>.</param>
    public void CopyPixels(Span<byte> destination, int strideBytes)
    {
        if (!_decodedValid)
        {
            TryEnsureDecoded(out _);
        }
        if (!_decodedValid)
        {
            throw new InvalidOperationException("No decoded pixel data available.");
        }

        int width = _decodedBitmap.WidthPx;
        int height = _decodedBitmap.HeightPx;
        int rowBytes = width * 4;
        if (strideBytes < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(strideBytes));
        }
        int needed = checked((height - 1) * strideBytes + rowBytes);
        if (destination.Length < needed)
        {
            throw new ArgumentException("Destination buffer is too small for the specified stride.", nameof(destination));
        }

        var src = _decodedBitmap.Data.AsSpan();
        int srcOffset = 0;
        int dstOffset = 0;
        for (int y = 0; y < height; y++)
        {
            src.Slice(srcOffset, rowBytes).CopyTo(destination.Slice(dstOffset, rowBytes));
            srcOffset += rowBytes;
            dstOffset += strideBytes;
        }
    }

    private static ImageSource FromStream(Stream stream)
    {
        if (stream.CanSeek)
        {
            long len64 = stream.Length;
            if (len64 > int.MaxValue)
            {
                throw new NotSupportedException("Embedded resource is too large.");
            }

            int len = (int)len64;
            var data = GC.AllocateUninitializedArray<byte>(len);
            stream.Position = 0;
            stream.ReadExactly(data);
            return new ImageSource(data);
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return new ImageSource(ms.ToArray());
    }

    internal bool TryGetBgra32PixelBuffer(out Bgra32PixelBuffer bitmap)
    {
        if (_decodedValid)
        {
            bitmap = _decodedBitmap;
            return true;
        }

        bitmap = default;
        return false;
    }

    public void EnsureDecode()
    {
        TryEnsureDecoded(out _);
    }

    private bool TryEnsureDecoded(out StaticPixelBufferSource pixelSource)
    {
        lock (_decodeLock)
        {
            if (_decodedValid && _decodedPixelSource != null)
            {
                pixelSource = _decodedPixelSource;
                return true;
            }

            if (_decodedValid)
            {
                // Raw-pixel source — wrap the existing buffer without invoking the decoder.
                _decodedPixelSource = new StaticPixelBufferSource(
                    _decodedBitmap.WidthPx, _decodedBitmap.HeightPx, _decodedBitmap.Data, _decodedBitmap.HasAlpha);
                pixelSource = _decodedPixelSource;
                return true;
            }

            if (_encoded is null || !ImageDecoders.TryDecode(_encoded, out var decoded))
            {
                pixelSource = null!;
                return false;
            }

            _decodedBitmap = decoded;
            _decodedValid = true;
            _decodedPixelSource = new StaticPixelBufferSource(decoded.WidthPx, decoded.HeightPx, decoded.Data, decoded.HasAlpha);
            pixelSource = _decodedPixelSource;
            // Cache FormatId before releasing encoded bytes so it remains available.
            if (!_formatIdComputed)
            {
                _cachedFormatId = ImageDecoders.DetectFormatId(_encoded);
                _formatIdComputed = true;
            }
            _encoded = null;
            return true;
        }
    }

    /// <summary>
    /// Creates a backend image for rendering.
    /// </summary>
    /// <param name="factory">The graphics factory used to create backend resources.</param>
    public IImage CreateImage(IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // Prefer the decoded pixel path so rendering and sampling share the same decode work and buffer.
        // Fall back to the factory's byte-based creation so custom factories can handle formats not supported
        // by the built-in decoders or don't support pixel sources.
        if (TryEnsureDecoded(out var pixels))
        {
            try
            {
                return factory.CreateImageView(pixels);
            }
            catch (NotSupportedException)
            {
                // Fall through to byte-based creation.
            }
        }

        if (_encoded is null)
        {
            throw new InvalidOperationException("Cannot create image: decode failed and no encoded bytes available.");
        }
        return factory.CreateImageFromBytes(_encoded);
    }
}
