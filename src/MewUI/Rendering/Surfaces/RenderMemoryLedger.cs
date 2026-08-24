using System.Diagnostics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Process-wide accounting of the render caches the framework owns: how much each cache holds right
/// now and whether every scratch surface that was acquired has come back. Bytes are the caches' own
/// estimates (allocated pixel bytes, tessellation sizes), not a measurement of GPU memory.
/// </summary>
public static class RenderMemoryLedger
{
    // A pooled surface is a GPU texture plus driver bookkeeping; tiny surfaces still cost this much.
    private const long SCRATCH_MIN_ACCOUNTED_BYTES = 128L * 1024;

    private static long _scratchActiveCount;
    private static long _scratchActiveBytes;
    private static long _scratchPooledCount;
    private static long _scratchPooledBytes;
    private static long _scratchCreated;
    private static long _scratchDisposed;
    private static long _bitmapCacheCount;
    private static long _bitmapCacheBytes;
    private static long _vectorCacheCount;
    private static long _vectorCacheBytes;
    private static long _textCacheBytes;
    private static long _geometryCacheBytes;
    private static long _encodedBackingCount;
    private static long _encodedBackingBytes;
    private static long _decodedPixelCount;
    private static long _decodedPixelBytes;
    private static long _decodeAttempts;
    private static long _decodeSucceeded;
    private static long _decodeFailed;
    private static long _decodeTemporaryCount;
    private static long _decodeTemporaryBytes;
    private static long _decodeTemporaryPeakBytes;
    private static long _imageRealizationRequests;
    private static long _imageRealizationSucceeded;
    private static long _pendingReleaseCount;
    private static long _pendingReleaseBytes;
    private static long _persistentResourceCount;
    private static long _persistentResourceBytes;
    private static long _metadataProbeAttempts;
    private static long _metadataProbeSucceeded;
    private static Process? _process;

    /// <summary>
    /// Platform-provided reader for the process memory counters. The platform host sets it on
    /// registration; without one the snapshot falls back to the runtime's process counters, which
    /// cannot report a private working set.
    /// </summary>
    internal static Func<ProcessMemory>? ProcessMemoryReader { get; set; }

    /// <summary>Reads the current totals.</summary>
    public static RenderMemorySnapshot Snapshot()
    {
        var process = ReadProcessMemory();
        return new RenderMemorySnapshot(
            Volatile.Read(ref _scratchActiveCount),
            Volatile.Read(ref _scratchActiveBytes),
            Volatile.Read(ref _scratchPooledCount),
            Volatile.Read(ref _scratchPooledBytes),
            Volatile.Read(ref _scratchCreated),
            Volatile.Read(ref _scratchDisposed),
            Volatile.Read(ref _bitmapCacheCount),
            Volatile.Read(ref _bitmapCacheBytes),
            Volatile.Read(ref _vectorCacheCount),
            Volatile.Read(ref _vectorCacheBytes),
            Volatile.Read(ref _textCacheBytes),
            Volatile.Read(ref _geometryCacheBytes),
            process.PrivateUsage,
            process.WorkingSetSize,
            process.PrivateWorkingSetSize,
            GC.GetTotalMemory(false))
        {
            EncodedBackingCount = Volatile.Read(ref _encodedBackingCount),
            EncodedBackingBytes = Volatile.Read(ref _encodedBackingBytes),
            DecodedPixelCount = Volatile.Read(ref _decodedPixelCount),
            DecodedPixelBytes = Volatile.Read(ref _decodedPixelBytes),
            DecodeAttempts = Volatile.Read(ref _decodeAttempts),
            DecodeSucceeded = Volatile.Read(ref _decodeSucceeded),
            DecodeFailed = Volatile.Read(ref _decodeFailed),
            DecodeTemporaryCount = Volatile.Read(ref _decodeTemporaryCount),
            DecodeTemporaryBytes = Volatile.Read(ref _decodeTemporaryBytes),
            DecodeTemporaryPeakBytes = Volatile.Read(ref _decodeTemporaryPeakBytes),
            ImageRealizationRequests = Volatile.Read(ref _imageRealizationRequests),
            ImageRealizationSucceeded = Volatile.Read(ref _imageRealizationSucceeded),
            PendingReleaseCount = Volatile.Read(ref _pendingReleaseCount),
            PendingReleaseBytes = Volatile.Read(ref _pendingReleaseBytes),
            PersistentResourceCount = Volatile.Read(ref _persistentResourceCount),
            PersistentResourceBytes = Volatile.Read(ref _persistentResourceBytes),
            MetadataProbeAttempts = Volatile.Read(ref _metadataProbeAttempts),
            MetadataProbeSucceeded = Volatile.Read(ref _metadataProbeSucceeded),
        };
    }

    private static ProcessMemory ReadProcessMemory()
    {
        var reader = ProcessMemoryReader;
        if (reader != null)
        {
            return reader();
        }

        var process = _process ??= Process.GetCurrentProcess();
        process.Refresh();
        return new ProcessMemory(process.PrivateMemorySize64, Environment.WorkingSet, 0);
    }

    /// <summary>Accounting size of a scratch surface allocation, shared by the pool and the ledger.</summary>
    internal static long ScratchBytes(int pixelWidth, int pixelHeight)
        => Math.Max(SCRATCH_MIN_ACCOUNTED_BYTES, (long)Math.Max(1, pixelWidth) * Math.Max(1, pixelHeight) * 4);

    internal static void ScratchAcquired(long bytes, bool created)
    {
        Interlocked.Increment(ref _scratchActiveCount);
        Interlocked.Add(ref _scratchActiveBytes, bytes);
        if (created)
        {
            Interlocked.Increment(ref _scratchCreated);
        }
    }

    internal static void ScratchReleased(long bytes)
    {
        Interlocked.Decrement(ref _scratchActiveCount);
        Interlocked.Add(ref _scratchActiveBytes, -bytes);
    }

    internal static void ScratchPooled(long bytes)
    {
        Interlocked.Increment(ref _scratchPooledCount);
        Interlocked.Add(ref _scratchPooledBytes, bytes);
    }

    internal static void ScratchUnpooled(long bytes, bool disposed)
    {
        Interlocked.Decrement(ref _scratchPooledCount);
        Interlocked.Add(ref _scratchPooledBytes, -bytes);
        if (disposed)
        {
            Interlocked.Increment(ref _scratchDisposed);
        }
    }

    internal static void ScratchDisposedOutsidePool() => Interlocked.Increment(ref _scratchDisposed);

    internal static void BitmapCacheEntryAdded(long bytes)
    {
        Interlocked.Increment(ref _bitmapCacheCount);
        Interlocked.Add(ref _bitmapCacheBytes, bytes);
    }

    internal static void BitmapCacheEntryRemoved(long bytes)
    {
        Interlocked.Decrement(ref _bitmapCacheCount);
        Interlocked.Add(ref _bitmapCacheBytes, -bytes);
    }

    internal static void VectorCacheEntryAdded(long bytes)
    {
        Interlocked.Increment(ref _vectorCacheCount);
        Interlocked.Add(ref _vectorCacheBytes, bytes);
    }

    internal static void VectorCacheEntryRemoved(long bytes)
    {
        Interlocked.Decrement(ref _vectorCacheCount);
        Interlocked.Add(ref _vectorCacheBytes, -bytes);
    }

    internal static void TextCacheBytesChanged(long delta) => Interlocked.Add(ref _textCacheBytes, delta);

    internal static void GeometryCacheBytesChanged(long delta) => Interlocked.Add(ref _geometryCacheBytes, delta);

    internal static void EncodedBackingAdded(long bytes)
    {
        Interlocked.Increment(ref _encodedBackingCount);
        Interlocked.Add(ref _encodedBackingBytes, bytes);
    }

    internal static void EncodedBackingRemoved(long bytes)
    {
        Interlocked.Decrement(ref _encodedBackingCount);
        Interlocked.Add(ref _encodedBackingBytes, -bytes);
    }

    internal static void DecodedPixelsAdded(long bytes)
    {
        Interlocked.Increment(ref _decodedPixelCount);
        Interlocked.Add(ref _decodedPixelBytes, bytes);
    }

    internal static void DecodedPixelsRemoved(long bytes)
    {
        Interlocked.Decrement(ref _decodedPixelCount);
        Interlocked.Add(ref _decodedPixelBytes, -bytes);
    }

    internal static void DecodeStarted() => Interlocked.Increment(ref _decodeAttempts);

    internal static void MetadataProbeCompleted(bool succeeded)
    {
        Interlocked.Increment(ref _metadataProbeAttempts);
        if (succeeded)
        {
            Interlocked.Increment(ref _metadataProbeSucceeded);
        }
    }

    internal static void DecodeCompleted(bool succeeded)
    {
        Interlocked.Increment(ref succeeded ? ref _decodeSucceeded : ref _decodeFailed);
    }

    internal static void DecodeTemporaryAdded(long bytes)
    {
        Interlocked.Increment(ref _decodeTemporaryCount);
        long current = Interlocked.Add(ref _decodeTemporaryBytes, bytes);
        long peak = Volatile.Read(ref _decodeTemporaryPeakBytes);
        while (current > peak)
        {
            long observed = Interlocked.CompareExchange(ref _decodeTemporaryPeakBytes, current, peak);
            if (observed == peak)
            {
                break;
            }
            peak = observed;
        }
    }

    internal static void DecodeTemporaryRemoved(long bytes)
    {
        Interlocked.Decrement(ref _decodeTemporaryCount);
        Interlocked.Add(ref _decodeTemporaryBytes, -bytes);
    }

    internal static void ImageRealizationRequested() => Interlocked.Increment(ref _imageRealizationRequests);

    internal static void ImageRealizationCompleted() => Interlocked.Increment(ref _imageRealizationSucceeded);

    internal static void PendingReleaseAdded(long bytes)
    {
        Interlocked.Increment(ref _pendingReleaseCount);
        Interlocked.Add(ref _pendingReleaseBytes, bytes);
    }

    internal static void PendingReleaseRemoved(long bytes)
    {
        Interlocked.Decrement(ref _pendingReleaseCount);
        Interlocked.Add(ref _pendingReleaseBytes, -bytes);
    }

    internal static void PersistentResourceAdded(long bytes)
    {
        Interlocked.Increment(ref _persistentResourceCount);
        Interlocked.Add(ref _persistentResourceBytes, bytes);
    }

    internal static void PersistentResourceRemoved(long bytes)
    {
        Interlocked.Decrement(ref _persistentResourceCount);
        Interlocked.Add(ref _persistentResourceBytes, -bytes);
    }
}

/// <summary>Process memory counters as the operating system reports them.</summary>
/// <param name="PrivateUsage">Committed memory private to the process (Windows private bytes; the physical footprint on macOS; anonymous data and stack mappings on Linux).</param>
/// <param name="WorkingSetSize">Resident memory, shared pages included.</param>
/// <param name="PrivateWorkingSetSize">Resident memory not shared with any other process; 0 when the platform cannot report it.</param>
public readonly record struct ProcessMemory(long PrivateUsage, long WorkingSetSize, long PrivateWorkingSetSize);

/// <summary>One reading of <see cref="RenderMemoryLedger"/>.</summary>
/// <param name="ScratchActiveCount">Scratch surfaces currently rented by a cache (bitmap caches, vector caches).</param>
/// <param name="ScratchActiveBytes">Allocated pixel bytes of the rented scratch surfaces.</param>
/// <param name="ScratchPooledCount">Scratch surfaces sitting in the pool, ready to be rented.</param>
/// <param name="ScratchPooledBytes">Allocated pixel bytes of the pooled scratch surfaces.</param>
/// <param name="ScratchCreated">Scratch surfaces created since the process started.</param>
/// <param name="ScratchDisposed">Scratch surfaces disposed since the process started.</param>
/// <param name="BitmapCacheCount">Elements currently holding a <see cref="BitmapCache"/> bitmap.</param>
/// <param name="BitmapCacheBytes">Allocated pixel bytes behind those bitmaps.</param>
/// <param name="VectorCacheCount"><c>Image</c> controls currently holding a rasterized vector bitmap.</param>
/// <param name="VectorCacheBytes">Allocated pixel bytes behind those bitmaps.</param>
/// <param name="TextCacheBytes">Rasterized text the backend text caches hold.</param>
/// <param name="GeometryCacheBytes">Tessellation and geometry data retained for frozen paths.</param>
/// <param name="PrivateUsage">See <see cref="ProcessMemory.PrivateUsage"/>.</param>
/// <param name="WorkingSetSize">See <see cref="ProcessMemory.WorkingSetSize"/>.</param>
/// <param name="PrivateWorkingSetSize">See <see cref="ProcessMemory.PrivateWorkingSetSize"/>.</param>
/// <param name="GcHeapBytes">Managed heap at the time of the reading.</param>
public readonly record struct RenderMemorySnapshot(
    long ScratchActiveCount,
    long ScratchActiveBytes,
    long ScratchPooledCount,
    long ScratchPooledBytes,
    long ScratchCreated,
    long ScratchDisposed,
    long BitmapCacheCount,
    long BitmapCacheBytes,
    long VectorCacheCount,
    long VectorCacheBytes,
    long TextCacheBytes,
    long GeometryCacheBytes,
    long PrivateUsage,
    long WorkingSetSize,
    long PrivateWorkingSetSize,
    long GcHeapBytes)
{
    /// <summary>Encoded image payloads currently retained by <c>ImageSource</c> instances.</summary>
    public long EncodedBackingCount { get; init; }

    /// <summary>Bytes in encoded image payloads currently retained by <c>ImageSource</c> instances.</summary>
    public long EncodedBackingBytes { get; init; }

    /// <summary>Decoded BGRA image variants currently retained by <c>ImageSource</c> instances.</summary>
    public long DecodedPixelCount { get; init; }

    /// <summary>Bytes in decoded BGRA image variants currently retained by <c>ImageSource</c> instances.</summary>
    public long DecodedPixelBytes { get; init; }

    /// <summary>Built-in image decode attempts since process start.</summary>
    public long DecodeAttempts { get; init; }

    /// <summary>Successful built-in image decodes since process start.</summary>
    public long DecodeSucceeded { get; init; }

    /// <summary>Failed built-in image decodes since process start.</summary>
    public long DecodeFailed { get; init; }

    /// <summary>Image decodes currently holding a temporary-memory reservation.</summary>
    public long DecodeTemporaryCount { get; init; }

    /// <summary>Estimated bytes reserved by image decodes currently in progress.</summary>
    public long DecodeTemporaryBytes { get; init; }

    /// <summary>Highest simultaneous decode temporary reservation since process start.</summary>
    public long DecodeTemporaryPeakBytes { get; init; }

    /// <summary>Backend image realization requests since process start.</summary>
    public long ImageRealizationRequests { get; init; }

    /// <summary>Backend image realization requests completed successfully since process start.</summary>
    public long ImageRealizationSucceeded { get; init; }

    /// <summary>Resources waiting for a backend operation before disposal.</summary>
    public long PendingReleaseCount { get; init; }

    /// <summary>Estimated bytes waiting for a backend operation before disposal.</summary>
    public long PendingReleaseBytes { get; init; }

    /// <summary>Persistent render-cache entries currently owned by device resource caches.</summary>
    public long PersistentResourceCount { get; init; }

    /// <summary>Estimated surface bytes owned by persistent render-cache entries.</summary>
    public long PersistentResourceBytes { get; init; }

    /// <summary>Image metadata probe attempts since process start.</summary>
    public long MetadataProbeAttempts { get; init; }

    /// <summary>Image metadata probes completed successfully since process start.</summary>
    public long MetadataProbeSucceeded { get; init; }

    /// <summary>Every scratch surface ever created is either still rented, pooled, or disposed.</summary>
    public bool IsBalanced => ScratchCreated - ScratchDisposed == ScratchActiveCount + ScratchPooledCount;
}
