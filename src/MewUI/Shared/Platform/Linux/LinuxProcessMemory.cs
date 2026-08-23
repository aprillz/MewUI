using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Platform.Linux;

/// <summary>Reads the process memory counters from the kernel's per-process status file.</summary>
internal static class LinuxProcessMemory
{
    public static ProcessMemory Read()
    {
        long rss = 0;
        long rssAnon = 0;
        long data = 0;
        long stack = 0;
        try
        {
            foreach (var line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith("VmRSS:", StringComparison.Ordinal)) rss = ParseKb(line);
                else if (line.StartsWith("RssAnon:", StringComparison.Ordinal)) rssAnon = ParseKb(line);
                else if (line.StartsWith("VmData:", StringComparison.Ordinal)) data = ParseKb(line);
                else if (line.StartsWith("VmStk:", StringComparison.Ordinal)) stack = ParseKb(line);
            }
        }
        catch (IOException)
        {
            return new ProcessMemory(0, Environment.WorkingSet, 0);
        }

        return new ProcessMemory(data + stack, rss, rssAnon);
    }

    private static long ParseKb(string line)
    {
        var span = line.AsSpan(line.IndexOf(':') + 1).Trim();
        int end = span.IndexOf(' ');
        if (end >= 0)
        {
            span = span[..end];
        }

        return long.TryParse(span, out long kb) ? kb * 1024 : 0;
    }
}
