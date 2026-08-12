using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button control that responds to clicks.
/// </summary>
public partial class Button : Control, IVisualTreeHost, ICommandSource
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<Button>(DefaultStyles.CreateButtonStyle);
    private static readonly bool _flatStyleRegistered =
        FrameworkNamedStyles.Register("flat-button", BuiltInStyles.CreateFlatButtonStyle);
    private static readonly bool _accentStyleRegistered =
        FrameworkNamedStyles.Register("accent-button", BuiltInStyles.CreateAccentButtonStyle);

    public static readonly MewProperty<Element?> ContentProperty =
        MewProperty<Element?>.Register<Button>(nameof(Content), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnContentChanged(oldValue, newValue));

    public static readonly MewProperty<Command?> CommandProperty =
        MewProperty<Command?>.Register<Button>(nameof(Command), null,
            MewPropertyOptions.None,
            static (self, oldValue, newValue) => self.OnCommandChanged(oldValue, newValue));

    public static readonly MewProperty<CommandPresentationMode> CommandPresentationModeProperty =
        MewProperty<CommandPresentationMode>.Register<Button>(nameof(CommandPresentationMode),
            CommandPresentationMode.None,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.UpdateCommandPresentationContent());

    /// <summary>
    /// Gets or sets the semantic command this button invokes; its CanExecute query joins
    /// <see cref="UIElement.IsEnabled"/> in the effective enabled state.
    /// </summary>
    public Command? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets which command presentation parts are used as generated content. The default is
    /// <see cref="MewUI.CommandPresentationMode.None"/>; an explicitly supplied Content value or
    /// binding always takes precedence.
    /// </summary>
    public CommandPresentationMode CommandPresentationMode
    {
        get => GetValue(CommandPresentationModeProperty);
        set => SetValue(CommandPresentationModeProperty, value);
    }

    private Window? _commandSourceWindow;
    private CommandContentPresenter? _commandPresentationContent;

    private void OnCommandChanged(Command? oldCommand, Command? newCommand)
    {
        if (oldCommand != null)
        {
            WeakEventManager.RemoveHandler(
                CommandPresentationWeakEvents.Changed,
                oldCommand.Presentation,
                this);
        }

        if (newCommand != null)
        {
            WeakEventManager.AddHandler(
                CommandPresentationWeakEvents.Changed,
                newCommand.Presentation,
                this,
                static button => button.UpdateCommandPresentationContent());
        }

        UpdateCommandSourceRegistration();
        ReevaluateSuggestedIsEnabled();
        UpdateCommandPresentationContent();
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
    /// Gets or sets the content element.
    /// </summary>
    public Element? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    protected virtual void OnContentChanged(Element? oldValue, Element? newValue)
    {
        if (oldValue != null) oldValue.Parent = null;
        if (newValue != null) newValue.Parent = this;
        UpdateCommandPresentationContent();
    }

    private Element? EffectiveContent
    {
        get
        {
            if (GetPropertyValueTrace(ContentProperty).EffectiveSource != ValueSource.Default)
            {
                return Content;
            }

            EnsureCommandPresentationContent();
            return _commandPresentationContent;
        }
    }

    private void EnsureCommandPresentationContent()
    {
        if (CommandPresentationMode == MewUI.CommandPresentationMode.None || Command == null)
        {
            DetachCommandPresentationContent();
            return;
        }

        if (_commandPresentationContent != null)
        {
            return;
        }

        _commandPresentationContent = new CommandContentPresenter { Parent = this };
        RefreshCommandPresentationContent();
    }

    private void RefreshCommandPresentationContent()
    {
        if (_commandPresentationContent == null || Command == null)
        {
            return;
        }

        double iconSize = Theme.Metrics.ContextMenuIconSize;
        if (!double.IsFinite(iconSize) || iconSize <= 0) iconSize = 16;
        var resolvedSize = IconTemplate.ResolveSize(iconSize, GetDpi() / 96.0);
        _commandPresentationContent.Update(Command.Presentation, CommandPresentationMode, resolvedSize);
    }

    private void UpdateCommandPresentationContent()
    {
        if (GetPropertyValueTrace(ContentProperty).EffectiveSource != ValueSource.Default)
        {
            DetachCommandPresentationContent();
        }
        else
        {
            EnsureCommandPresentationContent();
            RefreshCommandPresentationContent();
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void DetachCommandPresentationContent()
    {
        if (_commandPresentationContent == null) return;
        _commandPresentationContent.Parent = null;
        _commandPresentationContent = null;
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        if (oldTheme.Metrics.ContextMenuIconSize != newTheme.Metrics.ContextMenuIconSize)
        {
            UpdateCommandPresentationContent();
        }
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateCommandPresentationContent();
    }

    internal override void OnAccessKey() { Focus(); RaiseClick(); }

    private readonly PressCaptureHelper _pressCapture;

    static Button()
    {
        FocusableProperty.OverrideDefaultValue<Button>(true);
    }

    public Button()
    {
        _pressCapture = new PressCaptureHelper(this, SetPressed);
    }

    /// <summary>
    /// Click event handler (AOT-compatible).
    /// </summary>
    public event Action? Click;

    protected override bool ComputeIsEnabledSuggestion()
    {
        if (GetValue(CommandProperty) is Command command && FindVisualRoot() is Window window)
        {
            return window.CommandRouter.CanExecute(command, CommandTarget.From(this));
        }

        return true;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var content = EffectiveContent;
        if (content == null)
        {
            return new Size(Padding.HorizontalThickness + 20, Padding.VerticalThickness + 10).Inflate(border);
        }

        var contentSize = availableSize.Deflate(Padding).Deflate(border);
        content.Measure(contentSize);
        return content.DesiredSize.Inflate(Padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        var content = EffectiveContent;
        if (content == null)
        {
            return;
        }

        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;
        var contentBounds = bounds.Deflate(Padding).Deflate(border);
        content.Arrange(contentBounds);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bgColor = GetValue(BackgroundProperty);
        var borderColor = GetValue(BorderBrushProperty);

        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = CornerRadius;
        DrawBackgroundAndBorder(context, bounds, bgColor, borderColor, BorderThickness, radius);

        EffectiveContent?.Render(context);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButton.Left && IsEffectivelyEnabled)
        {
            _pressCapture.BeginPress(() => Focus());

            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButton.Left && IsPressed)
        {
            _pressCapture.EndPress();

            // Fire click if still over button
            if (!SuppressClickOnMouseUp && IsEffectivelyEnabled && Bounds.Contains(e.Position))
            {
                OnClick();
            }

            e.Handled = true;
        }
    }

    /// <summary>When true, mouse-up does not raise <see cref="Click"/> (RepeatButton fires it from press/timer instead).</summary>
    private protected virtual bool SuppressClickOnMouseUp => false;

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _pressCapture.CancelPress();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Space or Enter triggers click
        if ((e.Key == Key.Space || e.Key == Key.Enter) && IsEffectivelyEnabled)
        {
            SetPressed(true);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if ((e.Key == Key.Space || e.Key == Key.Enter) && IsPressed)
        {
            SetPressed(false);
            if (IsEffectivelyEnabled)
            {
                OnClick();
            }

            e.Handled = true;
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var content = EffectiveContent;
        return content == null || visitor(content);
    }

    protected virtual void OnClick()
    {
        Click?.Invoke();
        InvokeCommand();
    }

    private void InvokeCommand()
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
        if (Command is Command command)
        {
            WeakEventManager.RemoveHandler(
                CommandPresentationWeakEvents.Changed,
                command.Presentation,
                this);
        }
        DetachCommandPresentationContent();
        base.OnDispose();
    }

    internal void RaiseClick() => OnClick();
}
