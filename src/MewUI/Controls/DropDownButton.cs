namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button whose activation opens a drop-down menu. It has no primary command: every activation
/// path opens the menu, and the menu items carry the commands.
/// </summary>
public sealed partial class DropDownButton : ContentControl, IPopupOwner
{
    /// <summary>Name of the required template part that carries the button face and opens the menu.</summary>
    public const string PART_DROP_DOWN_BUTTON = "PART_DropDownButton";

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<DropDownButton>(DefaultStyles.CreateDropDownButtonStyle);

    public static readonly MewProperty<Menu?> DropDownMenuProperty =
        MewProperty<Menu?>.Register<DropDownButton>(nameof(DropDownMenu), null,
            MewPropertyOptions.None,
            static (self, _, _) => self._menu.OnMenuChanged());

    public static readonly MewProperty<bool> IsDropDownOpenProperty =
        MewProperty<bool>.Register<DropDownButton>(nameof(IsDropDownOpen), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState,
            static (self, _, newValue) => self.OnIsDropDownOpenChanged(newValue));

    public static readonly MewProperty<double> MaxDropDownHeightProperty =
        MewProperty<double>.Register<DropDownButton>(nameof(MaxDropDownHeight), 320.0);

    private readonly DropDownMenuController _menu;
    private Button? _facePart;

    static DropDownButton()
    {
        FocusableProperty.OverrideDefaultValue<DropDownButton>(true);
    }

    public DropDownButton()
    {
        _menu = new DropDownMenuController(
            this,
            () => DropDownMenu,
            () => MaxDropDownHeight,
            () => DropDownOpening?.Invoke(),
            () => DropDownClosed?.Invoke(),
            value => SetValue(IsDropDownOpenProperty, value));
    }

    /// <summary>Gets or sets the menu shown by the drop-down.</summary>
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

        _facePart = GetTemplateChild<Button>(PART_DROP_DOWN_BUTTON)
            ?? throw new InvalidOperationException(
                $"The {nameof(DropDownButton)} template must register a {nameof(Button)} part named '{PART_DROP_DOWN_BUTTON}'.");
        _facePart.Click += OnFaceClick;
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        if (_facePart != null)
        {
            _facePart.Click -= OnFaceClick;
            _facePart = null;
        }
    }

    private void OnFaceClick()
    {
        // The part is not focusable, so activation keeps the keyboard on the owner.
        Focus();
        ToggleDropDown();
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

    internal override void OnAccessKey()
    {
        Focus();
        ToggleDropDown();
    }

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
        base.OnKeyDown(e);

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key is Key.Space or Key.Enter or Key.F4 || (e.Key == Key.Down && e.AltKey))
        {
            ToggleDropDown();
            e.Handled = true;
        }
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
