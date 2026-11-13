using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class DropDownButton
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new MenuButtonAutomationPeer(this);
}
