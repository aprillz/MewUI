using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Platform;

/// <summary>A mounted volume (drive / disk) surfaced in the dialog's locations.</summary>
internal readonly record struct MountedVolume(string DisplayName, string Path);

/// <summary>
/// Platform seam for enumerating mounted volumes via the OS-native API.
/// <para>
/// Core port mapping - each implementation moves into its platform assembly and is registered like
/// <c>IPlatformHost</c> / <c>IFileDialogService</c>:
/// <list type="bullet">
///   <item><c>Aprillz.MewUI.Platform.Win32</c>  -> Windows impl (DriveInfo + volume labels)</item>
///   <item><c>Aprillz.MewUI.Platform.MacOS</c>  -> macOS impl (getmntinfo / NSFileManager.mountedVolumeURLs)</item>
///   <item><c>Aprillz.MewUI.Platform.X11</c>    -> Linux impl (/proc/mounts, optionally GVfs/UDisks2)</item>
/// </list>
/// The core abstraction is <c>IMountedVolumeProvider</c>; <see cref="MountedVolumeProviders.ForCurrentOS"/> here mirrors the
/// platform-host registration that resolves the active implementation.
/// </para>
/// </summary>
internal interface IMountedVolumeProvider
{
    IReadOnlyList<MountedVolume> GetVolumes();
}

internal static class MountedVolumeProviders
{
    public static IMountedVolumeProvider ForCurrentOS()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacMountedVolumeProvider();
        }
        if (OperatingSystem.IsWindows())
        {
            return new WindowsMountedVolumeProvider();
        }
        return new UnixMountedVolumeProvider();
    }
}

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

/// <summary>macOS: getmntinfo(3). Shows the boot volume once at "/" plus mounts under /Volumes,
/// hiding system mounts (/dev, /System/Volumes/*, /private/*) like Finder.</summary>
internal sealed partial class MacMountedVolumeProvider : IMountedVolumeProvider
{
    [LibraryImport("libc", EntryPoint = "getmntinfo")]
    private static partial int GetMntInfo(out IntPtr mntbufp, int flags);

    // struct statfs (Darwin, 64-bit): size 2168; f_mntonname at offset 88
    // (numeric fields + f_fstypename[16] at 72). f_mntfromname follows at 1112.
    private const int StatfsSize = 2168;
    private const int MntOnNameOffset = 88;
    private const int MntNoWait = 2;

    public IReadOnlyList<MountedVolume> GetVolumes()
    {
        var result = new List<MountedVolume>();
        try
        {
            int count = GetMntInfo(out IntPtr buffer, MntNoWait);
            if (count <= 0 || buffer == IntPtr.Zero)
            {
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                IntPtr entry = IntPtr.Add(buffer, i * StatfsSize);
                string mountPoint = Marshal.PtrToStringUTF8(IntPtr.Add(entry, MntOnNameOffset)) ?? string.Empty;

                if (mountPoint == "/")
                {
                    result.Add(new MountedVolume("Macintosh HD", "/"));
                }
                else if (mountPoint.StartsWith("/Volumes/", StringComparison.Ordinal))
                {
                    result.Add(new MountedVolume(Path.GetFileName(mountPoint), mountPoint));
                }
            }
        }
        catch
        {
            // getmntinfo unavailable.
        }
        return result;
    }
}

/// <summary>Linux/Unix: parse /proc/mounts, skipping pseudo filesystems; surface "/" plus removable
/// mounts under /media, /run/media, /mnt.</summary>
internal sealed class UnixMountedVolumeProvider : IMountedVolumeProvider
{
    private static readonly HashSet<string> _pseudoFilesystems = new(StringComparer.Ordinal)
    {
        "proc", "sysfs", "tmpfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "mqueue", "debugfs",
        "tracefs", "securityfs", "pstore", "bpf", "configfs", "fusectl", "autofs", "hugetlbfs",
        "binfmt_misc", "ramfs", "efivarfs", "rpc_pipefs", "nsfs", "overlay", "squashfs", "fuse.gvfsd-fuse",
    };

    public IReadOnlyList<MountedVolume> GetVolumes()
    {
        var result = new List<MountedVolume>();
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    continue;
                }

                string mountPoint = Unescape(parts[1]);
                string fsType = parts[2];
                if (_pseudoFilesystems.Contains(fsType))
                {
                    continue;
                }

                if (mountPoint == "/")
                {
                    result.Add(new MountedVolume("File System", "/"));
                }
                else if (mountPoint.StartsWith("/media/", StringComparison.Ordinal)
                    || mountPoint.StartsWith("/run/media/", StringComparison.Ordinal)
                    || mountPoint.StartsWith("/mnt/", StringComparison.Ordinal))
                {
                    result.Add(new MountedVolume(Path.GetFileName(mountPoint.TrimEnd('/')), mountPoint));
                }
            }
        }
        catch
        {
            // /proc/mounts unavailable.
        }
        return result;
    }

    // /proc/mounts escapes spaces/tabs as octal (\040, \011).
    private static string Unescape(string value)
        => value.Replace("\\040", " ").Replace("\\011", "\t");
}
