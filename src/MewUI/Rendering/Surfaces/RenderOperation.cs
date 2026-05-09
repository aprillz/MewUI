namespace Aprillz.MewUI.Rendering;

public static class RenderOperation
{
    public static IRenderOperation Completed { get; } = new CompletedRenderOperation();

    public static IRenderOperation FromTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.IsCompletedSuccessfully ? Completed : new TaskRenderOperation(task);
    }

    public static IRenderOperation FromValueTask(ValueTask valueTask)
        => valueTask.IsCompletedSuccessfully
            ? Completed
            : FromTask(valueTask.AsTask());

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

    private sealed class TaskRenderOperation : IRenderOperation
    {
        private readonly Task _task;

        public TaskRenderOperation(Task task)
        {
            _task = task;
        }

        public bool IsCompleted => _task.IsCompleted;

        public void Wait()
        {
            _task.GetAwaiter().GetResult();
        }

        public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
        {
            await _task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
        }
    }
}
