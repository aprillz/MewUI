using System.Reflection;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Backend-agnostic image source. Accepts either encoded image bytes (PNG/JPG/BMP) or
/// pre-decoded BGRA pixel buffers.
///
/// For built-in backends, decoded pixels are shared and cached so rendering and pixel
/// sampling (e.g. <c>Image.TryPeekColor</c>) reuse a single buffer. After a successful
/// decode the encoded bytes are released when the full intrinsic variant is resident. Target-sized
/// variants retain their backing so a later, larger request can replace them.
///
/// Sources created from raw pixels skip the decoder path entirely.
/// </summary>
public sealed class ImageSource : IOrientedImageSource, IImageMetadataSource, IDecodedPixelCacheOwner, IDisposable
{
    private delegate bool EncodedImageDecoder(byte[] encoded, out Bgra32PixelBuffer bitmap, out ImageOrientation orientation);
    private delegate string? EncodedFormatDetector(ReadOnlySpan<byte> encoded);

    private readonly object _decodeLock = new();
    private readonly EncodedImageDecoder? _decoder;
    private readonly EncodedFormatDetector? _formatDetector;
    private readonly SourceMemoryAccounting _memoryAccounting;
    private byte[]? _encoded;
    private string? _cachedFormatId;
    private bool _formatIdComputed;
    private ImageMetadata _metadata;
    private bool _metadataComputed;
    private bool _metadataValid;
    private Bgra32PixelBuffer _decodedBitmap;
    private DecodedPixelOwner? _decodedOwner;
    private bool _decodedValid;
    private ImageOrientation _orientation = ImageOrientation.Identity;
    private StaticPixelBufferSource? _decodedPixelSource;
    private int _disposed;

    private ImageSource(
        byte[] encoded,
        EncodedImageDecoder decoder,
        EncodedFormatDetector? formatDetector = null,
        string? knownFormatId = null)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        _encoded = encoded;
        _memoryAccounting = new SourceMemoryAccounting(encoded.LongLength);
        _decoder = decoder;
        _formatDetector = formatDetector;
        if (knownFormatId != null)
        {
            _cachedFormatId = knownFormatId;
            _formatIdComputed = true;
        }
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
        _decodedOwner = new DecodedPixelOwner(pixels);
        _decodedValid = true;
        _memoryAccounting = new SourceMemoryAccounting(0);
        _metadata = new ImageMetadata(pixels.WidthPx, pixels.HeightPx, ImageOrientation.Identity, pixels.HasAlpha);
        _metadataComputed = true;
        _metadataValid = true;
    }

    /// <summary>
    /// Gets the encoded image payload. Empty once the full intrinsic variant has been decoded;
    /// target-sized variants retain the payload for a later upgrade. Raw-pixel sources never carry
    /// encoded data.
    /// </summary>
    internal ReadOnlyMemory<byte> EncodedBytes => _encoded ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Best-effort detected format id from registered decoders (diagnostics only).
    /// Cached after first access - survives encoded-bytes release.
    /// </summary>
    public string? FormatId
    {
        get
        {
            if (!_formatIdComputed)
            {
                _cachedFormatId = _encoded is null ? null : _formatDetector?.Invoke(_encoded);
                _formatIdComputed = true;
            }
            return _cachedFormatId;
        }
    }

    /// <summary>Intrinsic pixel width read from metadata without decoding pixels when supported.</summary>
    public int PixelWidth => TryGetMetadata(out var metadata) ? metadata.PixelWidth : 0;

    /// <summary>Intrinsic pixel height read from metadata without decoding pixels when supported.</summary>
    public int PixelHeight => TryGetMetadata(out var metadata) ? metadata.PixelHeight : 0;

    /// <summary>Whether the source carries a meaningful alpha channel.</summary>
    public bool HasAlpha => !TryGetMetadata(out var metadata) || metadata.HasAlpha;

    /// <summary>
    /// Orientation parsed from the source metadata without decoding pixels. Raw-pixel sources and formats
    /// without orientation metadata return <see cref="ImageOrientation.Identity"/>.
    /// </summary>
    public ImageOrientation Orientation => TryGetMetadata(out var metadata)
        ? metadata.Orientation
        : ImageOrientation.Identity;

    /// <summary>
    /// Creates an <see cref="ImageSource"/> from encoded image bytes.
    /// </summary>
    public static ImageSource FromBytes(byte[] data) =>
        new(data, ImageDecoders.TryDecode, ImageDecoders.DetectFormatId);

    internal static ImageSource FromPngBytes(byte[] data) =>
        new(data, DecodePng, knownFormatId: "png");

    internal static ImageSource FromBmpBytes(byte[] data) =>
        new(data, DecodeBmp, knownFormatId: "bmp");

    /// <summary>
    /// Loads an <see cref="ImageSource"/> from a file path.
    /// </summary>
    /// <param name="path">Path to an encoded image file.</param>
    public static ImageSource FromFile(string path) => FromBytes(File.ReadAllBytes(path));

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
    /// Wraps a pre-decoded BGRA32 buffer. The array is referenced (not copied) - caller must
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
        TryEnsureDecoded(PixelWidth, PixelHeight, out _);
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
            return FromBytes(data);
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return FromBytes(ms.ToArray());
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

    bool IImageMetadataSource.TryGetMetadata(out ImageMetadata metadata) => TryGetMetadata(out metadata);

    internal bool TryGetMetadata(out ImageMetadata metadata)
    {
        lock (_decodeLock)
        {
            if (!_metadataComputed)
            {
                _metadataComputed = true;
                bool succeeded = _encoded != null && ImageDecoders.TryReadMetadata(_encoded, out _metadata);
                RenderMemoryLedger.MetadataProbeCompleted(succeeded);
                if (succeeded)
                {
                    _metadataValid = true;
                    _orientation = _metadata.Orientation;
                }
            }

            metadata = _metadata;
            return _metadataValid;
        }
    }

    public void EnsureDecode()
    {
        TryEnsureDecoded(PixelWidth, PixelHeight, out _);
    }

    /// <summary>Prepares pixels for a known device-pixel footprint without exceeding intrinsic size.</summary>
    public void EnsureDecode(int targetPixelWidth, int targetPixelHeight)
    {
        TryEnsureDecoded(targetPixelWidth, targetPixelHeight, out _);
    }

    private bool TryEnsureDecoded(out StaticPixelBufferSource pixelSource) =>
        TryEnsureDecoded(PixelWidth, PixelHeight, out pixelSource);

    private bool TryEnsureDecoded(int targetPixelWidth, int targetPixelHeight, out StaticPixelBufferSource pixelSource)
    {
        lock (_decodeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            bool hasMetadata = TryGetMetadata(out var metadata);
            int intrinsicWidth = hasMetadata ? metadata.PixelWidth : Math.Max(1, targetPixelWidth);
            int intrinsicHeight = hasMetadata ? metadata.PixelHeight : Math.Max(1, targetPixelHeight);
            targetPixelWidth = Math.Clamp(targetPixelWidth, 1, intrinsicWidth);
            targetPixelHeight = Math.Clamp(targetPixelHeight, 1, intrinsicHeight);

            double targetScale = Math.Min(
                (double)targetPixelWidth / intrinsicWidth,
                (double)targetPixelHeight / intrinsicHeight);
            double decodedScale = _decodedValid
                ? Math.Min(
                    (double)_decodedBitmap.WidthPx / intrinsicWidth,
                    (double)_decodedBitmap.HeightPx / intrinsicHeight)
                : 0;
            bool decodedCoversTarget = _decodedValid && decodedScale + 1e-9 >= targetScale;
            if (decodedCoversTarget && _decodedPixelSource != null)
            {
                if (_decodedOwner is { } cachedOwner)
                {
                    DecodedPixelCache.Shared.Touch(cachedOwner);
                }
                pixelSource = _decodedPixelSource;
                return true;
            }

            if (_decodedValid && (_encoded is null || _decoder is null))
            {
                // Raw-pixel source - wrap the existing buffer without invoking the decoder.
                _decodedPixelSource = new StaticPixelBufferSource(
                    _decodedBitmap.WidthPx, _decodedBitmap.HeightPx, _decodedBitmap.Data, _decodedBitmap.HasAlpha);
                pixelSource = _decodedPixelSource;
                return true;
            }

            if (_encoded is null || _decoder == null)
            {
                pixelSource = null!;
                return false;
            }

            RenderMemoryLedger.DecodeStarted();
            long estimatedTemporaryBytes = hasMetadata
                ? Math.Max(1, (long)intrinsicWidth * intrinsicHeight * 4)
                : ImageDecodeCoordinator.TemporaryByteBudget;
            using var decodeReservation = ImageDecodeCoordinator.Acquire(estimatedTemporaryBytes);
            bool targetIsIntrinsic = targetPixelWidth >= intrinsicWidth && targetPixelHeight >= intrinsicHeight;
            bool decoded = targetIsIntrinsic
                ? _decoder(_encoded, out var decodedBitmap, out var decodedOrientation)
                : ImageDecoders.TryDecode(
                    _encoded,
                    targetPixelWidth,
                    targetPixelHeight,
                    out decodedBitmap,
                    out decodedOrientation);
            if (!decoded)
            {
                RenderMemoryLedger.DecodeCompleted(succeeded: false);
                if (_decodedValid)
                {
                    _decodedPixelSource ??= new StaticPixelBufferSource(
                        _decodedBitmap.WidthPx,
                        _decodedBitmap.HeightPx,
                        _decodedBitmap.Data,
                        _decodedBitmap.HasAlpha);
                    pixelSource = _decodedPixelSource;
                    return true;
                }
                pixelSource = null!;
                return false;
            }

            var previousOwner = _decodedOwner;
            _decodedBitmap = decodedBitmap;
            _decodedOwner = new DecodedPixelOwner(decodedBitmap);
            _orientation = decodedOrientation;
            _decodedValid = true;
            _decodedPixelSource = new StaticPixelBufferSource(
                _decodedBitmap.WidthPx, _decodedBitmap.HeightPx, _decodedBitmap.Data, _decodedBitmap.HasAlpha);
            if (!_metadataValid)
            {
                _metadata = new ImageMetadata(
                    _decodedBitmap.WidthPx,
                    _decodedBitmap.HeightPx,
                    _orientation,
                    _decodedBitmap.HasAlpha);
                _metadataComputed = true;
                _metadataValid = true;
                intrinsicWidth = _decodedBitmap.WidthPx;
                intrinsicHeight = _decodedBitmap.HeightPx;
            }

            bool decodedIntrinsicVariant = _decodedBitmap.WidthPx >= intrinsicWidth
                && _decodedBitmap.HeightPx >= intrinsicHeight;
            _memoryAccounting.ReleaseEncoded(releaseEncoded: decodedIntrinsicVariant);
            DecodedPixelCache.Shared.Unregister(previousOwner);
            previousOwner?.Release();
            if (!decodedIntrinsicVariant)
            {
                DecodedPixelCache.Shared.Register(this, _decodedOwner);
            }
            RenderMemoryLedger.DecodeCompleted(succeeded: true);
            pixelSource = _decodedPixelSource;
            if (decodedIntrinsicVariant)
            {
                // Cache FormatId before releasing encoded bytes so it remains available.
                if (!_formatIdComputed)
                {
                    _cachedFormatId = _formatDetector?.Invoke(_encoded);
                    _formatIdComputed = true;
                }
                _encoded = null;
            }
            return true;
        }
    }

    /// <summary>
    /// Creates a backend image for rendering.
    /// </summary>
    /// <param name="factory">The graphics factory used to create backend resources.</param>
    public IImage CreateImage(IGraphicsFactory factory)
        => CreateImage(factory, PixelWidth, PixelHeight);

    public IImage CreateImage(IGraphicsFactory factory, int targetPixelWidth, int targetPixelHeight)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        RenderMemoryLedger.ImageRealizationRequested();

        // Prefer the decoded pixel path so rendering and sampling share the same decode work and buffer.
        if (TryCreateSharedDecodedImage(factory, targetPixelWidth, targetPixelHeight, out var sharedImage))
        {
            RenderMemoryLedger.ImageRealizationCompleted();
            return sharedImage;
        }

        if (_encoded is null)
        {
            throw new InvalidOperationException("Cannot create image: decode failed and no encoded bytes available.");
        }

        if (factory is IEncodedImageFactory encodedFactory)
        {
            var image = encodedFactory.CreateImageFromBytes(_encoded);
            RenderMemoryLedger.ImageRealizationCompleted();
            return image;
        }

        throw new NotSupportedException(
            $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. " +
            $"Detected: {FormatId ?? "unknown"}.");
    }

    private static bool DecodePng(byte[] encoded, out Bgra32PixelBuffer bitmap, out ImageOrientation orientation)
    {
        orientation = ImageOrientation.Identity;
        return new PngDecoder().TryDecode(encoded, out bitmap);
    }

    private static bool DecodeBmp(byte[] encoded, out Bgra32PixelBuffer bitmap, out ImageOrientation orientation)
    {
        orientation = ImageOrientation.Identity;
        return new BmpDecoder().TryDecode(encoded, out bitmap);
    }

    private readonly Dictionary<RenderDeviceIdentity, SharedImageRealization> _realizations = new();
    private static readonly ConditionalWeakTable<IGraphicsFactory, FactoryLifetimeRegistrations>
        _factoryLifetimeRegistrations = new();

    internal int ActiveRealizationCount
    {
        get
        {
            lock (_decodeLock)
            {
                return _realizations.Count;
            }
        }
    }

    internal static void RetireRealizationsForFactory(IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factoryLifetimeRegistrations.TryGetValue(factory, out var registrations))
        {
            return;
        }

        ulong deviceId = factory.RenderIdentity.DeviceId;
        foreach (var source in registrations.RetireAndSnapshot())
        {
            source.RetireRealizations(deviceId);
        }
    }

    private bool TryCreateSharedDecodedImage(
        IGraphicsFactory factory,
        int targetPixelWidth,
        int targetPixelHeight,
        out IImage image)
    {
        lock (_decodeLock)
        {
            var lifetimeRegistrations = _factoryLifetimeRegistrations.GetValue(
                factory,
                static _ => new FactoryLifetimeRegistrations());
            if (lifetimeRegistrations.IsRetired)
            {
                throw new ObjectDisposedException(factory.GetType().Name);
            }
            if (!lifetimeRegistrations.Register(this))
            {
                throw new ObjectDisposedException(factory.GetType().Name);
            }

            if (!TryEnsureDecoded(targetPixelWidth, targetPixelHeight, out var pixels)
                || _decodedOwner is not { } owner)
            {
                image = null!;
                return false;
            }

            double requestedScale = Math.Min(
                (double)Math.Max(1, targetPixelWidth) / Math.Max(1, PixelWidth),
                (double)Math.Max(1, targetPixelHeight) / Math.Max(1, PixelHeight));
            var renderIdentity = factory.RenderIdentity;
            if (_realizations.TryGetValue(renderIdentity, out var current)
                && current.ResidentScale + 1e-9 >= requestedScale)
            {
                image = current.Acquire(this);
                return true;
            }

            owner.AddReference();
            IImage? backendImage = null;
            IImage? acquiredLease = null;
            SharedImageRealization? createdRealization = null;
            SharedImageRealization? replacedRealization = null;
            try
            {
                backendImage = factory.CreateImageView(pixels);
                var realization = createdRealization = new SharedImageRealization(
                    backendImage,
                    owner,
                    renderIdentity,
                    Math.Min(
                        (double)backendImage.PixelWidth / Math.Max(1, PixelWidth),
                        (double)backendImage.PixelHeight / Math.Max(1, PixelHeight)));
                backendImage = null; // The realization owns the backend image from this point.
                acquiredLease = realization.Acquire(this);
                _realizations.TryGetValue(renderIdentity, out replacedRealization);
                _realizations[renderIdentity] = realization;
                if (replacedRealization is { ReferenceCount: 0 })
                {
                    replacedRealization.Dispose();
                }
                image = acquiredLease;
                acquiredLease = null;
                return true;
            }
            catch (NotSupportedException)
            {
                CleanupFailedRealizationCreation(
                    owner,
                    backendImage,
                    acquiredLease,
                    createdRealization);
                image = null!;
                return false;
            }
            catch
            {
                CleanupFailedRealizationCreation(
                    owner,
                    backendImage,
                    acquiredLease,
                    createdRealization);
                throw;
            }
        }
    }

    private void CleanupFailedRealizationCreation(
        DecodedPixelOwner owner,
        IImage? backendImage,
        IImage? acquiredLease,
        SharedImageRealization? realization)
    {
        if (acquiredLease is not null)
        {
            acquiredLease.Dispose();
            return;
        }

        if (realization is not null)
        {
            realization.Dispose();
            return;
        }

        backendImage?.Dispose();
        owner.Release();
    }

    private void ReleaseRealization(SharedImageRealization realization)
    {
        bool dispose;
        lock (_decodeLock)
        {
            dispose = realization.ReleaseReference();
            if (dispose)
            {
                RenderDeviceIdentity? removeIdentity = null;
                foreach (var pair in _realizations)
                {
                    if (ReferenceEquals(pair.Value, realization))
                    {
                        removeIdentity = pair.Key;
                        break;
                    }
                }
                if (removeIdentity is { } identity)
                {
                    _realizations.Remove(identity);
                }
            }
        }

        if (dispose)
        {
            realization.Dispose();
            DecodedPixelCache.Shared.Maintain();
        }
    }

    private void RetireRealizations(ulong deviceId)
    {
        List<SharedImageRealization>? dispose = null;
        lock (_decodeLock)
        {
            foreach (var pair in _realizations.ToArray())
            {
                if (pair.Key.DeviceId != deviceId)
                {
                    continue;
                }

                _realizations.Remove(pair.Key);
                if (pair.Value.ReferenceCount == 0)
                {
                    (dispose ??= []).Add(pair.Value);
                }
            }
        }

        if (dispose is not null)
        {
            foreach (var realization in dispose)
            {
                realization.Dispose();
            }
            DecodedPixelCache.Shared.Maintain();
        }
    }

    /// <summary>
    /// Releases decoded pixels and retires cached backend realizations owned by this source.
    /// Active image leases remain valid until their own disposal; no in-flight backend image is
    /// destroyed early.
    /// </summary>
    public void Dispose()
    {
        List<SharedImageRealization>? dispose = null;
        DecodedPixelOwner? decodedOwner;

        lock (_decodeLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (var realization in _realizations.Values)
            {
                if (realization.ReferenceCount == 0)
                {
                    (dispose ??= []).Add(realization);
                }
            }
            _realizations.Clear();

            decodedOwner = _decodedOwner;
            _decodedOwner = null;
            _decodedBitmap = default;
            _decodedPixelSource = null;
            _decodedValid = false;
            _encoded = null;
            _memoryAccounting.ReleaseEncoded(releaseEncoded: true);
        }

        DecodedPixelCache.Shared.Unregister(decodedOwner);
        decodedOwner?.Release();
        if (dispose is not null)
        {
            foreach (var realization in dispose)
            {
                realization.Dispose();
            }
        }
    }

    bool IDecodedPixelCacheOwner.TryEvictDecodedPixels(DecodedPixelOwner owner)
    {
        lock (_decodeLock)
        {
            if (!ReferenceEquals(_decodedOwner, owner))
            {
                return true;
            }
            if (_realizations.Count != 0 || _encoded is null)
            {
                return false;
            }

            _decodedOwner = null;
            _decodedBitmap = default;
            _decodedPixelSource = null;
            _decodedValid = false;
            owner.Release();
            return true;
        }
    }

    private sealed class SharedImageRealization : IDisposable
    {
        private readonly IImage _image;
        private readonly DecodedPixelOwner _pixels;
        private int _referenceCount;
        private int _disposed;

        public SharedImageRealization(
            IImage image,
            DecodedPixelOwner pixels,
            RenderDeviceIdentity renderIdentity,
            double residentScale)
        {
            _image = image;
            _pixels = pixels;
            RenderIdentity = renderIdentity;
            ResidentScale = residentScale;
            RenderMemoryLedger.NativeImageRealizationAdded(
                (long)image.PixelWidth * image.PixelHeight * 4);
        }

        public double ResidentScale { get; }

        public RenderDeviceIdentity RenderIdentity { get; }

        public int ReferenceCount => Volatile.Read(ref _referenceCount);

        public IImage Acquire(ImageSource source)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Interlocked.Increment(ref _referenceCount);
            return new SharedImageLease(source, this, _image.PixelWidth, _image.PixelHeight);
        }

        public bool ReleaseReference()
        {
            int remaining = Interlocked.Decrement(ref _referenceCount);
            if (remaining < 0)
            {
                throw new InvalidOperationException("Image realization was released more than once.");
            }
            return remaining == 0;
        }

        public IImage BackendImage => _image;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            RenderMemoryLedger.NativeImageRealizationRemoved(
                (long)_image.PixelWidth * _image.PixelHeight * 4);
            _image.Dispose();
            _pixels.Release();
        }
    }

    private sealed class FactoryLifetimeRegistrations
    {
        private readonly object _gate = new();
        private readonly List<WeakReference<ImageSource>> _sources = new();
        private bool _retired;

        public bool IsRetired
        {
            get
            {
                lock (_gate)
                {
                    return _retired;
                }
            }
        }

        public bool Register(ImageSource source)
        {
            lock (_gate)
            {
                if (_retired)
                {
                    return false;
                }

                for (int i = _sources.Count - 1; i >= 0; i--)
                {
                    if (!_sources[i].TryGetTarget(out var existing))
                    {
                        _sources.RemoveAt(i);
                    }
                    else if (ReferenceEquals(existing, source))
                    {
                        return true;
                    }
                }

                _sources.Add(new WeakReference<ImageSource>(source));
                return true;
            }
        }

        public ImageSource[] RetireAndSnapshot()
        {
            lock (_gate)
            {
                if (_retired)
                {
                    return [];
                }

                _retired = true;
                var result = new List<ImageSource>(_sources.Count);
                foreach (var weak in _sources)
                {
                    if (weak.TryGetTarget(out var source))
                    {
                        result.Add(source);
                    }
                }
                _sources.Clear();
                return result.ToArray();
            }
        }
    }

    private sealed class SharedImageLease(
        ImageSource source,
        SharedImageRealization realization,
        int pixelWidth,
        int pixelHeight) : IImage, IBackendImageProvider
    {
        private ImageSource? _source = source;
        private SharedImageRealization? _realization = realization;

        public int PixelWidth { get; } = pixelWidth;
        public int PixelHeight { get; } = pixelHeight;

        IImage IBackendImageProvider.BackendImage => _realization?.BackendImage
            ?? throw new ObjectDisposedException(nameof(SharedImageLease));

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _source, null);
            var entry = Interlocked.Exchange(ref _realization, null);
            if (owner != null && entry != null)
            {
                owner.ReleaseRealization(entry);
            }
        }
    }

    private sealed class SourceMemoryAccounting
    {
        private long _encodedBytes;

        public SourceMemoryAccounting(long encodedBytes)
        {
            _encodedBytes = encodedBytes;
            if (encodedBytes != 0)
            {
                RenderMemoryLedger.EncodedBackingAdded(encodedBytes);
            }
        }

        public void ReleaseEncoded(bool releaseEncoded)
        {
            if (releaseEncoded)
            {
                long encodedBytes = Interlocked.Exchange(ref _encodedBytes, 0);
                if (encodedBytes != 0)
                {
                    RenderMemoryLedger.EncodedBackingRemoved(encodedBytes);
                }
            }

        }

        ~SourceMemoryAccounting()
        {
            long encodedBytes = Interlocked.Exchange(ref _encodedBytes, 0);
            if (encodedBytes != 0)
            {
                RenderMemoryLedger.EncodedBackingRemoved(encodedBytes);
            }
        }
    }
}
