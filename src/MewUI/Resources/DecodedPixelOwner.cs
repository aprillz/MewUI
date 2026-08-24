using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Resources;

internal sealed class DecodedPixelOwner
{
    private int _referenceCount = 1;
    private long _accountedBytes;

    public DecodedPixelOwner(Bgra32PixelBuffer buffer)
    {
        Buffer = buffer;
        _accountedBytes = buffer.Data.LongLength;
        RenderMemoryLedger.DecodedPixelsAdded(_accountedBytes);
    }

    public Bgra32PixelBuffer Buffer { get; }

    public long AccountedBytes => Volatile.Read(ref _accountedBytes);

    public void AddReference()
    {
        int count = Interlocked.Increment(ref _referenceCount);
        if (count <= 1)
        {
            Interlocked.Decrement(ref _referenceCount);
            throw new ObjectDisposedException(nameof(DecodedPixelOwner));
        }
    }

    public void Release()
    {
        int remaining = Interlocked.Decrement(ref _referenceCount);
        if (remaining < 0)
        {
            throw new InvalidOperationException("Decoded pixel owner was released more than once.");
        }
        if (remaining == 0)
        {
            ReleaseAccounting();
        }
    }

    private void ReleaseAccounting()
    {
        long bytes = Interlocked.Exchange(ref _accountedBytes, 0);
        if (bytes != 0)
        {
            RenderMemoryLedger.DecodedPixelsRemoved(bytes);
        }
    }

    ~DecodedPixelOwner() => ReleaseAccounting();
}
