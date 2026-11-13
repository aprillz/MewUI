using System.Diagnostics;
using System.Globalization;
using Aprillz.MewUI;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Runs UiaProbe.ps1, an out-of-process UI Automation client, against a window. Callers await it on the UI thread,
/// which stays free to answer the probe's provider calls.
/// </summary>
internal static class UiaProbe
{
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Runs one probe command and returns its output lines; fails the test when the probe fails.</summary>
    public static async Task<string[]> RunAsync(Window window, string command, string target = "", string text = "")
    {
        using var process = Start(window, command, target, text, count: 1);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error).WaitAsync(_probeTimeout);
        await process.WaitForExitAsync().WaitAsync(_probeTimeout);
        if (process.ExitCode != 0)
        {
            Assert.Fail($"UIA probe '{command} {target}' failed with exit code {process.ExitCode}: {error.Result}");
        }

        return Lines(output.Result);
    }

    /// <summary>
    /// Starts the focus watcher, which prints "ready" once it listens and then "focus Type|Name" for each focus change
    /// in this process, until it has seen <paramref name="count"/> of them.
    /// </summary>
    public static async Task<FocusWatch> WatchFocusAsync(Window window, int count)
    {
        var process = Start(window, "focus", string.Empty, string.Empty, count);
        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(_probeTimeout);
            if (line == null)
            {
                string error = await process.StandardError.ReadToEndAsync();
                Assert.Fail($"UIA focus watcher ended before it listened: {error}");
            }

            if (line.Trim() == "ready")
            {
                return new FocusWatch(process);
            }
        }
    }

    private static Process Start(Window window, string command, string target, string text, int count)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(AppContext.BaseDirectory, "UiaProbe.ps1"),
            "-Command", command,
            "-Window", window.Handle.ToString(CultureInfo.InvariantCulture),
            "-Target", target,
            "-Text", text,
            "-Count", count.ToString(CultureInfo.InvariantCulture),
        })
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("powershell.exe did not start.");
    }

    /// <summary>The non-empty output lines, trimmed at the end only: leading spaces carry a tree line's depth.</summary>
    private static string[] Lines(string output)
        => output.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToArray();

    /// <summary>A running focus watcher.</summary>
    public sealed class FocusWatch : IDisposable
    {
        private readonly Process _process;

        public FocusWatch(Process process) => _process = process;

        /// <summary>The focus changes the watcher saw, as "Type|Name", once it has seen its count or timed out.</summary>
        public async Task<string[]> CollectAsync()
        {
            string output = await _process.StandardOutput.ReadToEndAsync().WaitAsync(_probeTimeout);
            await _process.WaitForExitAsync().WaitAsync(_probeTimeout);
            return Lines(output)
                .Where(line => line.StartsWith("focus ", StringComparison.Ordinal))
                .Select(line => line["focus ".Length..])
                .ToArray();
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }

            _process.Dispose();
        }
    }
}
