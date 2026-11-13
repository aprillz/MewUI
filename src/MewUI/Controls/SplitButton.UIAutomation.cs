using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class SplitButton
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new MenuButtonAutomationPeer(this);
}
