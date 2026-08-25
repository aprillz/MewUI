using Aprillz.MewUI;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Core;

[TestClass]
public sealed class DispatcherOperationTests
{
    [TestMethod]
    public void SuccessfulAbort_PreventsCallback()
    {
        var queue = new DispatcherQueue();
        bool invoked = false;
        DispatcherOperation operation = queue.EnqueueWithOperation(
            DispatcherPriority.Normal,
            () => invoked = true);

        Assert.IsTrue(operation.Abort());
        queue.Process();

        Assert.IsFalse(invoked);
        Assert.AreEqual(DispatcherOperationStatus.Aborted, operation.Status);
        Assert.IsFalse(operation.Abort());
    }

    [TestMethod]
    public void AbortAndExecute_CompeteOnOneAtomicTransition()
    {
        for (int iteration = 0; iteration < 500; iteration++)
        {
            var queue = new DispatcherQueue();
            int invoked = 0;
            DispatcherOperation operation = queue.EnqueueWithOperation(
                DispatcherPriority.Normal,
                () => Interlocked.Increment(ref invoked));

            using var start = new ManualResetEventSlim();
            bool aborted = false;
            Task processor = Task.Run(() =>
            {
                start.Wait();
                queue.Process();
            });
            Task aborter = Task.Run(() =>
            {
                start.Wait();
                aborted = operation.Abort();
            });

            start.Set();
            Task.WaitAll(processor, aborter);

            if (aborted)
            {
                Assert.AreEqual(0, invoked, $"Callback ran after successful abort at iteration {iteration}.");
                Assert.AreEqual(DispatcherOperationStatus.Aborted, operation.Status);
            }
            else
            {
                Assert.AreEqual(1, invoked, $"Callback did not run after execution won at iteration {iteration}.");
                Assert.AreEqual(DispatcherOperationStatus.Completed, operation.Status);
            }
        }
    }

    [TestMethod]
    public void ThrowingCallback_ReachesFaultedStateWithException()
    {
        var queue = new DispatcherQueue();
        var thrown = new InvalidOperationException("expected");
        DispatcherOperation operation = queue.EnqueueWithOperation(
            DispatcherPriority.Normal,
            () => throw thrown);

        queue.Process();

        Assert.AreEqual(DispatcherOperationStatus.Faulted, operation.Status);
        Assert.AreSame(thrown, operation.Exception);
    }

    [TestMethod]
    public void SuccessfulCallback_HasNoException()
    {
        var queue = new DispatcherQueue();
        DispatcherOperation operation = queue.EnqueueWithOperation(
            DispatcherPriority.Normal,
            static () => { });

        queue.Process();

        Assert.AreEqual(DispatcherOperationStatus.Completed, operation.Status);
        Assert.IsNull(operation.Exception);
    }
}
