using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Resolves a command to its nearest handler for a target context and dispatches invocations,
/// always re-querying CanExecute at invocation time.
/// </summary>
/// <remarks>
/// Resolution order: the explicit or focused target's context chain, then
/// <see cref="FallbackTarget"/>, then the window scope, then the application scope. The nearest
/// handler ends the search even when its CanExecute returns false (disabled shadowing).
/// </remarks>
public sealed class CommandRouter
{
    // Defensive bound for context-parent walks; mirrors focus-chain traversal guards.
    private const int MAX_CHAIN_LENGTH = 256;

    private readonly Window _window;
    private CommandTarget? _fallbackTarget;

    internal CommandRouter(Window window) => _window = window;

    /// <summary>
    /// Gets or sets the target consulted when the focused context has no handler; a shell layer
    /// typically points this at its active content.
    /// </summary>
    public CommandTarget? FallbackTarget
    {
        get => _fallbackTarget;
        set
        {
            if (_fallbackTarget == value)
            {
                return;
            }

            _fallbackTarget = value;
            _window.RequestCommandStateEvaluation();
        }
    }

    /// <summary>
    /// Captures the current focused context (or the window itself) as a reusable target snapshot.
    /// </summary>
    public CommandTarget CaptureTarget() => CommandTarget.From(ResolveFocusedOrigin());

    /// <summary>
    /// Captures the given element as a target snapshot.
    /// </summary>
    public CommandTarget CaptureTarget(Element origin) => CommandTarget.From(origin);

    /// <summary>
    /// Queries whether the command can execute in the current focused context.
    /// </summary>
    public bool CanExecute(Command command) => CanExecute(command, CaptureTarget());

    /// <summary>
    /// Queries whether the command can execute for the given target.
    /// </summary>
    public bool CanExecute(Command command, CommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryResolve(command, target, out var handler))
        {
            return false;
        }

        var context = new CommandContext(_window, source: null, CancellationToken.None, ResolveArgument(handler, target));
        return handler.CanExecute(in context);
    }

    /// <summary>
    /// Queries with an operand captured earlier (a menu snapshots it when it opens) instead of
    /// resolving one from the target now.
    /// </summary>
    internal bool CanExecute(Command command, CommandTarget target, object? argument)
    {
        if (!TryResolve(command, target, out var handler))
        {
            return false;
        }

        var context = new CommandContext(_window, source: null, CancellationToken.None, argument);
        return handler.CanExecute(in context);
    }

    /// <summary>
    /// Executes the command in the current focused context; returns false when no handler exists
    /// or CanExecute rejects the invocation.
    /// </summary>
    public ValueTask<bool> ExecuteAsync(Command command, Element? source = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(command, CaptureTarget(), source, cancellationToken);

    /// <summary>
    /// Executes the command for the given target; returns false when no handler exists or
    /// CanExecute rejects the invocation.
    /// </summary>
    public async ValueTask<bool> ExecuteAsync(Command command, CommandTarget target, Element? source = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryResolve(command, target, out var handler))
        {
            return false;
        }

        var context = new CommandContext(_window, source, cancellationToken, ResolveArgument(handler, target));
        if (!handler.CanExecute(in context))
        {
            return false;
        }

        await handler.ExecuteAsync(in context).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Synchronous invocation entry for input dispatch: starts execution and reports whether a
    /// handler ran (asynchronous completions are observed on the dispatcher).
    /// </summary>
    internal bool TryExecuteFromInput(Command command, CommandTarget target, Element? source)
    {
        if (!TryResolve(command, target, out var handler))
        {
            return false;
        }

        return TryExecuteFromInput(handler, target, source, ResolveArgument(handler, target));
    }

    /// <summary>
    /// Input dispatch with an operand captured earlier instead of one resolved from the target now.
    /// </summary>
    internal bool TryExecuteFromInput(Command command, CommandTarget target, Element? source, object? argument)
    {
        if (!TryResolve(command, target, out var handler))
        {
            return false;
        }

        return TryExecuteFromInput(handler, target, source, argument);
    }

    private bool TryExecuteFromInput(CommandHandler handler, CommandTarget target, Element? source, object? argument)
    {
        var context = new CommandContext(_window, source, CancellationToken.None, argument);
        if (!handler.CanExecute(in context))
        {
            return false;
        }

        var pending = handler.ExecuteAsync(in context);
        if (!pending.IsCompleted)
        {
            ObserveAsyncCompletion(pending);
        }
        else
        {
            // Surface a synchronously faulted ValueTask now instead of dropping it.
            pending.GetAwaiter().GetResult();
        }

        return true;
    }

    internal bool TryResolve(Command command, CommandTarget target, out CommandHandler handler)
    {
        if (TryResolveFromOrigin(target.Origin, command, out handler))
        {
            return true;
        }

        if (_fallbackTarget is CommandTarget fallback &&
            fallback != target &&
            TryResolveFromOrigin(fallback.Origin, command, out handler))
        {
            return true;
        }

        if (_window.TryGetCommandScope() is CommandScope windowScope &&
            TryResolveScopeChain(windowScope, command, out handler))
        {
            return true;
        }

        if (Application.CurrentCommandScopeOrNull is CommandScope applicationScope &&
            TryResolveScopeChain(applicationScope, command, out handler))
        {
            return true;
        }

        handler = null!;
        return false;
    }

    private bool TryResolveFromOrigin(object? origin, Command command, out CommandHandler handler)
    {
        if (origin is CommandScope scope)
        {
            return TryResolveScopeChain(scope, command, out handler);
        }

        if (origin is Element element)
        {
            int steps = 0;
            for (Element? current = element; current != null && steps < MAX_CHAIN_LENGTH; current = current.ContextParent, steps++)
            {
                // The window scope is a later, separate resolution stage (after FallbackTarget).
                if (ReferenceEquals(current, _window))
                {
                    break;
                }

                if (current.TryGetCommandScope() is CommandScope local &&
                    TryResolveScopeChain(local, command, out handler))
                {
                    return true;
                }
            }
        }

        handler = null!;
        return false;
    }

    /// <summary>
    /// Resolves the invocation operand: the value of the nearest <see cref="ICommandArgumentSource"/>
    /// on the anchor's context chain, the anchor itself included, or null when there is none.
    /// </summary>
    internal static object? ResolveArgument(Element anchor)
    {
        int steps = 0;
        for (Element? current = anchor; current != null && steps < MAX_CHAIN_LENGTH; current = current.ContextParent, steps++)
        {
            if (current is ICommandArgumentSource source)
            {
                return source.CommandArgument;
            }
        }

        return null;
    }

    private static object? ResolveArgument(CommandHandler handler, CommandTarget target)
    {
        // Only typed handlers read the operand, so the chain walk is skipped for every other shape.
        return handler.AcceptsArgument && target.OriginElement is Element anchor
            ? ResolveArgument(anchor)
            : null;
    }

    private static bool TryResolveScopeChain(CommandScope scope, Command command, out CommandHandler handler)
    {
        int steps = 0;
        for (CommandScope? current = scope; current != null && steps < MAX_CHAIN_LENGTH; current = current.Parent, steps++)
        {
            if (current.TryGetHandler(command, out handler))
            {
                return true;
            }
        }

        handler = null!;
        return false;
    }

    private Element ResolveFocusedOrigin()
    {
        var focused = _window.FocusManager.FocusedElement;
        if (focused != null && !ReferenceEquals(focused.FindVisualRoot(), _window))
        {
            focused = null;
        }

        return (Element?)focused ?? _window;
    }

    private static async void ObserveAsyncCompletion(ValueTask pending)
    {
        // async void: a faulted handler surfaces through the dispatcher exception policy.
        await pending.ConfigureAwait(true);
    }
}
