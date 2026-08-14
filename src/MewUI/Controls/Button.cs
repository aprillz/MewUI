using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button control that responds to clicks.
/// </summary>
public partial class Button : CommandSourceControl
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<Button>(DefaultStyles.CreateButtonStyle);
    private static readonly bool _flatStyleRegistered =
        FrameworkNamedStyles.Register("flat-button", BuiltInStyles.CreateFlatButtonStyle);
    private static readonly bool _accentStyleRegistered =
        FrameworkNamedStyles.Register("accent-button", BuiltInStyles.CreateAccentButtonStyle);

    /// <summary>
    /// Compatibility alias for <see cref="ContentControl.ContentProperty"/>, which now owns the slot.
    /// </summary>
    public static new readonly MewProperty<Element?> ContentProperty = ContentControl.ContentProperty;

    /// <summary>
    /// Compatibility alias for <see cref="CommandSourceControl.CommandProperty"/>, which now owns the slot.
    /// </summary>
    public static new readonly MewProperty<Command?> CommandProperty = CommandSourceControl.CommandProperty;

    public static readonly MewProperty<CommandPresentationMode> CommandPresentationModeProperty =
        MewProperty<CommandPresentationMode>.Register<Button>(nameof(CommandPresentationMode),
            CommandPresentationMode.None,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.UpdateCommandPresentationContent());

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

    private CommandContentPresenter? _commandPresentationContent;

    protected override void OnCommandChanged(Command? oldValue, Command? newValue)
    {
        if (oldValue != null)
        {
            WeakEventManager.RemoveHandler(
                CommandPresentationWeakEvents.Changed,
                oldValue.Presentation,
                this);
        }

        if (newValue != null)
        {
            WeakEventManager.AddHandler(
                CommandPresentationWeakEvents.Changed,
                newValue.Presentation,
                this,
                static button => button.UpdateCommandPresentationContent());
        }

        base.OnCommandChanged(oldValue, newValue);
        UpdateCommandPresentationContent();
    }

    protected override Element? SelectEffectiveContent()
        => GetPropertyValueTrace(ContentControl.ContentProperty).EffectiveSource != ValueSource.Default
            ? Content
            : _commandPresentationContent;

    protected override void OnContentChanged(Element? oldValue, Element? newValue)
    {
        base.OnContentChanged(oldValue, newValue);

        // An explicit content value retires the generated one, and clearing it brings that back.
        UpdateCommandPresentationContent();
    }

    protected override void OnValueSourceChanged(MewProperty property)
    {
        base.OnValueSourceChanged(property);

        // Assigning null over an unset Content is an explicit choice, so it retires the generated
        // content even though the value never changed.
        if (property.Id == ContentControl.ContentProperty.Id)
        {
            UpdateCommandPresentationContent();
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

        _commandPresentationContent = new CommandContentPresenter();
        RefreshCommandPresentationContent();
    }

    private void RefreshCommandPresentationContent()
    {
        if (_commandPresentationContent == null || Command == null)
        {
            return;
        }

        double iconSize = Theme.Metrics.CommandIconSize;
        if (!double.IsFinite(iconSize) || iconSize <= 0) iconSize = 16;
        var resolvedSize = IconTemplate.ResolveSize(iconSize, GetDpi() / 96.0);
        _commandPresentationContent.Update(Command.Presentation, CommandPresentationMode, resolvedSize);
    }

    private void UpdateCommandPresentationContent()
    {
        if (GetPropertyValueTrace(ContentControl.ContentProperty).EffectiveSource != ValueSource.Default)
        {
            DetachCommandPresentationContent();
        }
        else
        {
            EnsureCommandPresentationContent();
            RefreshCommandPresentationContent();
        }

        InvalidateEffectiveContent();
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
        if (oldTheme.Metrics.CommandIconSize != newTheme.Metrics.CommandIconSize)
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

    protected override bool ComputeIsEnabledSuggestion() => QueryCommandCanExecute();

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance || EffectiveContent != null)
        {
            return base.MeasureContent(availableSize);
        }

        var borderInset = GetBorderVisualInset();
        return new Size(Padding.HorizontalThickness + 20, Padding.VerticalThickness + 10)
            .Inflate(borderInset);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // A template owns the control's entire visuals; the built-in chrome would double-render.
        if (HasTemplateInstance)
        {
            return;
        }

        var bgColor = GetValue(BackgroundProperty);
        var borderColor = GetValue(BorderBrushProperty);

        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = CornerRadius;
        DrawBackgroundAndBorder(context, bounds, bgColor, borderColor, BorderThickness, radius);
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

    protected virtual void OnClick()
    {
        Click?.Invoke();
        InvokeCommand();
    }

    protected override void OnDispose()
    {
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
