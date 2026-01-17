namespace Aprillz.MewUI.Resources;

public static class ImageDecoders
{
    private static long _nextOrder;
    private static readonly object _lock = new();
    private static readonly List<Registration> _decoders = new();

    private const int DefaultPriority = 0;
    private const int FallbackPriority = -1000;

    /// <summary>
    /// Optional debug logger for decode failures and format detection.
    /// Keep null in production for zero overhead.
    /// </summary>
    public static Action<string>? DebugLog { get; set; }

    static ImageDecoders()
    {
        Register(new BmpDecoder());
        Register(new PngDecoder());
        Register(new JpegDecoder());
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

    public static bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap)
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

    public static bool TryDecode(byte[] encoded, out DecodedBitmap bitmap)
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
}
