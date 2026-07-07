namespace Aprillz.MewUI.Platform.Win32;

/// <summary>Windows: <see cref="DriveInfo"/> is the canonical enumeration. IsReady/VolumeLabel are not
/// probed here because they block on not-ready removable/network drives.</summary>
internal sealed class WindowsMountedVolumeProvider : IMountedVolumeProvider
{
    public IReadOnlyList<MountedVolume> GetVolumes()
    {
        var result = new List<MountedVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            result.Add(new MountedVolume(drive.Name, drive.Name));
        }
        return result;
    }
}
