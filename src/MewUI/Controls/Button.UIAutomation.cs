using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class Button
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new ButtonAutomationPeer(this);
}
