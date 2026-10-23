using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a drop-down picker (<see cref="ComboBox"/>, <see cref="DatePicker"/>, <see cref="ColorPicker"/>): it
/// opens and closes its popup and reports the chosen value as text. The value is chosen in the popup, not typed.
/// </summary>
internal sealed class DropDownAutomationPeer : UIElementAutomationPeer, IExpandCollapseProvider, IValueProvider
{
    public DropDownAutomationPeer(DropDownBase owner)
        : base(owner)
    {
    }

    private DropDownBase OwnerDropDown => (DropDownBase)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.ComboBox;

    internal override bool IsLeaf => true;

    public ExpandCollapseState ExpandCollapseState
        => OwnerDropDown.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        AutomationGuard.ThrowIfDisabled(this);
        OwnerDropDown.IsDropDownOpen = true;
    }

    public void Collapse()
    {
        AutomationGuard.ThrowIfDisabled(this);
        OwnerDropDown.IsDropDownOpen = false;
    }

    public string Value => Owner switch
    {
        ComboBox comboBox => comboBox.SelectedText ?? string.Empty,
        DatePicker datePicker => datePicker.SelectedDate?.ToString(datePicker.DateFormat) ?? string.Empty,
        ColorPicker colorPicker => FormatColor(colorPicker.SelectedColor),
        _ => string.Empty,
    };

    public bool IsReadOnly => true;

    public void SetValue(string value) => AutomationGuard.ThrowIfReadOnly(IsReadOnly, this);

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == PopupOwnerBase.IsDropDownOpenProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ExpandCollapseStateProperty, null, ExpandCollapseState);
        }
        else if (property == ComboBox.SelectedIndexProperty || property == DatePicker.SelectedDateProperty
            || property == ColorPicker.SelectedColorProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ValueProperty, null, Value);
        }

        base.OnOwnerPropertyChanged(property);
    }

    private static string FormatColor(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}

/// <summary>
/// The peer of a button with a drop-down menu: a <see cref="SplitButton"/> runs its primary action on invoke, and a
/// <see cref="DropDownButton"/>, whose face only opens the menu, toggles the menu on invoke.
/// </summary>
internal sealed class MenuButtonAutomationPeer : UIElementAutomationPeer, IInvokeProvider, IExpandCollapseProvider
{
    public MenuButtonAutomationPeer(UIElement owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Button;

    protected override string GetOwnerName() => AutomationText.Of(((ContentControl)Owner).EffectiveContent);

    internal override bool IsLeaf => ((ContentControl)Owner).EffectiveContent is TextBlockBase or Label;

    private bool IsOpen => Owner switch
    {
        SplitButton split => split.IsDropDownOpen,
        DropDownButton dropDown => dropDown.IsDropDownOpen,
        _ => false,
    };

    public void Invoke()
    {
        AutomationGuard.ThrowIfDisabled(this);
        switch (Owner)
        {
            case SplitButton split:
                split.RaiseClick();
                break;
            case DropDownButton dropDown:
                dropDown.IsDropDownOpen = !dropDown.IsDropDownOpen;
                break;
        }
    }

    public ExpandCollapseState ExpandCollapseState => IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => SetOpen(true);

    public void Collapse() => SetOpen(false);

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == SplitButton.IsDropDownOpenProperty || property == DropDownButton.IsDropDownOpenProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ExpandCollapseStateProperty, null, ExpandCollapseState);
        }

        base.OnOwnerPropertyChanged(property);
    }

    private void SetOpen(bool open)
    {
        AutomationGuard.ThrowIfDisabled(this);
        switch (Owner)
        {
            case SplitButton split:
                split.IsDropDownOpen = open;
                break;
            case DropDownButton dropDown:
                dropDown.IsDropDownOpen = open;
                break;
        }
    }
}
