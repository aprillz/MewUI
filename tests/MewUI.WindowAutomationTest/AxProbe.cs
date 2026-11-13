using System.Diagnostics;
using System.Globalization;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Runs AxProbe.swift, an out-of-process accessibility client, against this process. Callers await it on the UI
/// thread, which stays free to answer the probe's accessibility calls.
/// </summary>
internal static class AxProbe
{
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(120);
    private static string? _binary;

    /// <summary>Runs one probe command and returns its output lines; fails the test when the probe fails.</summary>
    public static async Task<string[]> RunAsync(string command, string target = "", string text = "")
    {
        using var process = Start(await BinaryAsync(), command, target, text);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error).WaitAsync(_probeTimeout);
        await process.WaitForExitAsync().WaitAsync(_probeTimeout);
        if (process.ExitCode != 0)
        {
            Assert.Fail($"AX probe '{command} {target}' failed with exit code {process.ExitCode}: {error.Result}");
        }

        return Lines(output.Result);
    }

    /// <summary>Starts the focus watcher, which prints "ready" once it listens and then "focus ..." per focus change.</summary>
    public static async Task<FocusWatch> WatchFocusAsync(int count)
    {
        var process = Start(await BinaryAsync(), "focus", count.ToString(CultureInfo.InvariantCulture), string.Empty);
        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(_probeTimeout);
            if (line == null)
            {
                string error = await process.StandardError.ReadToEndAsync();
                Assert.Fail($"AX focus watcher ended before it listened: {error}");
            }

            if (line.Trim() == "ready")
            {
                return new FocusWatch(process);
            }
        }
    }

    /// <summary>The probe compiled once per run; interpreting the script on every call would take seconds each.</summary>
    private static async Task<string> BinaryAsync()
    {
        if (_binary != null)
        {
            return _binary;
        }

        string output = Path.Combine(Path.GetTempPath(), "mewui-axprobe");
        var info = new ProcessStartInfo("swiftc")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "-O", Path.Combine(AppContext.BaseDirectory, "AxProbe.swift"), "-o", output })
        {
            info.ArgumentList.Add(argument);
        }

        using var compiler = Process.Start(info) ?? throw new InvalidOperationException("swiftc did not start.");
        var error = compiler.StandardError.ReadToEndAsync();
        await compiler.StandardOutput.ReadToEndAsync().WaitAsync(_probeTimeout);
        await compiler.WaitForExitAsync().WaitAsync(_probeTimeout);
        if (compiler.ExitCode != 0)
        {
            Assert.Fail($"compiling the AX probe failed: {await error}");
        }

        _binary = output;
        return output;
    }

    private static Process Start(string binary, string command, string target, string text)
    {
        var info = new ProcessStartInfo(binary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { command, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), target, text })
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("the AX probe did not start.");
    }

    /// <summary>The non-empty output lines, trimmed at the end only: leading spaces carry a tree line's depth.</summary>
    private static string[] Lines(string output)
        => output.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToArray();

    /// <summary>A running focus watcher.</summary>
    public sealed class FocusWatch : IDisposable
    {
        private readonly Process _process;

        public FocusWatch(Process process) => _process = process;

        /// <summary>The focus changes the watcher saw, as "Role|Description|...", once it has seen its count or timed out.</summary>
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
