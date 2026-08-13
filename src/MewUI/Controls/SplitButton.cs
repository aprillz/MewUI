namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button split into a primary face that runs <see cref="CommandSourceControl.Command"/> and a
/// drop-down face that opens a menu. When the command cannot execute, only the primary face goes
/// inactive; the menu stays reachable.
/// </summary>
public sealed partial class SplitButton : Button, IPopupOwner
{
    /// <summary>Name of the required template part that runs the primary action.</summary>
    public const string PART_PRIMARY_BUTTON = "PART_PrimaryButton";

    /// <summary>Name of the required template part that opens the menu.</summary>
    public const string PART_DROP_DOWN_BUTTON = "PART_DropDownButton";

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<SplitButton>(DefaultStyles.CreateSplitButtonStyle);

    public static readonly MewProperty<Menu?> DropDownMenuProperty =
        MewProperty<Menu?>.Register<SplitButton>(nameof(DropDownMenu), null,
            MewPropertyOptions.None,
            static (self, _, _) => self._menu.OnMenuChanged());

    public static readonly MewProperty<bool> IsDropDownOpenProperty =
        MewProperty<bool>.Register<SplitButton>(nameof(IsDropDownOpen), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState,
            static (self, _, newValue) => self.OnIsDropDownOpenChanged(newValue));

    public static readonly MewProperty<double> MaxDropDownHeightProperty =
        MewProperty<double>.Register<SplitButton>(nameof(MaxDropDownHeight), 320.0);

    private readonly DropDownMenuController _menu;
    private Button? _primaryPart;
    private Button? _dropDownPart;
    private bool _isPrimaryEnabled = true;

    static SplitButton()
    {
        CommandPresentationModeProperty.OverrideDefaultValue<SplitButton>(CommandPresentationMode.TextAndIcon);
    }

    public SplitButton()
    {
        _menu = new DropDownMenuController(
            this,
            () => DropDownMenu,
            () => MaxDropDownHeight,
            () => DropDownOpening?.Invoke(),
            () => DropDownClosed?.Invoke(),
            value => SetValue(IsDropDownOpenProperty, value));
    }

    /// <summary>Gets or sets the menu shown by the drop-down face.</summary>
    public Menu? DropDownMenu
    {
        get => GetValue(DropDownMenuProperty);
        set => SetValue(DropDownMenuProperty, value);
    }

    /// <summary>Gets or sets whether the menu is open.</summary>
    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>Gets or sets the maximum menu height; the menu scrolls beyond it.</summary>
    public double MaxDropDownHeight
    {
        get => GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>Occurs right before the menu is shown, so items can be rebuilt.</summary>
    public event Action? DropDownOpening;

    /// <summary>Occurs after the menu closed.</summary>
    public event Action? DropDownClosed;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _primaryPart = GetTemplateChild<Button>(PART_PRIMARY_BUTTON)
            ?? throw new InvalidOperationException(
                $"The {nameof(SplitButton)} template must register a {nameof(Button)} part named '{PART_PRIMARY_BUTTON}'.");
        _dropDownPart = GetTemplateChild<Button>(PART_DROP_DOWN_BUTTON)
            ?? throw new InvalidOperationException(
                $"The {nameof(SplitButton)} template must register a {nameof(Button)} part named '{PART_DROP_DOWN_BUTTON}'.");

        _primaryPart.Click += OnPrimaryPartClick;
        _dropDownPart.Click += OnDropDownPartClick;
        UpdatePrimaryPartEnabled();
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        if (_primaryPart != null)
        {
            _primaryPart.Click -= OnPrimaryPartClick;
            _primaryPart = null;
        }

        if (_dropDownPart != null)
        {
            _dropDownPart.Click -= OnDropDownPartClick;
            _dropDownPart = null;
        }
    }

    private void OnPrimaryPartClick()
    {
        // The parts are not focusable, so activation keeps the keyboard on the owner. The part does
        // not carry the command; the owner runs it once through the activation gate below.
        Focus();
        OnClick();
    }

    private void OnDropDownPartClick()
    {
        Focus();
        ToggleDropDown();
    }

    /// <summary>
    /// The single activation gate. Pointer forwarding, Space, Enter, the access key and
    /// <see cref="Button.RaiseClick"/> all arrive here, so a command that cannot execute runs from
    /// no path at all.
    /// </summary>
    protected override void OnClick()
    {
        if (!_isPrimaryEnabled)
        {
            return;
        }

        base.OnClick();
    }

    protected override bool ComputeIsEnabledSuggestion()
    {
        // Button promotes CanExecute to the whole control; a split button keeps the drop-down
        // reachable and applies the same answer to the primary face only.
        _isPrimaryEnabled = QueryCommandCanExecute();
        UpdatePrimaryPartEnabled();
        return true;
    }

    private void UpdatePrimaryPartEnabled()
    {
        if (_primaryPart != null)
        {
            _primaryPart.IsEnabled = _isPrimaryEnabled;
        }
    }

    private void OnIsDropDownOpenChanged(bool newValue)
    {
        if (_menu.IsSynchronizing)
        {
            return;
        }

        if (newValue)
        {
            _menu.Open();
        }
        else
        {
            _menu.Close();
        }
    }

    private void ToggleDropDown() => IsDropDownOpen = !IsDropDownOpen;

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsDropDownOpen)
        {
            return state with { Flags = state.Flags | VisualStateFlags.Active };
        }

        return state;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && IsEffectivelyEnabled && (e.Key == Key.F4 || (e.Key == Key.Down && e.AltKey)))
        {
            ToggleDropDown();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _menu.NotifyThemeChanged(oldTheme, newTheme);
    }

    void IPopupOwner.OnPopupClosed(UIElement popup, PopupCloseKind kind) => _menu.OnPopupClosed(popup);

    protected override void OnDispose()
    {
        _menu.Dispose();
        base.OnDispose();
    }
}
