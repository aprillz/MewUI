using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// An icon the application shows in the system's notification area or status bar, with an optional menu.
/// </summary>
/// <remarks>
/// Nothing appears until <see cref="IsVisible"/> is true while the application runs. The icon is removed when
/// <see cref="IsVisible"/> turns false, when it is disposed, and when the run ends, which also disposes it. Use it on
/// the UI thread. How the menu is presented follows each platform's convention.
/// </remarks>
public sealed class TrayIcon : MewObject, IDisposable
{
    /// <summary>The icon image; null shows the application's own icon.</summary>
    public static readonly MewProperty<IconSource?> IconProperty =
        MewProperty<IconSource?>.Register<TrayIcon>(nameof(Icon), null, MewPropertyOptions.None,
            static (self, _, _) => self.OnAppearanceChanged());

    /// <summary>The text shown when the pointer rests on the icon.</summary>
    public static readonly MewProperty<string> ToolTipTextProperty =
        MewProperty<string>.Register<TrayIcon>(nameof(ToolTipText), string.Empty, MewPropertyOptions.None,
            static (self, _, _) => self.OnAppearanceChanged());

    /// <summary>The menu the icon opens; null for an icon without a menu.</summary>
    public static readonly MewProperty<ContextMenu?> ContextMenuProperty =
        MewProperty<ContextMenu?>.Register<TrayIcon>(nameof(ContextMenu), null, MewPropertyOptions.None,
            static (self, _, _) => self.OnAppearanceChanged());

    /// <summary>The command the icon runs when activated.</summary>
    public static readonly MewProperty<Command?> CommandProperty =
        MewProperty<Command?>.Register<TrayIcon>(nameof(Command), null);

    /// <summary>The value passed to <see cref="Command"/> as its invocation argument.</summary>
    public static readonly MewProperty<object?> CommandDataProperty =
        MewProperty<object?>.Register<TrayIcon>(nameof(CommandData), null);

    /// <summary>Whether the icon is shown; false by default.</summary>
    public static readonly MewProperty<bool> IsVisibleProperty =
        MewProperty<bool>.Register<TrayIcon>(nameof(IsVisible), false, MewPropertyOptions.None,
            static (self, _, newValue) => self.OnIsVisibleChanged(newValue));

    /// <summary>
    /// Whether the icon is a single-color glyph that the platform may tint to match its status bar.
    /// </summary>
    public static readonly MewProperty<bool> IsMonochromeIconProperty =
        MewProperty<bool>.Register<TrayIcon>(nameof(IsMonochromeIcon), false, MewPropertyOptions.None,
            static (self, _, _) => self.OnAppearanceChanged());

    private ITrayIconBackend? _backend;
    private CommandScope? _commands;
    private bool _showQueued;
    private bool _waitingForDispatcher;
    private bool _disposed;

    /// <summary>Occurs when the platform reports a primary activation of the icon apart from opening its menu.</summary>
    public event Action<TrayIconEventArgs>? Click;

    /// <summary>Occurs on a double click where the platform reports one; its clicks raise <see cref="Click"/> as well.</summary>
    public event Action<TrayIconEventArgs>? DoubleClick;

    /// <summary>Occurs just before the menu opens; a handler may replace or fill <see cref="ContextMenu"/>.</summary>
    public event Action? MenuOpening;

    /// <summary>Gets whether the running application's platform has somewhere to show tray icons now; false before a run.</summary>
    public static bool IsSupported => Application.IsRunning && Application.Current.PlatformHost.IsTrayIconSupported;

    /// <inheritdoc cref="IconProperty"/>
    public IconSource? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <inheritdoc cref="ToolTipTextProperty"/>
    public string ToolTipText
    {
        get => GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value ?? string.Empty);
    }

    /// <inheritdoc cref="ContextMenuProperty"/>
    public ContextMenu? ContextMenu
    {
        get => GetValue(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    /// <inheritdoc cref="CommandProperty"/>
    public Command? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <inheritdoc cref="CommandDataProperty"/>
    public object? CommandData
    {
        get => GetValue(CommandDataProperty);
        set => SetValue(CommandDataProperty, value);
    }

    /// <inheritdoc cref="IsVisibleProperty"/>
    public bool IsVisible
    {
        get => GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <inheritdoc cref="IsMonochromeIconProperty"/>
    public bool IsMonochromeIcon
    {
        get => GetValue(IsMonochromeIconProperty);
        set => SetValue(IsMonochromeIconProperty, value);
    }

    /// <summary>
    /// Gets the scope the icon's command and its menu's commands resolve in before <see cref="Application.Commands"/>.
    /// </summary>
    public CommandScope Commands => _commands ??= new CommandScope();

    internal bool IsShown => _backend != null;

    internal bool HasClickHandler => Click != null;

    /// <summary>The target the icon's and its menu's commands resolve against through the application's router.</summary>
    internal CommandTarget CommandTarget => CommandTarget.From(Commands);

    internal void RaiseClick(Point screenPosition, MouseButton button)
    {
        if (_disposed)
        {
            return;
        }

        Click?.Invoke(new TrayIconEventArgs(screenPosition, button));
        if (!_disposed && Command is Command command)
        {
            Application.Current.CommandRouter.TryExecuteFromInput(command, CommandTarget, source: null, CommandData);
        }
    }

    internal void RaiseDoubleClick(Point screenPosition, MouseButton button)
    {
        if (!_disposed)
        {
            DoubleClick?.Invoke(new TrayIconEventArgs(screenPosition, button));
        }
    }

    /// <summary>Raises <see cref="MenuOpening"/> and returns the menu to open then, or null when there is none.</summary>
    internal ContextMenu? PrepareMenu()
    {
        if (_disposed)
        {
            return null;
        }

        MenuOpening?.Invoke();
        return _disposed ? null : ContextMenu;
    }

    /// <summary>Removes the icon and releases its resources; the icon cannot be shown again.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWaitingForDispatcher();
        RemoveBackend();
        Click = null;
        DoubleClick = null;
        MenuOpening = null;
        _commands?.Dispose();
        _commands = null;
    }

    private void OnIsVisibleChanged(bool visible)
    {
        if (visible)
        {
            QueueShow();
        }
        else
        {
            RemoveBackend();
        }
    }

    private void OnAppearanceChanged() => _backend?.Update();

    private void QueueShow()
    {
        if (_disposed || _backend != null || _showQueued)
        {
            return;
        }

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher != null)
        {
            // A later turn, so properties set after IsVisible in an initializer or a fluent chain apply first.
            _showQueued = true;
            dispatcher.BeginInvoke(ShowNow);
        }
        else if (!_waitingForDispatcher)
        {
            _waitingForDispatcher = true;
            Application.DispatcherChanged += OnDispatcherChanged;
        }
    }

    private void OnDispatcherChanged(IDispatcher? dispatcher)
    {
        if (dispatcher != null)
        {
            StopWaitingForDispatcher();
            QueueShow();
        }
    }

    private void StopWaitingForDispatcher()
    {
        if (_waitingForDispatcher)
        {
            _waitingForDispatcher = false;
            Application.DispatcherChanged -= OnDispatcherChanged;
        }
    }

    private void ShowNow()
    {
        _showQueued = false;
        if (_disposed || _backend != null || !IsVisible || !Application.IsRunning)
        {
            return;
        }

        var application = Application.Current;
        _backend = application.PlatformHost.CreateTrayIconBackend(this);
        if (_backend != null)
        {
            application.RegisterTrayIcon(this);
        }
    }

    private void RemoveBackend()
    {
        var backend = _backend;
        if (backend == null)
        {
            return;
        }

        _backend = null;
        if (Application.IsRunning)
        {
            Application.Current.UnregisterTrayIcon(this);
        }

        backend.Dispose();
    }
}

/// <summary>Provides data for <see cref="TrayIcon.Click"/> and <see cref="TrayIcon.DoubleClick"/>.</summary>
public sealed class TrayIconEventArgs
{
    internal TrayIconEventArgs(Point screenPosition, MouseButton button)
    {
        ScreenPosition = screenPosition;
        Button = button;
    }

    /// <summary>Gets where the click happened on the screen, in device pixels; for a keyboard activation, the icon's position.</summary>
    public Point ScreenPosition { get; }

    /// <summary>Gets the mouse button; <see cref="MouseButton.Left"/> for a keyboard activation.</summary>
    public MouseButton Button { get; }
}
