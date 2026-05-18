using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Identifies a backend GPU device for compatibility checks as an opaque
/// (IdLow, IdHigh, NativeHandle) tuple. Equality is structural — two devices are equal
/// when all three fields match, which is enough to gate zero-copy interop without the
/// Core type carrying any GPU API name. Per-backend packing lives next to the backend.
/// </summary>
public readonly record struct GpuDeviceIdentity(ulong IdLow, long IdHigh, nint NativeHandle)
{
    public bool IsEmpty => IdLow == 0 && IdHigh == 0 && NativeHandle == 0;
}

/// <summary>
/// Describes where a GPU resource belongs. Backends use this as a compatibility hint
/// before attempting zero-copy import/sample paths.
/// </summary>
public readonly record struct GpuResourceAffinity(PlatformDisplayIdentity? Display, GpuDeviceIdentity? Device)
{
    public bool IsEmpty => Display is null && Device is null;
}

/// <summary>
/// Optional capability for external GPU resources and leases that can report their
/// device/display affinity.
/// </summary>
public interface IGpuResourceAffinityProvider
{
    GpuResourceAffinity? Affinity { get; }
}
