namespace Aprillz.MewUI;

/// <summary>
/// Lifetime token for a handler registered with a <see cref="CommandScope"/>; disposing removes it.
/// </summary>
public sealed class CommandRegistration : IDisposable
{
    private CommandScope? _scope;
    private readonly CommandHandler _handler;

    internal CommandRegistration(CommandScope scope, CommandHandler handler)
    {
        _scope = scope;
        _handler = handler;
    }

    /// <summary>
    /// Gets the registered command.
    /// </summary>
    public Command Command => _handler.Command;

    /// <summary>
    /// Removes the handler from its scope; later calls do nothing.
    /// </summary>
    public void Dispose()
    {
        var scope = _scope;
        _scope = null;
        scope?.RemoveRegistration(_handler.Command, _handler);
    }
}
