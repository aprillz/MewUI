namespace Aprillz.MewUI.Resources;

public static class ImageDecoders
{
    private static readonly object _lock = new();
    private static readonly Dictionary<ImageFormat, IImageDecoder> _decoders = new();

    static ImageDecoders()
    {
        Register(new BmpDecoder());
        Register(new PngDecoder());
    }

    public static void Register(IImageDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        if (decoder.Format == ImageFormat.Unknown)
        {
            throw new ArgumentException("Decoder must specify a format.", nameof(decoder));
        }

        lock (_lock)
        {
            _decoders[decoder.Format] = decoder;
        }
    }

    public static bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap)
    {
        var format = DetectFormat(encoded);
        if (format == ImageFormat.Unknown)
        {
            DiagLog.Write("ImageDecoders: Unknown format.");
            bitmap = default;
            return false;
        }

        IImageDecoder? decoder;
        lock (_lock)
        {
            _decoders.TryGetValue(format, out decoder);
        }

        if (decoder == null)
        {
            DiagLog.Write($"ImageDecoders: No decoder registered for {format}.");
            bitmap = default;
            return false;
        }

        var ok = decoder.TryDecode(encoded, out bitmap);
        if (!ok)
        {
            DiagLog.Write($"ImageDecoders: Decode failed for {format} (length={encoded.Length}).");
        }

        return ok;
    }

    public static bool TryDecode(byte[] encoded, out DecodedBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var format = DetectFormat(encoded);
        if (format == ImageFormat.Unknown)
        {
            DiagLog.Write("ImageDecoders: Unknown format.");
            bitmap = default;
            return false;
        }

        IImageDecoder? decoder;
        lock (_lock)
        {
            _decoders.TryGetValue(format, out decoder);
        }

        if (decoder == null)
        {
            DiagLog.Write($"ImageDecoders: No decoder registered for {format}.");
            bitmap = default;
            return false;
        }

        bool ok;
        if (decoder is IByteArrayImageDecoder fast)
        {
            ok = fast.TryDecode(encoded, out bitmap);
        }
        else
        {
            ok = decoder.TryDecode(encoded, out bitmap);
        }

        if (!ok)
        {
            DiagLog.Write($"ImageDecoders: Decode failed for {format} (length={encoded.Length}).");
        }

        return ok;
    }

    public static ImageFormat DetectFormat(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length >= 2 && encoded[0] == (byte)'B' && encoded[1] == (byte)'M')
        {
            return ImageFormat.Bmp;
        }

        if (encoded.Length >= 8 &&
            encoded[0] == 0x89 && encoded[1] == (byte)'P' && encoded[2] == (byte)'N' && encoded[3] == (byte)'G' &&
            encoded[4] == 0x0D && encoded[5] == 0x0A && encoded[6] == 0x1A && encoded[7] == 0x0A)
        {
            return ImageFormat.Png;
        }

        if (encoded.Length >= 3 && encoded[0] == 0xFF && encoded[1] == 0xD8 && encoded[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        // SVG can start with "<svg" or an XML header; detection is intentionally conservative.
        if (encoded.Length >= 4 && encoded[0] == (byte)'<' && (encoded[1] == (byte)'s' || encoded[1] == (byte)'?'))
        {
            return ImageFormat.Svg;
        }

        return ImageFormat.Unknown;
    }
}
