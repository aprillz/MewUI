namespace Aprillz.MewUI.Controls;

/// <summary>
/// Represents a deferral that allows asynchronous completion of an event.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreWebView2Deferral : IDisposable
{
    private ComObject<ICoreWebView2Deferral>? _deferral;
    private bool _completed;
    private bool _disposed;

    internal CoreWebView2Deferral(ICoreWebView2Deferral deferral)
    {
        if (deferral == null) throw new ArgumentNullException(nameof(deferral));
        _deferral = new ComObject<ICoreWebView2Deferral>(deferral);
    }

    /// <summary>
    /// Signals that the deferred event has been processed.
    /// Call this method when your asynchronous operation is complete.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The deferral has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Complete has already been called.</exception>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_completed)
        {
            throw new InvalidOperationException("Complete has already been called on this deferral.");
        }

        _completed = true;
        _deferral?.Object.Complete().ThrowOnError();
    }

    /// <summary>
    /// Releases all resources used by the CoreWebView2Deferral.
    /// If Complete() was not called, this will call Complete() automatically.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Auto-complete if not already completed to prevent hanging
        if (!_completed && _deferral != null && !_deferral.IsDisposed)
        {
            try
            {
                _deferral.Object.Complete();
            }
            catch
            {
                // Ignore errors during auto-complete on dispose
            }
        }

        _deferral?.Dispose();
        _deferral = null;
    }
}
