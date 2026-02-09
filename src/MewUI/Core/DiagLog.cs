using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Best-effort diagnostic logger for development and troubleshooting.
/// When diagnostics are disabled, log calls become no-ops.
/// </summary>
public static class DiagLog
{
    private static readonly object _sync = new();

    /// <summary>
    /// Gets whether diagnostics logging is enabled.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Writes a diagnostic message in DEBUG builds only.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                Debug.WriteLine(message);
                File.AppendAllText(WritePath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Writes a diagnostic message regardless of build configuration.
    /// </summary>
    public static void WriteAlways(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                Debug.WriteLine(message);
                File.AppendAllText(WritePath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Writes a snapshot of process and GC memory statistics.
    /// </summary>
    /// <param name="tag">Caller-provided tag to identify the point-in-time snapshot.</param>
    public static void WriteProcessMemory(string tag)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            using var p = Process.GetCurrentProcess();
            long managed = GC.GetTotalMemory(forceFullCollection: false);
            long heap = 0;
            try
            {
                heap = GC.GetGCMemoryInfo().HeapSizeBytes;
            }
            catch
            {
                // best-effort
            }

            WriteAlways(
                $"[Mem] {tag} pid={p.Id} private={FormatBytes(p.PrivateMemorySize64)} ws={FormatBytes(p.WorkingSet64)} " +
                $"vm={FormatBytes(p.VirtualMemorySize64)} managed={FormatBytes(managed)} heap={FormatBytes(heap)}");
        }
        catch
        {
            // best-effort
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return $"{bytes}B";
        }

        double mib = bytes / (1024.0 * 1024.0);
        if (mib < 1024)
        {
            return $"{mib:F1}MiB";
        }

        return $"{mib / 1024.0:F2}GiB";
    }

    private static string WritePath => field ??= Path.Combine(AppContext.BaseDirectory, "mewui_diag.log");
}
