using System.Reflection;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI;

/// <summary>
/// Backend-agnostic encoded image source (PNG/JPG/BMP/SVG).
/// Decode/upload is performed by the active graphics backend.
/// </summary>
public sealed class ImageSource
{
    public byte[] Data { get; }
    public ImageFormat Format { get; }

    private ImageSource(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        Format = ImageDecoders.DetectFormat(Data);
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
}
