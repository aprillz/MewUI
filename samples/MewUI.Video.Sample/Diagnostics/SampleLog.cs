namespace Aprillz.MewUI.Video.Sample.Diagnostics;

public static class SampleLog
{
    /// <summary>How many recent lines are kept for the log window; older lines are dropped.</summary>
    public const int MAX_LINES = 1000;

    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new();

    public static event Action<string>? LineAppended;

    /// <summary>The kept lines, oldest first.</summary>
    public static string[] GetLines()
    {
        lock (Gate)
        {
            return [.. Lines];
        }
    }

    public static void Write(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        lock (Gate)
        {
            Lines.Enqueue(line);
            if (Lines.Count > MAX_LINES)
            {
                Lines.Dequeue();
            }
        }

        LineAppended?.Invoke(line);
    }
}
