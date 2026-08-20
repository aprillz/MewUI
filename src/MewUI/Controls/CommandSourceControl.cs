using Aprillz.MewUI.Input;

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

    /// <summary>
    /// Which parts of the command this control builds a tooltip from when it has no
    /// <see cref="Control.ToolTip"/> of its own. Defaults to none: a control that shows its command's
    /// text and icon has already said what a tooltip would say.
    /// </summary>
    public static readonly MewProperty<CommandToolTipMode> CommandToolTipModeProperty =
        MewProperty<CommandToolTipMode>.Register<CommandSourceControl>(nameof(CommandToolTipMode),
            CommandToolTipMode.None, MewPropertyOptions.None);

    private Window? _commandSourceWindow;

    /// <inheritdoc cref="CommandToolTipModeProperty"/>
    public CommandToolTipMode CommandToolTipMode
    {
        get => GetValue(CommandToolTipModeProperty);
        set => SetValue(CommandToolTipModeProperty, value);
    }

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
    /// A tooltip of the control's own always wins. Otherwise the parts <see cref="CommandToolTipMode"/> asks
    /// for are collected from the command, and a mode that collects nothing shows nothing: no part stands
    /// in for another that has no material behind it.
    /// </summary>
    protected override Element? ResolveToolTipContent()
        => ToolTip ?? BuildCommandToolTip();

    private Element? BuildCommandToolTip()
    {
        var mode = CommandToolTipMode;
        if (mode == CommandToolTipMode.None || Command is not Command command)
        {
            return null;
        }

        string? text = mode.HasFlag(CommandToolTipMode.Text) ? command.Text : null;

        string? shortcut = null;
        if (mode.HasFlag(CommandToolTipMode.Shortcut) && FindVisualRoot() is Window window)
        {
            shortcut = InputMapResolver.GetEffectiveGestureText(window, command, origin: this);
        }

        string? description = mode.HasFlag(CommandToolTipMode.Description) ? command.Description : null;

        // The shortcut rides the name's line in brackets, which is what a toolbar tooltip does everywhere;
        // aligning it to a column is a menu row's business and a tooltip has no column to align to.
        string title = (text, shortcut) switch
        {
            (not null, not null) => $"{text} ({shortcut})",
            (not null, null) => text,
            (null, not null) => shortcut,
            _ => string.Empty,
        };

        string content = (title.Length > 0, !string.IsNullOrEmpty(description)) switch
        {
            (true, true) => $"{title}\n{description}",
            (true, false) => title,
            (false, true) => description!,
            _ => string.Empty,
        };

        return content.Length > 0 ? new TextBlock { Text = content } : null;
    }

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
