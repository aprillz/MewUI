namespace Aprillz.MewUI.Platform.Linux.X11;

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
