using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Identifies a backend GPU device for compatibility checks as an opaque
/// (IdLow, IdHigh, NativeHandle) tuple. Equality is structural - two devices are equal
/// when all three fields match, which is enough to gate zero-copy interop without the
/// Core type carrying any GPU API name. Per-backend packing lives next to the backend.
/// </summary>
/// <param name="IdLow">Low part of the opaque id.</param>
/// <param name="IdHigh">High part of the opaque id.</param>
/// <param name="NativeHandle">Native device handle, when the backend has one.</param>
public readonly record struct GpuDeviceIdentity(ulong IdLow, long IdHigh, nint NativeHandle)
{
    /// <summary>Gets whether no field is set.</summary>
    public bool IsEmpty => IdLow == 0 && IdHigh == 0 && NativeHandle == 0;
}

/// <summary>
/// Describes where a GPU resource belongs. Backends use this as a compatibility hint
/// before attempting zero-copy import/sample paths.
/// </summary>
/// <param name="Display">Display the resource is tied to, or <see langword="null"/> when it is not tied to one.</param>
/// <param name="Device">GPU device the resource belongs to, or <see langword="null"/> when unknown.</param>
public readonly record struct GpuResourceAffinity(PlatformDisplayIdentity? Display, GpuDeviceIdentity? Device)
{
    /// <summary>Gets whether neither a display nor a device is set.</summary>
    public bool IsEmpty => Display is null && Device is null;
}

/// <summary>
/// Optional capability for external GPU resources and leases that can report their
/// device/display affinity.
/// </summary>
public interface IGpuResourceAffinityProvider
{
    /// <summary>Gets the affinity, or <see langword="null"/> when the resource cannot report it.</summary>
    GpuResourceAffinity? Affinity { get; }
}
