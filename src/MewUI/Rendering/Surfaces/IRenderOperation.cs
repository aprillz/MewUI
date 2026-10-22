namespace Aprillz.MewUI.Rendering;

/// <summary>A backend operation that may complete asynchronously, such as a readback or an upload.</summary>
public interface IRenderOperation : IDisposable
{
    /// <summary>Gets whether the operation has completed.</summary>
    bool IsCompleted { get; }

    /// <summary>Blocks until the operation completes.</summary>
    void Wait();

    /// <summary>Waits for the operation to complete, observing <paramref name="cancellationToken"/>.</summary>
    ValueTask WaitAsync(CancellationToken cancellationToken = default);
}
