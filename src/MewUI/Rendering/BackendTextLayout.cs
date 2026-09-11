using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Backend-private text run measurement and native-handle carrier.
/// </summary>
internal sealed class BackendTextLayout
{
    private NativeHandleLease? _backendLease;

    public required Size MeasuredSize { get; init; }

    public required Rect EffectiveBounds { get; set; }

    public required double EffectiveMaxWidth { get; init; }

    public required double ContentHeight { get; init; }

    /// <summary>
    /// Set on text-engine run realizations: ink the backend draws outside <see cref="EffectiveBounds"/>
    /// instead of clipping it. Null for immediate-mode layouts, which keep their box as the clip.
    /// </summary>
    internal TextInkOverhang? InkOverhang { get; set; }

    /// <summary>Backend-private native handle for rendering.</summary>
    internal nint BackendHandle => Volatile.Read(ref _backendLease)?.Handle ?? 0;

    internal NativeHandleLease? BackendLease => Volatile.Read(ref _backendLease);

    internal void AttachBackendHandle(nint handle, Action<nint> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        ReleaseBackendHandle();
        _backendLease = new NativeHandleLease(handle, release);
    }

    internal void ReleaseBackendHandle()
    {
        Interlocked.Exchange(ref _backendLease, null)?.Release();
    }

    ~BackendTextLayout() => ReleaseBackendHandle();
}

internal sealed class NativeHandleLease(nint handle, Action<nint> release)
{
    private nint _handle = handle;
    private Action<nint>? _release = release;

    public nint Handle => Volatile.Read(ref _handle);

    public void Release()
    {
        nint value = Interlocked.Exchange(ref _handle, 0);
        var callback = Interlocked.Exchange(ref _release, null);
        if (value != 0)
        {
            callback?.Invoke(value);
        }
    }
}
