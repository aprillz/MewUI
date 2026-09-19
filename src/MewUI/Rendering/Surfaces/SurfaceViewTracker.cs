namespace Aprillz.MewUI.Rendering;

/// <summary>
/// View bookkeeping shared by the backend surfaces that implement <see cref="IRetainableSurface"/>:
/// the owner asks to release, and the resources are freed once the last image view is gone.
/// </summary>
internal struct SurfaceViewTracker
{
    private int _viewCount;
    private int _releaseRequested;

    /// <summary>True while an image view still aliases the surface.</summary>
    public bool HasViews => Volatile.Read(ref _viewCount) != 0;

    public void AddView() => Interlocked.Increment(ref _viewCount);

    /// <summary>True when that was the last view and the owner already asked to release.</summary>
    public bool ReleaseView()
        => Interlocked.Decrement(ref _viewCount) == 0 && Volatile.Read(ref _releaseRequested) != 0;

    /// <summary>Records the owner's release request; true when no view remains, so the caller releases now.</summary>
    public bool RequestRelease()
    {
        Volatile.Write(ref _releaseRequested, 1);
        return Volatile.Read(ref _viewCount) == 0;
    }
}
