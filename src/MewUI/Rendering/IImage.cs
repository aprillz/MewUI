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

    public static IImage WrapLogical(IImage image, int pixelWidth, int pixelHeight)
        => image.PixelWidth == pixelWidth && image.PixelHeight == pixelHeight
            ? image
            : new LogicalBackendImageView(image, pixelWidth, pixelHeight);

    private sealed class LogicalBackendImageView(IImage backendImage, int pixelWidth, int pixelHeight)
        : IImage, IBackendImageProvider
    {
        private IImage? _backendImage = backendImage;

        public int PixelWidth { get; } = pixelWidth;
        public int PixelHeight { get; } = pixelHeight;

        IImage IBackendImageProvider.BackendImage => Volatile.Read(ref _backendImage)
            ?? throw new ObjectDisposedException(nameof(LogicalBackendImageView));

        public bool TrySetPostReleaseCallback(Action callback)
            => Volatile.Read(ref _backendImage)?.TrySetPostReleaseCallback(callback) == true;

        public void Dispose() => Interlocked.Exchange(ref _backendImage, null)?.Dispose();
    }
}
