namespace Aprillz.MewUI;

/// <summary>
/// Simple one-shot "request/consume" channel used by tests/tools to trigger a screenshot or capture operation.
/// </summary>
public static class SmokeCapture
{
    private static string? _pendingPath;

    /// <summary>
    /// Requests a capture to be written to the specified path.
    /// Subsequent calls replace the pending request.
    /// </summary>
    /// <param name="path">Output path.</param>
    public static void Request(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Interlocked.Exchange(ref _pendingPath, path);
    }

    /// <summary>
    /// Attempts to consume the pending capture request.
    /// </summary>
    /// <param name="path">The requested output path on success.</param>
    /// <returns><see langword="true"/> if a pending request existed; otherwise, <see langword="false"/>.</returns>
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
