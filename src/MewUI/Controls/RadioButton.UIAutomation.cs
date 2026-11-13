using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class RadioButton
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new RadioButtonAutomationPeer(this);
}
