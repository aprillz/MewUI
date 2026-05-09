namespace Aprillz.MewUI.Rendering;

public interface IRenderOperation : IDisposable
{
    bool IsCompleted { get; }

    void Wait();

    ValueTask WaitAsync(CancellationToken cancellationToken = default);
}
