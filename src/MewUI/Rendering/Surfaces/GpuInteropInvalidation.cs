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

    public GpuInteropInvalidationReason Reason { get; }

    public bool RenderTargetDeviceChanged { get; }

    public bool DisplayChanged { get; }

    public bool ExternalResourceMismatch { get; }

    /// <summary>
    /// Optional backend/platform render target handle associated with this invalidation.
    /// On Win32 window targets this is the HWND. A value of 0 means the invalidation is
    /// factory-wide or not tied to a single render target.
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
    event EventHandler<GpuInteropInvalidatedEventArgs>? GpuInteropInvalidated;
}
