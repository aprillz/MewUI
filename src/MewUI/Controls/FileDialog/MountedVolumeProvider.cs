namespace Aprillz.MewUI.Platform;

/// <summary>A mounted volume (drive / disk) surfaced in the dialog's locations.</summary>
/// <param name="DisplayName">Name shown for the volume.</param>
/// <param name="Path">Root path of the volume.</param>
public readonly record struct MountedVolume(string DisplayName, string Path);

/// <summary>
/// Platform seam for enumerating mounted volumes via the OS-native API. The active implementation is
/// exposed by the registered platform host (native implementations live in the platform assemblies);
/// the default reports no volumes.
/// </summary>
public interface IMountedVolumeProvider
{
    /// <summary>Returns the mounted volumes.</summary>
    IReadOnlyList<MountedVolume> GetVolumes();
}

internal static class MountedVolumeProviders
{
    /// <summary>The active provider, exposed by the registered platform host (empty when none / headless).</summary>
    public static IMountedVolumeProvider Current =>
        (Application.IsRunning ? Application.Current.PlatformHost?.MountedVolumeProvider : null) ?? EmptyMountedVolumeProvider.Instance;
}

/// <summary>No volumes (headless / unregistered platform).</summary>
internal sealed class EmptyMountedVolumeProvider : IMountedVolumeProvider
{
    public static readonly EmptyMountedVolumeProvider Instance = new();

    public IReadOnlyList<MountedVolume> GetVolumes() => Array.Empty<MountedVolume>();
}
