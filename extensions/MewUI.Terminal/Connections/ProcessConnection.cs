namespace Aprillz.MewUI.Terminal;

/// <summary>
/// Runs a program of this machine on a pseudo-terminal: a ConPTY pseudo-console on Windows, a pty on Linux and macOS.
/// </summary>
public sealed class ProcessConnection : ITerminalConnection
{
    private readonly IPseudoTerminalProcess _process;

    /// <summary>Creates a connection that runs <paramref name="fileName"/> with <paramref name="arguments"/> when started.</summary>
    /// <param name="fileName">The program, as a path or a name searched on PATH.</param>
    /// <param name="arguments">The arguments, each passed as one argument.</param>
    public ProcessConnection(string fileName, IReadOnlyList<string>? arguments = null)
    {
        FileName = fileName;
        Arguments = arguments ?? Array.Empty<string>();

        if (OperatingSystem.IsWindows())
        {
            _process = new ConPtyProcess(this);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            _process = new UnixPtyProcess(this);
        }
        else
        {
            throw new PlatformNotSupportedException("Pseudo-terminals are supported on Windows, Linux and macOS.");
        }
    }

    /// <inheritdoc/>
    public event TerminalOutputHandler? Output;

    /// <inheritdoc/>
    public event Action<int>? Closed;

    /// <summary>Gets the program to run.</summary>
    public string FileName { get; }

    /// <summary>Gets the arguments of the program.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Gets or sets the working directory, or null for the current one. Takes effect at <see cref="Start"/>.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the title the program's console starts with on Windows, which a shell like cmd shows until it sets
    /// its own; null leaves the system default, the program's path. Other systems have no starting title.
    /// </summary>
    public string? StartingTitle { get; init; }

    /// <summary>Gets the environment variables to set or (with a null value) remove, over those of this process.</summary>
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Gets or sets the size in cells the program starts with.</summary>
    public int InitialColumns { get; init; } = 80;

    /// <summary>Gets or sets the size in rows the program starts with.</summary>
    public int InitialRows { get; init; } = 24;

    /// <summary>The shell a terminal opens by default: <c>%COMSPEC%</c> (cmd.exe) on Windows, <c>$SHELL</c> elsewhere.</summary>
    public static string DefaultShell
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                string? comspec = System.Environment.GetEnvironmentVariable("COMSPEC");
                return string.IsNullOrEmpty(comspec) ? Path.Combine(System.Environment.SystemDirectory, "cmd.exe") : comspec;
            }

            string? shell = System.Environment.GetEnvironmentVariable("SHELL");
            return string.IsNullOrEmpty(shell) ? "/bin/sh" : shell;
        }
    }

    /// <inheritdoc/>
    public void Start() => _process.Start();

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<byte> data) => _process.Write(data);

    /// <inheritdoc/>
    public void Resize(int columns, int rows) => _process.Resize(columns, rows);

    /// <summary>Ends the connection; the program is closed with its pseudo-terminal.</summary>
    public void Dispose() => _process.Dispose();

    internal void RaiseOutput(ReadOnlySpan<byte> data) => Output?.Invoke(data);

    internal void RaiseClosed(int exitCode) => Closed?.Invoke(exitCode);

    /// <summary>The environment of the child: this process's, with TERM and the overrides applied.</summary>
    internal Dictionary<string, string> BuildEnvironment(bool setTerm)
    {
        var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        // The terminal emulates xterm with 24-bit color; ConPTY sets its own terminal type for Windows programs.
        if (setTerm)
        {
            environment["TERM"] = "xterm-256color";
            environment["COLORTERM"] = "truecolor";

            // The terminal speaks UTF-8; without a locale, shells treat typed non-ASCII text as raw bytes.
            if (!environment.ContainsKey("LC_ALL") && !environment.ContainsKey("LC_CTYPE") && !environment.ContainsKey("LANG"))
            {
                environment["LANG"] = OperatingSystem.IsMacOS() ? "en_US.UTF-8" : "C.UTF-8";
            }
        }

        foreach (var pair in Environment)
        {
            if (pair.Value == null)
            {
                environment.Remove(pair.Key);
            }
            else
            {
                environment[pair.Key] = pair.Value;
            }
        }

        return environment;
    }
}

/// <summary>A program on a pseudo-terminal of one platform.</summary>
internal interface IPseudoTerminalProcess : IDisposable
{
    void Start();

    void Write(ReadOnlySpan<byte> data);

    void Resize(int columns, int rows);
}
