using System.Text;

namespace Aprillz.MewUI.Core;

internal static class DiagLog
{
    private static readonly object _sync = new();

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("MEWUI_DIAG"), "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("MEWUI_DIAG"), "true", StringComparison.OrdinalIgnoreCase);

    public static void Write(string message)
    {
        if (!Enabled)
            return;

        try
        {
            lock (_sync)
            {
                File.AppendAllText(GetPath(), $"{DateTime.UtcNow:O} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static string GetPath()
    {
        var path = Environment.GetEnvironmentVariable("MEWUI_DIAG_PATH");
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        return Path.Combine(AppContext.BaseDirectory, "mewui_diag.log");
    }
}

