namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that invoke a semantic <see cref="MewUI.Command"/>: it owns the router
/// wiring (command source registration, CanExecute queries, execution) but decides nothing about
/// when to invoke or how to present the command. Derived controls call
/// <see cref="InvokeCommand"/> from their own activation path.
/// </summary>
public abstract class CommandSourceControl : ContentControl, ICommandSource
{
    public static readonly MewProperty<Command?> CommandProperty =
        MewProperty<Command?>.Register<CommandSourceControl>(nameof(Command), null,
            MewPropertyOptions.None,
            static (self, oldValue, newValue) => self.OnCommandChanged(oldValue, newValue));

    private Window? _commandSourceWindow;

    /// <summary>
    /// Gets or sets the semantic command this control invokes.
    /// </summary>
    public Command? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected virtual void OnCommandChanged(Command? oldValue, Command? newValue)
    {
        UpdateCommandSourceRegistration();
        ReevaluateSuggestedIsEnabled();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        UpdateCommandSourceRegistration();
    }

    private void UpdateCommandSourceRegistration()
    {
        var window = Command != null ? FindVisualRoot() as Window : null;
        if (ReferenceEquals(_commandSourceWindow, window))
        {
            return;
        }

        _commandSourceWindow?.UnregisterCommandSource(this);
        _commandSourceWindow = window;
        window?.RegisterCommandSource(this);
    }

    void ICommandSource.EvaluateCommandState() => ReevaluateSuggestedIsEnabled();

    /// <summary>
    /// Asks the router whether the current command can execute. Returns true when this control has
    /// no command or no routing window, so callers can use it directly as an enabled suggestion.
    /// </summary>
    protected bool QueryCommandCanExecute()
    {
        if (GetValue(CommandProperty) is Command command && FindVisualRoot() is Window window)
        {
            return window.CommandRouter.CanExecute(command, CommandTarget.From(this));
        }

        return true;
    }

    /// <summary>
    /// Executes the current command as a user-initiated invocation. Does nothing without a command.
    /// </summary>
    protected void InvokeCommand()
    {
        if (GetValue(CommandProperty) is Command command && FindVisualRoot() is Window window)
        {
            window.CommandRouter.TryExecuteFromInput(command, CommandTarget.From(this), this);
        }
    }

    protected override void OnDispose()
    {
        _commandSourceWindow?.UnregisterCommandSource(this);
        _commandSourceWindow = null;
        base.OnDispose();
    }
}
