using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Markdown;

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
[DoNotParallelize]
public sealed class MarkdownLifecycleTests
{
    [TestMethod]
    public void SynchronousImageLeaseReleasesOnceWhenPresenterIsDisposed()
    {
        EnsureGdi();
        int releaseCount = 0;
        var resolver = new ImmediateResolver(() => new MarkdownImageLease(new TestImageSource(), () => Interlocked.Increment(ref releaseCount)));
        var presenter = new MarkdownPresenter
        {
            Markdown = "![alt](image.png)",
            ImageResolver = resolver
        };

        presenter.Measure(new Size(240, 100));
        presenter.Measure(new Size(240, 100));
        Assert.AreEqual(1, resolver.CallCount);

        presenter.Dispose();

        Assert.AreEqual(1, Volatile.Read(ref releaseCount));
    }

    [TestMethod]
    public void ReplacingMarkdownCancelsOldImageAndReleasesLateResult()
    {
        EnsureGdi();
        var oldPending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newPending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int oldReleaseCount = 0;
        int oldImageCreationCount = 0;
        var oldSource = new TestImageSource(() => Interlocked.Increment(ref oldImageCreationCount));
        var resolver = new UrlResolver(
            new Dictionary<string, TaskCompletionSource<MarkdownImageLease?>>(StringComparer.Ordinal)
            {
                ["old.png"] = oldPending,
                ["new.png"] = newPending
            });
        var presenter = new MarkdownPresenter
        {
            Markdown = "![old](old.png)",
            ImageResolver = resolver
        };

        presenter.Measure(new Size(240, 100));
        Element? oldRoot = presenter.DocumentRoot;
        presenter.Markdown = "![new](new.png)";
        presenter.Measure(new Size(240, 100));
        Element? newRoot = presenter.DocumentRoot;

        Assert.AreNotSame(oldRoot, newRoot);
        oldPending.SetResult(new MarkdownImageLease(oldSource, () => Interlocked.Increment(ref oldReleaseCount)));
        SpinWait.SpinUntil(() => Volatile.Read(ref oldReleaseCount) == 1, TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, Volatile.Read(ref oldReleaseCount));
        Assert.AreEqual(0, Volatile.Read(ref oldImageCreationCount));

        presenter.Dispose();
        newPending.SetResult(null);
    }

    [TestMethod]
    public void ReplacingResolverCancelsOldImageTask()
    {
        EnsureGdi();
        var oldPending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newPending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int oldReleaseCount = 0;
        var oldResolver = new UrlResolver(new Dictionary<string, TaskCompletionSource<MarkdownImageLease?>>(StringComparer.Ordinal)
        {
            ["old.png"] = oldPending
        });
        var newResolver = new UrlResolver(new Dictionary<string, TaskCompletionSource<MarkdownImageLease?>>(StringComparer.Ordinal)
        {
            ["new.png"] = newPending
        });
        var presenter = new MarkdownPresenter
        {
            Markdown = "![old](old.png)",
            ImageResolver = oldResolver
        };

        presenter.Measure(new Size(240, 100));
        presenter.ImageResolver = newResolver;
        presenter.Markdown = "![new](new.png)";
        presenter.Measure(new Size(240, 100));
        oldPending.SetResult(new MarkdownImageLease(new TestImageSource(), () => Interlocked.Increment(ref oldReleaseCount)));
        SpinWait.SpinUntil(() => Volatile.Read(ref oldReleaseCount) == 1, TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, Volatile.Read(ref oldReleaseCount));
        Assert.AreEqual(1, oldResolver.CallCount);
        Assert.AreEqual(1, newResolver.CallCount);

        presenter.Dispose();
        newPending.SetResult(null);
    }

    [TestMethod]
    public void ResolverFailureAndNullResolverKeepAlternativeText()
    {
        EnsureGdi();
        using (var noResolver = new MarkdownPresenter { Markdown = "![alternative](image.png)" })
        {
            noResolver.Measure(new Size(240, 100));
            Assert.IsNotNull(noResolver.DocumentRoot);
        }

        using var throwing = new MarkdownPresenter
        {
            Markdown = "![alternative](image.png)",
            ImageResolver = new ThrowingResolver()
        };
        throwing.Measure(new Size(240, 100));
        Assert.IsNotNull(throwing.DocumentRoot);
    }

    [TestMethod]
    public void PendingCompletionDoesNotMutateTreeBeforeSynchronizationDispatch()
    {
        EnsureGdi();
        var pending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int imageCreationCount = 0;
        var source = new TestImageSource(() => Interlocked.Increment(ref imageCreationCount));
        int releaseCount = 0;
        var resolver = new PendingResolver(pending);
        var presenter = new MarkdownPresenter
        {
            Markdown = "![alternative](image.png)",
            ImageResolver = resolver
        };
        SynchronizationContext? previous = SynchronizationContext.Current;
        var synchronization = new QueuedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(synchronization);
        try
        {
            presenter.Measure(new Size(240, 100));
            pending.SetResult(new MarkdownImageLease(source, () => Interlocked.Increment(ref releaseCount)));
            SpinWait.SpinUntil(() => synchronization.Count > 0, TimeSpan.FromSeconds(2));

            Assert.AreEqual(0, Volatile.Read(ref imageCreationCount));
            Assert.AreEqual(0, Volatile.Read(ref releaseCount));

            synchronization.Drain();
            SpinWait.SpinUntil(() => Volatile.Read(ref imageCreationCount) == 1, TimeSpan.FromSeconds(2));
            presenter.Dispose();
            Assert.AreEqual(1, Volatile.Read(ref releaseCount));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            presenter.Dispose();
        }
    }

    private static GdiGraphicsFactory EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
        return (GdiGraphicsFactory)Application.DefaultGraphicsFactory;
    }

    private sealed class ImmediateResolver(Func<MarkdownImageLease?> create) : IMarkdownImageResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(create());
        }
    }

    private sealed class PendingResolver(TaskCompletionSource<MarkdownImageLease?> pending) : IMarkdownImageResolver
    {
        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
            => new(pending.Task);
    }

    private sealed class UrlResolver(IReadOnlyDictionary<string, TaskCompletionSource<MarkdownImageLease?>> pending) : IMarkdownImageResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return new(pending[request.Url].Task);
        }
    }

    private sealed class ThrowingResolver : IMarkdownImageResolver
    {
        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("test resolver failure");
    }

    private sealed class TestImageSource(Action? onCreate = null) : IImageSource
    {
        public IImage CreateImage(IGraphicsFactory factory)
        {
            onCreate?.Invoke();
            return new TestImage();
        }
    }

    private sealed class TestImage : IImage
    {
        public int PixelWidth => 1;
        public int PixelHeight => 1;
        public void Dispose() { }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly object _gate = new();

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_gate)
            {
                _queue.Enqueue((callback, state));
            }
        }

        public void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) work;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }
                    work = _queue.Dequeue();
                }
                work.Callback(work.State);
            }
        }
    }
}
