using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Platform.MacOS;

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
