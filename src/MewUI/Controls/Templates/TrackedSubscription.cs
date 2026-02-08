namespace Aprillz.MewUI.Controls;

internal sealed class TrackedSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public TrackedSubscription(Action unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(unsubscribe);
        _unsubscribe = unsubscribe;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unsubscribe();
    }
}

