using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of a text element (<see cref="TextBlock"/>, <see cref="MarkupTextBlock"/>, <see cref="Label"/>): its shown text is its name.</summary>
internal sealed class TextBlockAutomationPeer : UIElementAutomationPeer
{
    public TextBlockAutomationPeer(UIElement owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Text;

    protected override string GetOwnerName() => AutomationText.TextBelow(Owner);

    internal override bool IsLeaf => true;

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == TextBlock.TextProperty || property == MarkupTextBlock.TextProperty
            || property == Label.TextProperty || property == AccessText.RawTextProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.NameProperty, null, GetName());
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>
/// The peer of an edit box (<see cref="TextBox"/>, <see cref="MultiLineTextBox"/>, <see cref="PasswordBox"/>). A
/// password box reports an empty value so the secret never leaves the control.
/// </summary>
internal sealed class TextBoxAutomationPeer : UIElementAutomationPeer, IValueProvider
{
    public TextBoxAutomationPeer(TextBase owner)
        : base(owner)
    {
    }

    private TextBase OwnerText => (TextBase)Owner;

    /// <summary>True for a password box, whose value is never reported.</summary>
    public bool IsPassword => Owner is PasswordBox;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Edit;

    protected override string GetOwnerName() => OwnerText.Placeholder;

    internal override bool IsLeaf => true;

    public string Value => Owner switch
    {
        TextBox textBox => textBox.Text,
        MultiLineTextBox multiLine => multiLine.Text,
        _ => string.Empty,
    };

    public bool IsReadOnly => OwnerText.IsReadOnly;

    public void SetValue(string value)
    {
        AutomationGuard.ThrowIfDisabled(this);
        AutomationGuard.ThrowIfReadOnly(IsReadOnly, this);

        // Through the editor, as typing would: the change commits to a two-way binding like user input does.
        OwnerText.SelectAll();
        OwnerText.ReplaceSelection(value ?? string.Empty);
    }

    /// <summary>Called by the edit box on every text change, whether typed, set, or bound.</summary>
    internal void OnOwnerTextChanged() => RaisePropertyChangedEvent(AutomationProperty.ValueProperty, null, Value);

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == TextBase.IsReadOnlyProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsReadOnlyProperty, !IsReadOnly, IsReadOnly);
        }

        base.OnOwnerPropertyChanged(property);
    }
}
