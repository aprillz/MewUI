using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of a <see cref="CheckBox"/>.</summary>
internal sealed class CheckBoxAutomationPeer : UIElementAutomationPeer, IToggleProvider
{
    public CheckBoxAutomationPeer(CheckBox owner)
        : base(owner)
    {
    }

    private CheckBox OwnerCheckBox => (CheckBox)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.CheckBox;

    protected override string GetOwnerName() => AutomationText.Of(OwnerCheckBox.EffectiveContent);

    internal override bool IsLeaf => OwnerCheckBox.EffectiveContent is TextBlockBase or Label;

    public ToggleState ToggleState => OwnerCheckBox.IsChecked switch
    {
        true => ToggleState.On,
        false => ToggleState.Off,
        _ => ToggleState.Indeterminate,
    };

    public void Toggle()
    {
        AutomationGuard.ThrowIfDisabled(this);
        OwnerCheckBox.Toggle();
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == CheckBox.IsCheckedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ToggleStateProperty, null, ToggleState);
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>The peer of a <see cref="ToggleButton"/> or a <see cref="ToggleSwitch"/>.</summary>
internal sealed class ToggleButtonAutomationPeer : UIElementAutomationPeer, IToggleProvider
{
    public ToggleButtonAutomationPeer(ToggleBase owner)
        : base(owner)
    {
    }

    private ToggleBase OwnerToggle => (ToggleBase)Owner;

    protected override AutomationControlType GetControlTypeCore()
        => Owner is ToggleSwitch ? AutomationControlType.CheckBox : AutomationControlType.Button;

    protected override string GetOwnerName() => AutomationText.Of(OwnerToggle.EffectiveContent);

    internal override bool IsLeaf => OwnerToggle.EffectiveContent is TextBlockBase or Label;

    public ToggleState ToggleState => OwnerToggle.IsChecked ? ToggleState.On : ToggleState.Off;

    public void Toggle()
    {
        AutomationGuard.ThrowIfDisabled(this);
        OwnerToggle.ToggleFromAutomation();
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ToggleBase.IsCheckedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ToggleStateProperty, null, ToggleState);
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>The peer of a <see cref="RadioButton"/>: selecting it checks it and clears its group.</summary>
internal sealed class RadioButtonAutomationPeer : UIElementAutomationPeer, ISelectionItemProvider
{
    public RadioButtonAutomationPeer(RadioButton owner)
        : base(owner)
    {
    }

    private RadioButton OwnerRadio => (RadioButton)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.RadioButton;

    protected override string GetOwnerName() => AutomationText.Of(OwnerRadio.EffectiveContent);

    internal override bool IsLeaf => OwnerRadio.EffectiveContent is TextBlockBase or Label;

    public bool IsSelected => OwnerRadio.IsChecked;

    public AutomationPeer? SelectionContainer => GetParent();

    public void Select()
    {
        AutomationGuard.ThrowIfDisabled(this);
        if (!OwnerRadio.IsChecked)
        {
            OwnerRadio.ToggleFromAutomation();
        }
    }

    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
        if (OwnerRadio.IsChecked)
        {
            throw new InvalidOperationException("A radio button is cleared by selecting another in its group.");
        }
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ToggleBase.IsCheckedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsSelectedProperty, !OwnerRadio.IsChecked, OwnerRadio.IsChecked);
        }

        base.OnOwnerPropertyChanged(property);
    }
}
