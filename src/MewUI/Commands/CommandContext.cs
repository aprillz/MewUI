using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Invocation context passed to command handlers: the routing window (none when the application's router runs the
/// command), the invocation source element (e.g. the toolbar button) and the invocation's cancellation token.
/// </summary>
public readonly struct CommandContext
{
    internal CommandContext(Window? window, Element? source, CancellationToken cancellationToken, object? argument = null)
    {
        Window = window;
        Source = source;
        CancellationToken = cancellationToken;
        Argument = argument;
    }

    /// <summary>
    /// Gets the window whose router dispatched the command, or null when <see cref="Application.CommandRouter"/> did,
    /// as for a tray icon's menu while no window is open.
    /// </summary>
    public Window? Window { get; }

    /// <summary>
    /// Gets the element that initiated the invocation, or null for programmatic execution.
    /// </summary>
    public Element? Source { get; }

    /// <summary>
    /// Gets the cancellation token for asynchronous handlers.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the invocation operand resolved from the nearest <see cref="ICommandArgumentSource"/>;
    /// only typed handlers consume it, as their parameter.
    /// </summary>
    internal object? Argument { get; }
}
