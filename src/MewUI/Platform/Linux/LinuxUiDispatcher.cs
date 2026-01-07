using System.Collections.Concurrent;
using System.Threading;

namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxUiDispatcher : SynchronizationContext, IUiDispatcher
{
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> _queue = new();

    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _uiThreadId;

    public void Post(Action action)
    {
        if (action == null)
            return;
        _queue.Enqueue(action);
    }

    public void Send(Action action)
    {
        if (action == null)
            return;
        if (IsOnUIThread)
        {
            action();
            return;
        }

        using var gate = new ManualResetEventSlim(false);
        Exception? error = null;
        _queue.Enqueue(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { gate.Set(); }
        });

        gate.Wait();
        if (error != null)
            throw new AggregateException(error);
    }

    public void ProcessWorkItems()
    {
        while (_queue.TryDequeue(out var action))
            action();
    }

    public override void Post(SendOrPostCallback d, object? state)
        => Post(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
        => Send(() => d(state));
}

