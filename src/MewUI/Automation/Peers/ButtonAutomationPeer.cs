using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of a <see cref="Button"/> and the buttons derived from it.</summary>
internal sealed class ButtonAutomationPeer : UIElementAutomationPeer, IInvokeProvider
{
    public ButtonAutomationPeer(Button owner)
        : base(owner)
    {
    }

    private Button OwnerButton => (Button)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Button;

    protected override string GetOwnerName() => AutomationText.Of(OwnerButton.EffectiveContent);

    /// <summary>Text content is the button's name, so it is not repeated as a child.</summary>
    internal override bool IsLeaf => OwnerButton.EffectiveContent is TextBlockBase or Label;

    public void Invoke()
    {
        AutomationGuard.ThrowIfDisabled(this);
        OwnerButton.RaiseClick();
    }
}
