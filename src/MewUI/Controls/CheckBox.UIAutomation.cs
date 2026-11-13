using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class CheckBox
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new CheckBoxAutomationPeer(this);
}
