namespace Aprillz.MewUI;

/// <summary>
/// Semantic handler container mapping commands to execute/can-execute pairs for one context
/// (an element subtree, a window or the application).
/// </summary>
/// <remarks>
/// A scope holds at most one handler per command; replacing it requires an explicit
/// <see cref="Unregister"/> (or disposing the previous <see cref="CommandRegistration"/>).
/// <see cref="Parent"/> forms an explicit semantic chain that is independent of the visual tree.
/// Mutation is a UI-thread operation.
/// </remarks>
public sealed class CommandScope : IDisposable
{
    private Dictionary<Command, CommandHandler>? _handlers;
    private bool _disposed;

    public CommandScope(CommandScope? parent = null) => Parent = parent;

    /// <summary>
    /// Gets the explicit semantic parent scope consulted when this scope has no handler.
    /// </summary>
    public CommandScope? Parent { get; }

    /// <summary>
    /// Registers a parameterless handler.
    /// </summary>
    public CommandRegistration Register(Command command, Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return AddHandler(new SimpleCommandHandler(command, execute, canExecute));
    }

    /// <summary>
    /// Registers a handler receiving the invocation context.
    /// </summary>
    public CommandRegistration Register(Command command, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return AddHandler(new ContextCommandHandler(command, execute, canExecute));
    }

    /// <summary>
    /// Registers an asynchronous handler receiving the invocation context.
    /// </summary>
    public CommandRegistration Register(Command command, Func<CommandContext, ValueTask> execute, Func<CommandContext, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return AddHandler(new AsyncContextCommandHandler(command, execute, canExecute));
    }

    /// <summary>
    /// Registers a handler that receives the invocation operand: the value of the nearest
    /// <see cref="ICommandArgumentSource"/> above the invocation anchor. The handler cannot execute
    /// while no operand of type <typeparamref name="TArg"/> is present.
    /// </summary>
    /// <remarks>
    /// Write the lambda parameter type explicitly (<c>(Item item) => ...</c>); an untyped lambda
    /// resolves to the <see cref="CommandContext"/> overload instead.
    /// </remarks>
    public CommandRegistration Register<TArg>(Command command, Action<TArg> execute, Func<TArg, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return AddHandler(new ArgumentCommandHandler<TArg>(command, execute, canExecute));
    }

    /// <summary>
    /// Registers a handler invoked with the given target, enabling closure-free static lambdas.
    /// </summary>
    public CommandRegistration Register<T>(Command command, T target, Action<T> execute, Func<T, bool>? canExecute = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(execute);
        return AddHandler(new TargetCommandHandler<T>(command, target, execute, canExecute));
    }

    /// <summary>
    /// Registers a handler invoked with the given target and the invocation context.
    /// </summary>
    public CommandRegistration Register<T>(Command command, T target, Action<T, CommandContext> execute, Func<T, CommandContext, bool>? canExecute = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(execute);
        return AddHandler(new TargetContextCommandHandler<T>(command, target, execute, canExecute));
    }

    /// <summary>
    /// Registers an asynchronous handler invoked with the given target and the invocation context.
    /// </summary>
    public CommandRegistration Register<T>(Command command, T target, Func<T, CommandContext, ValueTask> execute, Func<T, CommandContext, bool>? canExecute = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(execute);
        return AddHandler(new AsyncTargetContextCommandHandler<T>(command, target, execute, canExecute));
    }

    /// <summary>
    /// Returns whether this scope (not its parents) has a handler for the command.
    /// </summary>
    public bool Contains(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _handlers?.ContainsKey(command) == true;
    }

    /// <summary>
    /// Unregisters this scope's handler for the command; returns false when none exists.
    /// </summary>
    public bool Unregister(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _handlers?.Remove(command) == true;
    }

    /// <summary>
    /// Removes all handlers from this scope.
    /// </summary>
    public void Clear() => _handlers?.Clear();

    /// <summary>
    /// Clears the scope and rejects further registration.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _handlers = null;
    }

    internal bool TryGetHandler(Command command, out CommandHandler handler)
    {
        if (_handlers != null && _handlers.TryGetValue(command, out var found))
        {
            handler = found;
            return true;
        }

        handler = null!;
        return false;
    }

    internal void RemoveRegistration(Command command, CommandHandler handler)
    {
        // Only the registration's own handler may be removed; a replaced command keeps its new handler.
        if (_handlers != null && _handlers.TryGetValue(command, out var current) && ReferenceEquals(current, handler))
        {
            _handlers.Remove(command);
        }
    }

    private CommandRegistration AddHandler(CommandHandler handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var handlers = _handlers ??= new Dictionary<Command, CommandHandler>(capacity: 4);
        if (!handlers.TryAdd(handler.Command, handler))
        {
            throw new InvalidOperationException(
                $"Command '{handler.Command.Id}' is already registered in this scope. Unregister it first to replace the handler.");
        }

        return new CommandRegistration(this, handler);
    }
}
