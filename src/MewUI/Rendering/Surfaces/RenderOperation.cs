namespace Aprillz.MewUI.Rendering;

/// <summary>Creates <see cref="IRenderOperation"/> values.</summary>
public static class RenderOperation
{
    /// <summary>Gets an operation that is already completed.</summary>
    public static IRenderOperation Completed { get; } = new CompletedRenderOperation();

    /// <summary>Wraps <paramref name="task"/>; returns <see cref="Completed"/> when it has already completed successfully.</summary>
    public static IRenderOperation FromTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.IsCompletedSuccessfully ? Completed : new TaskRenderOperation(task);
    }

    /// <summary>Wraps <paramref name="valueTask"/>; returns <see cref="Completed"/> when it has already completed successfully.</summary>
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
