using System.Text;

namespace Aprillz.MewUI.Video.Sample.Diagnostics;

public static class SampleLog
{
    private static readonly object Gate = new();
    private static readonly StringBuilder Buffer = new();

    public static event Action<string>? LineAppended;

    public static string Snapshot
    {
        get
        {
            lock (Gate)
            {
                return Buffer.ToString();
            }
        }
    }

    public static void Write(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        lock (Gate)
        {
            Buffer.AppendLine(line);
        }

        LineAppended?.Invoke(line);
    }
}