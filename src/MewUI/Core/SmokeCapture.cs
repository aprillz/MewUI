using System.Threading;

namespace Aprillz.MewUI.Core;

public static class SmokeCapture
{
    private static string? _pendingPath;

    public static void Request(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        Interlocked.Exchange(ref _pendingPath, path);
    }

    public static bool TryConsume(out string path)
    {
        var p = Interlocked.Exchange(ref _pendingPath, null);
        if (string.IsNullOrWhiteSpace(p))
        {
            path = string.Empty;
            return false;
        }

        path = p;
        return true;
    }
}
