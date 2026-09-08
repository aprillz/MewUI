namespace Aprillz.MewUI.Markdown;

/// <summary>Resolves host-authorized images; implementations enforce URI, byte and pixel limits before decoding.</summary>
public interface IMarkdownImageResolver
{
    /// <summary>Returns an image lease; cancellation may be ignored, but every returned lease is released by the control.</summary>
    ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken);
}

/// <summary>Describes an image without implicitly authorizing file or network access.</summary>
public sealed record MarkdownImageRequest(string Url, Uri? ResolvedUri, string AlternativeText, string? Title);

/// <summary>Leases a source until disposal; release may return a shared source to a cache instead of destroying it.</summary>
public sealed class MarkdownImageLease : IDisposable
{
    private Action? _release;

    /// <summary>Creates a lease; null release denotes a borrowed source owned entirely by the host.</summary>
    public MarkdownImageLease(IImageSource source, Action? release = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _release = release;
    }

    /// <summary>Gets the source valid for this lease's lifetime.</summary>
    public IImageSource Source { get; }

    /// <summary>Releases this lease at most once; the source is not disposed implicitly.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
