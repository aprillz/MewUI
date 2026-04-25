using System.Diagnostics;

namespace Aprillz.MewUI
{
    [DebuggerStepThrough]
    public static class StopwatchExtensions
    {
        public static string Mark(this Stopwatch sw, bool writeDebug = true)
        {
            var elapsed = sw.Elapsed;
            string message = GetText(elapsed);

            if (writeDebug)
            {
                Debug.WriteLine(message);
            }

            sw.Restart();

            return message;
        }

        public static string Mark(this Stopwatch sw, string text, bool writeDebug = true)
        {
            var elapsed = sw.Elapsed;
            var message = $"{text}: {GetText(elapsed)}";

            if (writeDebug)
            {
                Debug.WriteLine(message);
            }

            sw.Restart();

            return message;
        }

        public static string GetText(this TimeSpan elapsed)
        {
            string message;
            if (elapsed.TotalMilliseconds < 1)
            {
                message = $"{elapsed.TotalMilliseconds * 1000:0.0} ㎲";
            }
            else if (elapsed.TotalSeconds < 1)
            {
                message = $"{elapsed.TotalMilliseconds:0.000} ms";
            }
            else
            {
                message = $"{elapsed.TotalSeconds:0.000} sec";
            }

            return message;
        }
    }
}
