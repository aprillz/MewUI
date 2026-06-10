namespace Aprillz.MewUI.Resources;

/// <summary>
/// Maintains a process-wide registry of image decoders used by <see cref="ImageSource"/>.
/// </summary>
public static class ImageDecoders
{
    private static long _nextOrder;
    private static readonly object _lock = new();
    private static readonly List<Registration> _decoders = new();

    private const int DefaultPriority = 0;
    private const int FallbackPriority = -1000;

    static ImageDecoders()
    {
        Register(new BmpDecoder());
        Register(new PngDecoder());
        Register(new JpegDecoder());
        Register(new IconDecoder());
    }

    private readonly record struct Registration(IImageDecoder Decoder, int Priority, long Order);

    /// <summary>
    /// Registers a decoder. Decoders are queried in registration order, with the most recently registered
    /// non-fallback decoders checked first. If multiple decoders report <see cref="IImageDecoder.CanDecode"/>
    /// for the same input, the first match wins.
    /// </summary>
    public static void Register(IImageDecoder decoder) => RegisterCore(decoder, DefaultPriority);

    /// <summary>
    /// Registers a fallback decoder. Fallback decoders are only tried after all regular decoders.
    /// Use this for "try anything" decoders that might match ambiguous inputs.
    /// </summary>
    public static void RegisterFallback(IImageDecoder decoder) => RegisterCore(decoder, FallbackPriority);

    private static void RegisterCore(IImageDecoder decoder, int priority)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentException.ThrowIfNullOrWhiteSpace(decoder.Id);

        lock (_lock)
        {
            // Remove any existing decoder with the same Id (treat Id as the uniqueness key).
            for (int i = _decoders.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_decoders[i].Decoder.Id, decoder.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _decoders.RemoveAt(i);
                }
            }

            long order = Interlocked.Increment(ref _nextOrder);
            _decoders.Add(new Registration(decoder, priority, order));
            _decoders.Sort(static (a, b) =>
            {
                int cmp = b.Priority.CompareTo(a.Priority);
                if (cmp != 0)
                {
                    return cmp;
                }

                // Later registrations should win ties.
                return b.Order.CompareTo(a.Order);
            });
        }
    }

    /// <summary>
    /// Attempts to decode an encoded image span into a <see cref="Bgra32PixelBuffer"/>.
    /// </summary>
    /// <param name="encoded">Encoded image bytes.</param>
    /// <param name="bitmap">Decoded bitmap on success.</param>
    /// <returns><see langword="true"/> if a decoder matched and decoding succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> encoded, out Bgra32PixelBuffer bitmap)
    {
        IImageDecoder? decoder = null;
        lock (_lock)
        {
            for (int i = 0; i < _decoders.Count; i++)
            {
                var d = _decoders[i].Decoder;
                if (d.CanDecode(encoded))
                {
                    decoder = d;
                    break;
                }
            }
        }

        if (decoder == null)
        {
            DiagLog.Write("ImageDecoders: No decoder matched input.");
            bitmap = default;
            return false;
        }

        var ok = decoder.TryDecode(encoded, out bitmap);
        if (!ok)
        {
            DiagLog.Write($"ImageDecoders: Decode failed for '{decoder.Id}' (length={encoded.Length}).");
        }

        return ok;
    }

    /// <summary>
    /// Attempts to decode an encoded image byte array into a <see cref="Bgra32PixelBuffer"/>.
    /// </summary>
    /// <param name="encoded">Encoded image bytes.</param>
    /// <param name="bitmap">Decoded bitmap on success.</param>
    /// <returns><see langword="true"/> if a decoder matched and decoding succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryDecode(byte[] encoded, out Bgra32PixelBuffer bitmap)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        IImageDecoder? decoder = null;
        lock (_lock)
        {
            for (int i = 0; i < _decoders.Count; i++)
            {
                var d = _decoders[i].Decoder;
                if (d.CanDecode(encoded))
                {
                    decoder = d;
                    break;
                }
            }
        }

        if (decoder == null)
        {
            DiagLog.Write("ImageDecoders: No decoder matched input.");
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
            DiagLog.Write($"ImageDecoders: Decode failed for '{decoder.Id}' (length={encoded.Length}).");
        }

        return ok;
    }

    /// <summary>
    /// Attempts to read decoded image dimensions without materializing the pixel buffer.
    /// </summary>
    public static bool TryReadInfo(byte[] encoded, out int width, out int height, out bool hasAlpha)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var decoder = FindDecoder(encoded);
        if (decoder is not IByteArrayImageDecoder fast)
        {
            DiagLog.Write(decoder is null
                ? "ImageDecoders: No decoder matched input."
                : $"ImageDecoders: Decoder '{decoder.Id}' does not support fast info reads.");
            width = 0;
            height = 0;
            hasAlpha = false;
            return false;
        }

        bool ok = fast.TryReadInfo(encoded, out width, out height, out hasAlpha);
        if (!ok)
        {
            DiagLog.Write($"ImageDecoders: Read info failed for '{decoder.Id}' (length={encoded.Length}).");
        }

        return ok;
    }

    /// <summary>
    /// Attempts to decode an image directly into an existing BGRA32 destination buffer.
    /// </summary>
    public static bool TryDecodeInto(byte[] encoded, Span<byte> destination, out int width, out int height, out bool hasAlpha)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var decoder = FindDecoder(encoded);
        if (decoder is not IByteArrayImageDecoder fast)
        {
            DiagLog.Write(decoder is null
                ? "ImageDecoders: No decoder matched input."
                : $"ImageDecoders: Decoder '{decoder.Id}' does not support direct decode.");
            width = 0;
            height = 0;
            hasAlpha = false;
            return false;
        }

        bool ok = fast.TryDecodeInto(encoded, destination, out width, out height, out hasAlpha);
        if (!ok)
        {
            DiagLog.Write($"ImageDecoders: Direct decode failed for '{decoder.Id}' (length={encoded.Length}).");
        }

        return ok;
    }

    /// <summary>
    /// Attempts to identify the input format by probing registered decoders.
    /// This is intended for diagnostics only.
    /// </summary>
    public static string? DetectFormatId(ReadOnlySpan<byte> encoded)
    {
        lock (_lock)
        {
            for (int i = 0; i < _decoders.Count; i++)
            {
                var d = _decoders[i].Decoder;
                if (d.CanDecode(encoded))
                {
                    return d.Id;
                }
            }
        }

        return null;
    }

    private static IImageDecoder? FindDecoder(ReadOnlySpan<byte> encoded)
    {
        lock (_lock)
        {
            for (int i = 0; i < _decoders.Count; i++)
            {
                var d = _decoders[i].Decoder;
                if (d.CanDecode(encoded))
                {
                    return d;
                }
            }
        }

        return null;
    }
}
