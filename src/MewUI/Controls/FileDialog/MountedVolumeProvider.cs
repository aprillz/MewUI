namespace Aprillz.MewUI.Platform;

/// <summary>A mounted volume (drive / disk) surfaced in the dialog's locations.</summary>
public readonly record struct MountedVolume(string DisplayName, string Path);

/// <summary>
/// Platform seam for enumerating mounted volumes via the OS-native API. The active implementation is
/// exposed by the registered <see cref="IPlatformHost"/> (native impls live in the platform assemblies);
/// the default is <see cref="EmptyMountedVolumeProvider"/> (no volumes). See platform-seam-plan.md.
/// </summary>
public interface IMountedVolumeProvider
{
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
