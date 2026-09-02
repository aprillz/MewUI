namespace Aprillz.MewUI;

/// <summary>
/// Handler pair (execute + can-execute query) registered for a command in a <see cref="CommandScope"/>.
/// One derived shape per public Register overload so no closure allocation is needed.
/// </summary>
internal abstract class CommandHandler
{
    protected CommandHandler(Command command) => Command = command;

    public Command Command { get; }

    /// <summary>
    /// Whether the handler consumes the invocation operand; the router resolves one only for these.
    /// </summary>
    public virtual bool AcceptsArgument => false;

    /// <summary>
    /// Queries current state; a handler without a can-execute predicate is always executable.
    /// </summary>
    public abstract bool CanExecute(in CommandContext context);

    public abstract ValueTask ExecuteAsync(in CommandContext context);
}

internal sealed class ArgumentCommandHandler<TArg> : CommandHandler
{
    private readonly Action<TArg> _execute;
    private readonly Func<TArg, bool>? _canExecute;

    public ArgumentCommandHandler(Command command, Action<TArg> execute, Func<TArg, bool>? canExecute)
        : base(command)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool AcceptsArgument => true;

    public override bool CanExecute(in CommandContext context)
        => context.Argument is TArg argument && (_canExecute?.Invoke(argument) ?? true);

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        if (context.Argument is TArg argument)
        {
            _execute(argument);
        }

        return default;
    }
}

internal sealed class SimpleCommandHandler : CommandHandler
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public SimpleCommandHandler(Command command, Action execute, Func<bool>? canExecute)
        : base(command)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke() ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute();
        return default;
    }
}

internal sealed class ContextCommandHandler : CommandHandler
{
    private readonly Action<CommandContext> _execute;
    private readonly Func<CommandContext, bool>? _canExecute;

    public ContextCommandHandler(Command command, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute)
        : base(command)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute(context);
        return default;
    }
}

internal sealed class AsyncContextCommandHandler : CommandHandler
{
    private readonly Func<CommandContext, ValueTask> _execute;
    private readonly Func<CommandContext, bool>? _canExecute;

    public AsyncContextCommandHandler(Command command, Func<CommandContext, ValueTask> execute, Func<CommandContext, bool>? canExecute)
        : base(command)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context) => _execute(context);
}

internal sealed class TargetCommandHandler<T> : CommandHandler
    where T : class
{
    private readonly T _target;
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public TargetCommandHandler(Command command, T target, Action<T> execute, Func<T, bool>? canExecute)
        : base(command)
    {
        _target = target;
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(_target) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute(_target);
        return default;
    }
}

internal sealed class TargetContextCommandHandler<T> : CommandHandler
    where T : class
{
    private readonly T _target;
    private readonly Action<T, CommandContext> _execute;
    private readonly Func<T, CommandContext, bool>? _canExecute;

    public TargetContextCommandHandler(Command command, T target, Action<T, CommandContext> execute, Func<T, CommandContext, bool>? canExecute)
        : base(command)
    {
        _target = target;
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(_target, context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute(_target, context);
        return default;
    }
}

internal sealed class AsyncTargetContextCommandHandler<T> : CommandHandler
    where T : class
{
    private readonly T _target;
    private readonly Func<T, CommandContext, ValueTask> _execute;
    private readonly Func<T, CommandContext, bool>? _canExecute;

    public AsyncTargetContextCommandHandler(Command command, T target, Func<T, CommandContext, ValueTask> execute, Func<T, CommandContext, bool>? canExecute)
        : base(command)
    {
        _target = target;
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(_target, context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context) => _execute(_target, context);
}
