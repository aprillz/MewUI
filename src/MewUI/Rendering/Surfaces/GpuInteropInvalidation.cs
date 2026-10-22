namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Describes why external GPU interop resources should be revalidated before reuse.
/// </summary>
public enum GpuInteropInvalidationReason
{
    /// <summary>
    /// The platform display or monitor associated with a render target changed.
    /// </summary>
    DisplayChanged,

    /// <summary>
    /// The backend render target device changed or was recreated.
    /// </summary>
    RenderTargetDeviceChanged,

    /// <summary>
    /// The backend GPU device was lost and backend-owned GPU resources were invalidated.
    /// </summary>
    DeviceLost,

    /// <summary>
    /// An external GPU resource no longer appears compatible with the current render target.
    /// </summary>
    ExternalResourceMismatch,
}

/// <summary>
/// Event data for a notification that external GPU interop resources should be revalidated.
/// This event is notification-only; it does not suppress backend-owned resource invalidation.
/// </summary>
public sealed class GpuInteropInvalidatedEventArgs : EventArgs
{
    /// <summary>Creates event data for <paramref name="reason"/>.</summary>
    public GpuInteropInvalidatedEventArgs(
        GpuInteropInvalidationReason reason,
        bool renderTargetDeviceChanged = false,
        bool displayChanged = false,
        bool externalResourceMismatch = false,
        nint renderTargetHandle = 0)
    {
        Reason = reason;
        RenderTargetDeviceChanged = renderTargetDeviceChanged;
        DisplayChanged = displayChanged;
        ExternalResourceMismatch = externalResourceMismatch;
        RenderTargetHandle = renderTargetHandle;
    }

    /// <summary>Gets the primary reason for the invalidation.</summary>
    public GpuInteropInvalidationReason Reason { get; }

    /// <summary>Gets whether the render target device changed or was recreated.</summary>
    public bool RenderTargetDeviceChanged { get; }

    /// <summary>Gets whether the display associated with the render target changed.</summary>
    public bool DisplayChanged { get; }

    /// <summary>Gets whether an external resource was found incompatible with the render target.</summary>
    public bool ExternalResourceMismatch { get; }

    /// <summary>
    /// Optional backend/platform render target handle associated with this invalidation.
    /// For window targets this is the platform's native window handle. A value of 0 means the
    /// invalidation is factory-wide or not tied to a single render target.
    /// </summary>
    public nint RenderTargetHandle { get; }
}

/// <summary>
/// Optional capability for render devices that can notify external GPU resource owners
/// that their resources should be revalidated. General UI resources do not need to
/// subscribe; backend-owned resources are invalidated by the backend automatically.
/// </summary>
public interface IGpuInteropInvalidationSource
{
    /// <summary>Occurs when external GPU interop resources should be revalidated.</summary>
    event EventHandler<GpuInteropInvalidatedEventArgs>? GpuInteropInvalidated;
}
