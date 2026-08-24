using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Resources;

internal static class ImageDecodeCoordinator
{
    internal const int MaxConcurrentDecodes = 2;
    internal const long TemporaryByteBudget = 256L * 1024 * 1024;

    private static readonly SemaphoreSlim s_slots = new(MaxConcurrentDecodes, MaxConcurrentDecodes);
    private static readonly object s_budgetLock = new();
    private static long s_reservedBytes;

    public static Reservation Acquire(long estimatedTemporaryBytes)
    {
        estimatedTemporaryBytes = Math.Max(1, estimatedTemporaryBytes);
        s_slots.Wait();
        try
        {
            lock (s_budgetLock)
            {
                while (s_reservedBytes != 0
                    && (estimatedTemporaryBytes > TemporaryByteBudget
                        || s_reservedBytes + estimatedTemporaryBytes > TemporaryByteBudget))
                {
                    Monitor.Wait(s_budgetLock);
                }

                s_reservedBytes += estimatedTemporaryBytes;
                RenderMemoryLedger.DecodeTemporaryAdded(estimatedTemporaryBytes);
            }
            return new Reservation(estimatedTemporaryBytes);
        }
        catch
        {
            s_slots.Release();
            throw;
        }
    }

    internal sealed class Reservation(long bytes) : IDisposable
    {
        private long _bytes = bytes;

        public void Dispose()
        {
            long released = Interlocked.Exchange(ref _bytes, 0);
            if (released == 0)
            {
                return;
            }

            lock (s_budgetLock)
            {
                s_reservedBytes -= released;
                RenderMemoryLedger.DecodeTemporaryRemoved(released);
                Monitor.PulseAll(s_budgetLock);
            }
            s_slots.Release();
        }
    }
}
