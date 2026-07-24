namespace Aprillz.MewUI.Preview;

/// <summary>
/// Timing/diagnostic lines for the preview session, written to stdout so they surface in the IDE
/// extension's output channel (dotnet watch forwards child stdout).
/// </summary>
internal static class PreviewTrace
{
    internal static void Log(string message)
        => Console.WriteLine($"[mewui-preview {DateTime.Now:HH:mm:ss.fff}] {message}");
}
