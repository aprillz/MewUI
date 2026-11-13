using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class DropDownBase
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new DropDownAutomationPeer(this);
}
