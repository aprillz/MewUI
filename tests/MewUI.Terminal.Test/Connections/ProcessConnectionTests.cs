using System.Collections.Concurrent;
using System.Text;

using Aprillz.MewUI.Terminal;

namespace MewUI.Terminal.Test.Connections;

[TestClass]
public sealed class ProcessConnectionTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    /// <summary>Collects a connection's output as text and its exit code.</summary>
    private sealed class OutputCollector
    {
        private readonly StringBuilder _text = new();
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly BlockingCollection<bool> _changes = new();

        public OutputCollector(ITerminalConnection connection)
        {
            connection.Output += OnOutput;
            connection.Closed += exitCode => _closed.TrySetResult(exitCode);
        }

        public Task<int> Closed => _closed.Task;

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        private void OnOutput(ReadOnlySpan<byte> data)
        {
            Span<char> chars = stackalloc char[Encoding.UTF8.GetMaxCharCount(data.Length)];
            lock (_text)
            {
                int count = _decoder.GetChars(data, chars, false);
                _text.Append(chars[..count]);
            }

            _changes.Add(true);
        }

        /// <summary>Waits until the output contains <paramref name="expected"/>.</summary>
        public bool WaitFor(string expected)
        {
            var deadline = DateTime.UtcNow + _timeout;
            while (!Text.Contains(expected, StringComparison.Ordinal))
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !_changes.TryTake(out _, remaining))
                {
                    return Text.Contains(expected, StringComparison.Ordinal);
                }
            }

            return true;
        }
    }

    [TestMethod]
    public void RunsCommandAndReportsExit()
    {
        ProcessConnection connection;
        if (OperatingSystem.IsWindows())
        {
            connection = new ProcessConnection("cmd.exe", ["/c", "echo mew-terminal-output& exit 3"]);
        }
        else
        {
            connection = new ProcessConnection("/bin/sh", ["-c", "echo mew-terminal-output; exit 3"]);
        }

        using (connection)
        {
            var collector = new OutputCollector(connection);
            connection.Start();

            Assert.IsTrue(collector.WaitFor("mew-terminal-output"), $"Output was: {collector.Text}");
            Assert.IsTrue(collector.Closed.Wait(_timeout), "The connection did not close.");
            Assert.AreEqual(3, collector.Closed.Result);
        }
    }

    [TestMethod]
    public void StartingTitleIsTheConsoleTitle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Only a Windows console has a starting title.");
        }

        // The pseudo console reports the title it starts with as a title sequence, before the program changes it.
        using var connection = new ProcessConnection("cmd.exe", ["/c", "echo mew-terminal-output"]) { StartingTitle = "mew-starting-title" };
        var collector = new OutputCollector(connection);
        connection.Start();

        Assert.IsTrue(collector.WaitFor("mew-starting-title"), $"Output was: {collector.Text}");
        Assert.IsTrue(collector.Closed.Wait(_timeout), "The connection did not close.");
    }

    [TestMethod]
    public void InputReachesProgram()
    {
        ProcessConnection connection;
        string command;
        if (OperatingSystem.IsWindows())
        {
            connection = new ProcessConnection("cmd.exe", ["/q", "/k", "prompt $g"]);
            command = "echo mew-%OS%-input\r";
        }
        else
        {
            connection = new ProcessConnection("/bin/sh");
            command = "echo mew-$((40+2))-input\r";
        }

        using (connection)
        {
            var collector = new OutputCollector(connection);
            connection.Start();

            connection.Write(Encoding.UTF8.GetBytes(command));

            // The shell expands the variable or arithmetic, so the result proves the program ran the input.
            string expected = OperatingSystem.IsWindows() ? "mew-Windows_NT-input" : "mew-42-input";
            Assert.IsTrue(collector.WaitFor(expected), $"Output was: {collector.Text}");
        }
    }

    [TestMethod]
    public void ResizeReachesProgram()
    {
        ProcessConnection connection;
        string command;
        string expected;
        if (OperatingSystem.IsWindows())
        {
            connection = new ProcessConnection("powershell.exe", ["-NoLogo", "-NoProfile"]) { InitialColumns = 80, InitialRows = 24 };
            command = "'mew-size-' + [Console]::WindowWidth + 'x' + [Console]::WindowHeight\r";
            expected = "mew-size-100x30";
        }
        else
        {
            connection = new ProcessConnection("/bin/sh") { InitialColumns = 80, InitialRows = 24 };
            command = "echo mew-size-$(stty size | tr ' ' x)\r";
            expected = "mew-size-30x100";
        }

        using (connection)
        {
            var collector = new OutputCollector(connection);
            connection.Start();
            connection.Resize(100, 30);

            connection.Write(Encoding.UTF8.GetBytes(command));
            Assert.IsTrue(collector.WaitFor(expected), $"Output was: {collector.Text}");
        }
    }

    [TestMethod]
    public void CommandLineQuotesArguments()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string commandLine = ConPtyProcess.BuildCommandLine(@"C:\Program Files\app.exe", ["plain", "with space", "quote\"inside", @"trailing\", ""]);
        Assert.AreEqual("\"C:\\Program Files\\app.exe\" plain \"with space\" \"quote\\\"inside\" trailing\\ \"\"", commandLine);
    }
}
