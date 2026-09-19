namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Abstract interface for image resources.
/// </summary>
public interface IImage : IDisposable
{
    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    int PixelWidth { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    int PixelHeight { get; }

    /// <summary>
    /// Gets the size of the image.
    /// </summary>
    Size Size => new(PixelWidth, PixelHeight);

    /// <summary>
    /// Asks the image to invoke <paramref name="callback"/> when its backend-specific
    /// GPU/NVG references have actually been released - not just when
    /// <see cref="IDisposable.Dispose"/> returns. Returns <see langword="true"/> when the
    /// backend accepted the callback (will defer-fire from its release drain);
    /// <see langword="false"/> when no deferral is implemented and the caller must run
    /// the equivalent cleanup itself after <c>Dispose</c>.
    /// <para/>
    /// Used by <c>DefaultFilterContext.AcquireScratch</c> to delay returning the scratch
    /// <see cref="IRenderSurface"/> to its pool until any zero-copy NVG draw
    /// referencing the underlying texture has flushed - without this, the next acquire in
    /// the same filter eval can recycle the RT and overwrite the texture mid-flight,
    /// surfacing as cross-filter content bleed when UI invalidate races a render.
    /// <para/>
    /// Default returns <see langword="false"/> for backends whose images aren't shared
    /// zero-copy with the NVG draw queue (no race possible - synchronous cleanup is fine).
    /// </summary>
    bool TrySetPostReleaseCallback(Action callback) => false;
}

/// <summary>
/// An image that can hand out an independent handle to the same pixels, so recorded drawing data
/// keeps drawing after the original owner disposes its handle.
/// </summary>
internal interface IRetainableImage
{
    IImage Retain();
}

/// <summary>
/// A surface whose image views keep it alive. The real release waits for the last view, so a frame
/// recorded from it can still draw after the owner has let the surface go.
/// </summary>
internal interface IRetainableSurface
{
    /// <summary>True while an image view still aliases this surface.</summary>
    bool HasSurfaceViews { get; }

    void AddSurfaceView();

    void ReleaseSurfaceView();
}

internal interface IBackendImageProvider
{
    IImage BackendImage { get; }
}

internal static class ImageResource
{
    public static IImage ResolveBackendImage(IImage image)
    {
        while (image is IBackendImageProvider provider)
        {
            image = provider.BackendImage;
        }
        return image;
    }

    /// <summary>
    /// Wraps an image that aliases <paramref name="owner"/> so the surface outlives every handle.
    /// A recording that snapshots the view takes a handle of its own, which is what lets a window
    /// keep its retained path while a control rebuilds or releases the surface underneath.
    /// </summary>
    public static IImage WrapSurfaceView(IImage backendImage, IRetainableSurface owner)
    {
        owner.AddSurfaceView();
        return new SurfaceViewImage(new SurfaceViewState(backendImage, owner));
    }

    private sealed class SurfaceViewState(IImage backendImage, IRetainableSurface owner)
    {
        private int _handles = 1;

        internal IImage BackendImage { get; } = backendImage;

        internal void AddHandle() => Interlocked.Increment(ref _handles);

        internal void ReleaseHandle()
        {
            if (Interlocked.Decrement(ref _handles) != 0)
            {
                return;
            }

            BackendImage.Dispose();
            owner.ReleaseSurfaceView();
        }
    }

    private sealed class SurfaceViewImage(SurfaceViewState state)
        : IImage, IBackendImageProvider, IRetainableImage
    {
        private SurfaceViewState? _state = state;

        private SurfaceViewState State => Volatile.Read(ref _state)
            ?? throw new ObjectDisposedException(nameof(SurfaceViewImage));

        public int PixelWidth => State.BackendImage.PixelWidth;
        public int PixelHeight => State.BackendImage.PixelHeight;

        IImage IBackendImageProvider.BackendImage => State.BackendImage;

        public bool TrySetPostReleaseCallback(Action callback)
            => State.BackendImage.TrySetPostReleaseCallback(callback);

        IImage IRetainableImage.Retain()
        {
            var current = State;
            current.AddHandle();
            return new SurfaceViewImage(current);
        }

        public void Dispose() => Interlocked.Exchange(ref _state, null)?.ReleaseHandle();
    }

    public static IImage WrapLogical(IImage image, int pixelWidth, int pixelHeight)
        => image.PixelWidth == pixelWidth && image.PixelHeight == pixelHeight
            ? image
            : new LogicalBackendImageView(image, pixelWidth, pixelHeight);

    private sealed class LogicalBackendImageView(IImage backendImage, int pixelWidth, int pixelHeight)
        : IImage, IBackendImageProvider, IRetainableImage
    {
        private IImage? _backendImage = backendImage;

        public int PixelWidth { get; } = pixelWidth;
        public int PixelHeight { get; } = pixelHeight;

        IImage IBackendImageProvider.BackendImage => Volatile.Read(ref _backendImage)
            ?? throw new ObjectDisposedException(nameof(LogicalBackendImageView));

        public bool TrySetPostReleaseCallback(Action callback)
            => Volatile.Read(ref _backendImage)?.TrySetPostReleaseCallback(callback) == true;

        IImage IRetainableImage.Retain()
        {
            var backendImage = Volatile.Read(ref _backendImage)
                ?? throw new ObjectDisposedException(nameof(LogicalBackendImageView));
            if (backendImage is not IRetainableImage retainableImage)
            {
                throw new NotSupportedException("The backend image does not support retained leases.");
            }
            return WrapLogical(retainableImage.Retain(), PixelWidth, PixelHeight);
        }

        public void Dispose() => Interlocked.Exchange(ref _backendImage, null)?.Dispose();
    }
}
