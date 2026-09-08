using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Markdown;

internal sealed class MarkdownInlineImage : IInlineTextObject, IDisposable
{
    private const double MAX_DIMENSION = 2048;

    private readonly IMarkdownImageResolver? _resolver;
    private readonly MarkdownImageRequest _request;
    private readonly Action _changed;
    private CancellationTokenSource? _cancellation;
    private CancellationTokenRegistration _leaseRegistration;
    private MarkdownImageLease? _lease;
    private IImage? _image;
    private IGraphicsFactory? _factory;
    private IImageSource? _source;
    private double _maxWidth = double.PositiveInfinity;
    private bool _started;
    private bool _failed;
    private bool _disposed;

    internal MarkdownInlineImage(
        MarkdownSpan span,
        Uri? baseUri,
        IMarkdownImageResolver? resolver,
        Action changed)
    {
        _resolver = resolver;
        _request = new MarkdownImageRequest(
            span.Url ?? string.Empty,
            MarkdownPresenter.ResolveUri(span.Url ?? string.Empty, baseUri),
            span.Text,
            span.Title);
        _changed = changed;
    }

    internal bool IsReady => _source != null && !_failed;

    internal void Start(IDispatcher? dispatcher, SynchronizationContext? synchronization)
    {
        if (_started || _disposed || _resolver == null)
        {
            return;
        }

        _started = true;
        _cancellation = new CancellationTokenSource();
        _ = LoadAsync(_cancellation.Token, dispatcher, synchronization);
    }

    internal bool TryPrepare(IGraphicsFactory factory, double maxWidth)
    {
        if (!IsReady)
        {
            return false;
        }

        _maxWidth = double.IsFinite(maxWidth) ? Math.Max(1, maxWidth) : double.PositiveInfinity;

        if (_source is IVectorImageSource)
        {
            return true;
        }

        if (_image != null && ReferenceEquals(_factory, factory))
        {
            return true;
        }

        try
        {
            _image?.Dispose();
            if (_source is ImageSource imageSource && imageSource.PixelWidth > 0 && imageSource.PixelHeight > 0)
            {
                double scale = Math.Min(1, Math.Min(MAX_DIMENSION / imageSource.PixelWidth,
                    MAX_DIMENSION / imageSource.PixelHeight));
                int targetWidth = Math.Max(1, (int)Math.Ceiling(imageSource.PixelWidth * scale));
                int targetHeight = Math.Max(1, (int)Math.Ceiling(imageSource.PixelHeight * scale));
                _image = imageSource.CreateImage(factory, targetWidth, targetHeight);
            }
            else
            {
                _image = _source!.CreateImage(factory);
            }
            _factory = factory;
            return _image.PixelWidth > 0 && _image.PixelHeight > 0;
        }
        catch (Exception)
        {
            _image?.Dispose();
            _image = null;
            _factory = null;
            _failed = true;
            _lease?.Dispose();
            _lease = null;
            _source = null;
            return false;
        }
    }

    public InlineMetrics Measure()
    {
        if (!IsReady)
        {
            return new InlineMetrics(0, 0, 0);
        }

        Size size = GetIntrinsicSize();
        return new InlineMetrics(size.Width, size.Height, size.Height);
    }

    public void Draw(ITextRenderContext context, Point origin)
    {
        if (!IsReady)
        {
            return;
        }

        Size size = GetIntrinsicSize();
        Rect destination = new(origin.X, origin.Y, size.Width, size.Height);
        if (_source is IVectorImageSource vector)
        {
            vector.Render(context.Graphics, destination);
        }
        else if (_image != null)
        {
            Size orientedSize = OrientationTransform.GetOrientedSize(
                GetOrientation(), _image.PixelWidth, _image.PixelHeight);
            context.Graphics.DrawImageOriented(
                _image,
                GetOrientation(),
                _image.PixelWidth,
                _image.PixelHeight,
                new Rect(0, 0, orientedSize.Width, orientedSize.Height),
                destination);
        }
    }

    private async Task LoadAsync(
        CancellationToken cancellation,
        IDispatcher? dispatcher,
        SynchronizationContext? synchronization)
    {
        MarkdownImageLease? result = null;
        try
        {
            ValueTask<MarkdownImageLease?> pending = _resolver!.ResolveAsync(_request, cancellation);
            bool synchronous = pending.IsCompleted;
            result = await pending.ConfigureAwait(false);
            if (result == null)
            {
                return;
            }

            void Apply()
            {
                if (_disposed || cancellation.IsCancellationRequested)
                {
                    result.Dispose();
                    return;
                }

                CancellationTokenRegistration registration = cancellation.Register(result.Dispose);
                if (_disposed || cancellation.IsCancellationRequested)
                {
                    registration.Dispose();
                    result.Dispose();
                    return;
                }

                _leaseRegistration = registration;
                _lease = result;
                _source = result.Source;
                _changed();
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
                result.Dispose();
            }
        }
        catch (Exception)
        {
            result?.Dispose();
        }
    }

    private Size GetIntrinsicSize()
    {
        Size intrinsic;
        if (_source is IVectorImageSource vector)
        {
            intrinsic = vector.IntrinsicSize;
        }
        else
        {
            intrinsic = OrientationTransform.GetOrientedSize(GetOrientation(), GetRawWidth(), GetRawHeight());
        }

        if (intrinsic.Width <= 0 || intrinsic.Height <= 0)
        {
            return Size.Empty;
        }

        double scale = Math.Min(1, Math.Min(MAX_DIMENSION / intrinsic.Width, MAX_DIMENSION / intrinsic.Height));
        if (double.IsFinite(_maxWidth))
        {
            scale = Math.Min(scale, _maxWidth / intrinsic.Width);
        }
        return new Size(intrinsic.Width * scale, intrinsic.Height * scale);
    }

    private int GetRawWidth() => _source is ImageSource imageSource && imageSource.PixelWidth > 0
        ? imageSource.PixelWidth
        : _image?.PixelWidth ?? 0;

    private int GetRawHeight() => _source is ImageSource imageSource && imageSource.PixelHeight > 0
        ? imageSource.PixelHeight
        : _image?.PixelHeight ?? 0;

    private ImageOrientation GetOrientation()
        => _source is IOrientedImageSource oriented
            ? OrientationTransform.Normalize(oriented.Orientation)
            : ImageOrientation.Normal;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _leaseRegistration.Dispose();
        _leaseRegistration = default;
        _image?.Dispose();
        _image = null;
        _lease?.Dispose();
        _lease = null;
        _source = null;
        _factory = null;
    }
}
