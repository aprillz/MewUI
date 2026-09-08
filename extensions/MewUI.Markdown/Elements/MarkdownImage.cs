using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Markdown;

internal sealed class MarkdownImage : ContentControl
{
    private readonly IMarkdownImageResolver? _resolver;
    private readonly MarkdownImageRequest _request;
    private CancellationTokenSource? _cancellation;
    private MarkdownImageLease? _lease;
    private bool _disposed;
    private bool _started;

    internal MarkdownImage(MarkdownSpan span, Uri? baseUri, IMarkdownImageResolver? resolver)
    {
        _resolver = resolver;
        _request = new MarkdownImageRequest(span.Url ?? string.Empty,
            MarkdownPresenter.ResolveUri(span.Url ?? string.Empty, baseUri), span.Text, span.Title);
        ShowAlternative();
    }

    private void ShowAlternative()
    {
        var previous = Content as FrameworkElement;
        Content = new TextBlock { Text = _request.AlternativeText, TextWrapping = TextWrapping.Wrap };
        previous?.Dispose();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (!_started && !_disposed && _resolver != null)
        {
            _started = true;
            _cancellation = new CancellationTokenSource();
            var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
            _ = LoadAsync(_cancellation.Token, dispatcher, SynchronizationContext.Current);
        }
        return base.MeasureContent(availableSize);
    }

    private async Task LoadAsync(CancellationToken cancellation, IDispatcher? dispatcher, SynchronizationContext? synchronization)
    {
        MarkdownImageLease? result = null;
        try
        {
            var pending = _resolver!.ResolveAsync(_request, cancellation);
            bool synchronous = pending.IsCompleted;
            result = await pending.ConfigureAwait(false);
            if (result == null)
            {
                return;
            }
            if (cancellation.IsCancellationRequested)
            {
                result.Dispose();
                return;
            }

            var lease = result;
            var registration = cancellation.Register(lease.Dispose);
            void Apply()
            {
                registration.Dispose();
                if (_disposed || cancellation.IsCancellationRequested)
                {
                    lease.Dispose();
                    return;
                }
                var previous = Content as FrameworkElement;
                Content = new Image { Source = lease.Source, StretchMode = Stretch.Uniform, MaxHeight = 2048 };
                previous?.Dispose();
                _lease = lease;
            }

            if (synchronous)
            {
                Apply();
            }
            else if (dispatcher != null)
            {
                dispatcher.BeginInvoke(Apply);
            }
            else if (synchronization != null)
            {
                synchronization.Post(_ => Apply(), null);
            }
            else
            {
                registration.Dispose();
                lease.Dispose();
            }
        }
        catch (Exception)
        {
            result?.Dispose();
            // Resolver failure keeps alternative text and never changes the current document from a worker thread.
        }
    }

    private void Reset()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        ShowAlternative();
        _lease?.Dispose();
        _lease = null;
        _started = false;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (oldRoot != null && oldRoot != newRoot)
        {
            Reset();
        }
    }

    protected override void OnDispose()
    {
        _disposed = true;
        Reset();
        var previous = Content as FrameworkElement;
        Content = null;
        previous?.Dispose();
        base.OnDispose();
    }
}
