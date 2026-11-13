using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of a text element (<see cref="TextBlock"/>, <see cref="MarkupTextBlock"/>, <see cref="Label"/>): its shown text is its name.</summary>
internal sealed class TextBlockAutomationPeer : UIElementAutomationPeer, ITextProvider
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
            RaiseEventFromTree(AutomationEventKind.TextChanged);
        }

        base.OnOwnerPropertyChanged(property);
    }

    public string Text => GetName();

    public int CaretOffset => -1;

    public IReadOnlyList<TextRange> GetSelection() => [];

    public TextRange GetUnitAt(int offset, TextUnit unit) => AutomationTextUnits.UnitAt(Text, offset, unit);

    /// <summary>The element's bounds: a text element exposes no per-character geometry.</summary>
    public IReadOnlyList<Rect> GetBounds(int start, int length) => [GetBoundingRectangle()];

    public int GetOffsetAt(Point clientPoint) => 0;

    public void Select(int start, int length) => throw new InvalidOperationException("A text element cannot be selected.");
}

/// <summary>
/// The peer of an edit box (<see cref="TextBox"/>, <see cref="MultiLineTextBox"/>, <see cref="PasswordBox"/>). A
/// password box reports an empty value so the secret never leaves the control.
/// </summary>
internal sealed class TextBoxAutomationPeer : UIElementAutomationPeer, IValueProvider, ITextProvider
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
    internal void OnOwnerTextChanged()
    {
        RaisePropertyChangedEvent(AutomationProperty.ValueProperty, null, Value);
        RaiseEventFromTree(AutomationEventKind.TextChanged);
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == TextBase.IsReadOnlyProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsReadOnlyProperty, !IsReadOnly, IsReadOnly);
        }
        else if (property == TextBase.SelectionStartProperty || property == TextBase.SelectionLengthProperty)
        {
            RaiseEventFromTree(AutomationEventKind.TextSelectionChanged);
        }

        base.OnOwnerPropertyChanged(property);
    }

    /// <summary>The text a reader reads; empty for a password box.</summary>
    public string Text => Value;

    public int CaretOffset => IsPassword ? -1 : Math.Min(OwnerText.CaretPosition, Text.Length);

    public IReadOnlyList<TextRange> GetSelection()
    {
        int length = Text.Length;
        int start = Math.Clamp(OwnerText.SelectionStart, 0, length);
        int end = Math.Clamp(OwnerText.SelectionStart + OwnerText.SelectionLength, start, length);
        if (IsPassword || end == start)
        {
            return [];
        }
        else
        {
            return [new TextRange(start, end - start)];
        }
    }

    public TextRange GetUnitAt(int offset, TextUnit unit) => AutomationTextUnits.UnitAt(Text, offset, unit);

    public IReadOnlyList<Rect> GetBounds(int start, int length)
        => AutomationTextUnits.Bounds(Text, start, length, OwnerText.GetCharRectInWindow);

    public int GetOffsetAt(Point clientPoint) => AutomationTextUnits.OffsetAt(Text, clientPoint, OwnerText.GetCharRectInWindow);

    public void Select(int start, int length)
    {
        AutomationGuard.ThrowIfDisabled(this);
        int textLength = Text.Length;
        int first = Math.Clamp(start, 0, textLength);
        OwnerText.Select(first, Math.Clamp(length, 0, textLength - first));
    }
}
