namespace Aprillz.MewUI.Rendering;

public static class RenderOperation
{
    public static IRenderOperation Completed { get; } = new CompletedRenderOperation();

    private sealed class CompletedRenderOperation : IRenderOperation
    {
        public bool IsCompleted => true;

        public void Wait()
        {
        }

        public ValueTask WaitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
