using System.Reflection;

using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Backend-agnostic encoded image source (PNG/JPG/BMP).
///
/// For built-in backends, decoding is shared and cached so that:
/// - rendering can create backend images from a single decoded pixel buffer, and
/// - controls can sample pixels (e.g. <c>Image.TryPeekColor</c>) without re-decoding.
///
/// If the built-in decoder cannot decode the payload, creation falls back to
/// <see cref="IGraphicsFactory.CreateImageFromBytes(byte[])"/> so custom factories can handle additional formats.
/// </summary>
public sealed class ImageSource : IImageSource
{
    public byte[] Data { get; }

    /// <summary>
    /// Best-effort detected format id from registered decoders (diagnostics only).
    /// </summary>
    public string? FormatId => ImageDecoders.DetectFormatId(Data);

    private readonly object _decodeLock = new();
    private DecodedBitmap _decodedBitmap;
    private bool _decodedValid;
    private StaticPixelBufferSource? _decodedPixelSource;

    private ImageSource(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    public static ImageSource FromBytes(byte[] data) => new(data);

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

    public static bool TryFromResource<TAnchor>(string resourceName, out ImageSource? source) =>
        TryFromResource(typeof(TAnchor).Assembly, resourceName, out source);

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

    internal bool TryGetDecodedBitmap(out DecodedBitmap bitmap)
    {
        if (_decodedValid)
        {
            bitmap = _decodedBitmap;
            return true;
        }

        bitmap = default;
        return false;
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

            if (!ImageDecoders.TryDecode(Data, out var decoded))
            {
                pixelSource = null!;
                return false;
            }

            _decodedBitmap = decoded;
            _decodedValid = true;
            _decodedPixelSource = new StaticPixelBufferSource(decoded.WidthPx, decoded.HeightPx, decoded.Data);
            pixelSource = _decodedPixelSource;
            return true;
        }
    }

    public IImage CreateImage(IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // Prefer the decoded pixel path so rendering and sampling share the same decode work and buffer.
        // Fall back to the factory's byte-based creation so custom factories can handle formats not supported
        // by the built-in decoders.
        if (factory.Backend != GraphicsBackend.Custom && TryEnsureDecoded(out var pixels))
        {
            return factory.CreateImageFromPixelSource(pixels);
        }

        return factory.CreateImageFromBytes(Data);
    }
}
