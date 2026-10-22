namespace Aprillz.MewUI.Terminal;

/// <summary>Receives bytes a connection read from the application.</summary>
/// <param name="data">The bytes, valid only during the call.</param>
public delegate void TerminalOutputHandler(ReadOnlySpan<byte> data);

/// <summary>
/// The other end of a terminal: an application whose output the terminal shows and to which it sends keyboard input
/// and reports, as UTF-8.
/// </summary>
public interface ITerminalConnection : IDisposable
{
    /// <summary>Occurs on a background thread with each block of output.</summary>
    event TerminalOutputHandler? Output;

    /// <summary>Occurs once, on a background thread, when the connection ends, with the exit code or -1 if unknown.</summary>
    event Action<int>? Closed;

    /// <summary>Starts the connection; output may be raised from then on.</summary>
    void Start();

    /// <summary>Sends input to the application.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Tells the application the terminal size in cells.</summary>
    void Resize(int columns, int rows);
}
