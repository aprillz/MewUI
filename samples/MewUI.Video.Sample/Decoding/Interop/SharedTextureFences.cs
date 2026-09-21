using System.Runtime.InteropServices;

using Aprillz.MewUI.Video.Sample.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Pair of NT-shared D3D11 fences that order converter writes against OpenGL reads of the shared output textures.
/// </summary>
/// <remarks>
/// The producer fence is signaled by D3D11 after each conversion and waited on by GL before sampling. The consumer
/// fence is signaled by GL after a frame samples a texture and waited on by D3D11 before converting into it again.
/// Both waits run on the GPU queues, so neither thread blocks.
/// </remarks>
internal sealed unsafe class SharedTextureFences : IDisposable
{
    private const int DeviceCreateFenceIndex = 68;
    private const int FenceCreateSharedHandleIndex = 7;
    private const int Context4SignalIndex = 147;
    private const int Context4WaitIndex = 148;
    private const uint D3D11_FENCE_FLAG_SHARED = 0x2;
    private const uint GENERIC_ALL = 0x10000000;

    private static readonly Guid IID_ID3D11Device5 = new("8FFDE202-A0E7-45DF-9E01-E837801B5EA0");
    private static readonly Guid IID_ID3D11DeviceContext4 = new("917600DA-F58C-4C33-98D8-3E15B390FA24");
    private static readonly Guid IID_ID3D11Fence = new("AFFDE9D1-1DF7-4BB7-8A34-0F46251DAB80");

    private readonly nint _context4;
    private readonly nint _producerFence;
    private readonly nint _consumerFence;
    private readonly object _gate = new();
    // Consumer fence value each texture was last read up to; converting into the texture waits for it.
    private readonly Dictionary<nint, ulong> _lastReadValues = new();
    private ulong _producedValue;
    private long _consumedValue;
    private bool _disposed;

    private SharedTextureFences(nint context4, nint producerFence, nint consumerFence, nint producerHandle, nint consumerHandle)
    {
        _context4 = context4;
        _producerFence = producerFence;
        _consumerFence = consumerFence;
        ProducerFenceHandle = producerHandle;
        ConsumerFenceHandle = consumerHandle;
    }

    public nint ProducerFenceHandle { get; }

    public nint ConsumerFenceHandle { get; }

    /// <summary>
    /// Set when the reader cannot wait on the producer fence; the converter then finishes each conversion on the CPU before handing it over.
    /// </summary>
    public bool CpuCompletionRequired { get; set; }

    public static SharedTextureFences? TryCreate(nint device, nint deviceContext)
    {
        nint device5 = 0;
        nint context4 = 0;
        nint producerFence = 0;
        nint consumerFence = 0;
        nint producerHandle = 0;
        nint consumerHandle = 0;

        try
        {
            int deviceResult = Marshal.QueryInterface(device, in IID_ID3D11Device5, out device5);
            int contextResult = Marshal.QueryInterface(deviceContext, in IID_ID3D11DeviceContext4, out context4);
            if (deviceResult < 0 || device5 == 0 || contextResult < 0 || context4 == 0)
            {
                SampleLog.Write($"[fence] ID3D11Device5/ID3D11DeviceContext4 unavailable: device hr=0x{deviceResult:X8}, context hr=0x{contextResult:X8}.");
                return null;
            }

            if (!TryCreateSharedFence(device5, out producerFence, out producerHandle)
                || !TryCreateSharedFence(device5, out consumerFence, out consumerHandle))
            {
                return null;
            }

            var fences = new SharedTextureFences(context4, producerFence, consumerFence, producerHandle, consumerHandle);
            context4 = 0;
            producerFence = 0;
            consumerFence = 0;
            producerHandle = 0;
            consumerHandle = 0;
            SampleLog.Write($"[fence] shared fences created: producer=0x{fences.ProducerFenceHandle:X} consumer=0x{fences.ConsumerFenceHandle:X}");
            return fences;
        }
        finally
        {
            ReleaseIfNeeded(device5);
            ReleaseIfNeeded(context4);
            ReleaseIfNeeded(producerFence);
            ReleaseIfNeeded(consumerFence);
            CloseIfNeeded(producerHandle);
            CloseIfNeeded(consumerHandle);
        }
    }

    /// <summary>
    /// Queues a GPU wait until GL has finished every recorded read of <paramref name="texture"/>.
    /// </summary>
    public void WaitBeforeWrite(nint texture)
    {
        ulong lastRead;
        lock (_gate)
        {
            if (_disposed || !_lastReadValues.TryGetValue(texture, out lastRead))
            {
                return;
            }
        }

        var wait = (delegate* unmanaged[Stdcall]<nint, nint, ulong, int>)(*(nint**)_context4)[Context4WaitIndex];
        _ = wait(_context4, _consumerFence, lastRead);
    }

    /// <summary>
    /// Signals the producer fence after a conversion and returns the value GL has to wait for.
    /// </summary>
    public ulong SignalProduced()
    {
        ulong value;
        lock (_gate)
        {
            value = ++_producedValue;
        }

        var signal = (delegate* unmanaged[Stdcall]<nint, nint, ulong, int>)(*(nint**)_context4)[Context4SignalIndex];
        _ = signal(_context4, _producerFence, value);
        return value;
    }

    /// <summary>
    /// Reserves the next consumer fence value for a GL read of <paramref name="texture"/>.
    /// </summary>
    public ulong ReserveReadValue(nint texture)
    {
        ulong value = (ulong)Interlocked.Increment(ref _consumedValue);
        lock (_gate)
        {
            _lastReadValues[texture] = value;
        }

        return value;
    }

    /// <summary>
    /// Forgets a released texture so a later texture reusing the same address starts without a pending read.
    /// </summary>
    public void Forget(nint texture)
    {
        lock (_gate)
        {
            _lastReadValues.Remove(texture);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        ReleaseIfNeeded(_producerFence);
        ReleaseIfNeeded(_consumerFence);
        ReleaseIfNeeded(_context4);
        CloseIfNeeded(ProducerFenceHandle);
        CloseIfNeeded(ConsumerFenceHandle);
    }

    private static bool TryCreateSharedFence(nint device5, out nint fence, out nint sharedHandle)
    {
        fence = 0;
        sharedHandle = 0;

        var createFence = (delegate* unmanaged[Stdcall]<nint, ulong, uint, Guid*, nint*, int>)(*(nint**)device5)[DeviceCreateFenceIndex];
        Guid fenceId = IID_ID3D11Fence;
        nint localFence = 0;
        int createResult = createFence(device5, 0, D3D11_FENCE_FLAG_SHARED, &fenceId, &localFence);
        if (createResult < 0 || localFence == 0)
        {
            SampleLog.Write($"[fence] ID3D11Device5::CreateFence failed hr=0x{createResult:X8}.");
            return false;
        }

        var createHandle = (delegate* unmanaged[Stdcall]<nint, nint, uint, char*, nint*, int>)(*(nint**)localFence)[FenceCreateSharedHandleIndex];
        nint localHandle = 0;
        int handleResult = createHandle(localFence, 0, GENERIC_ALL, null, &localHandle);
        if (handleResult < 0 || localHandle == 0)
        {
            SampleLog.Write($"[fence] ID3D11Fence::CreateSharedHandle failed hr=0x{handleResult:X8}.");
            ReleaseIfNeeded(localFence);
            return false;
        }

        fence = localFence;
        sharedHandle = localHandle;
        return true;
    }

    private static void ReleaseIfNeeded(nint unknown)
    {
        if (unknown != 0)
        {
            Marshal.Release(unknown);
        }
    }

    private static void CloseIfNeeded(nint handle)
    {
        if (handle != 0)
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
