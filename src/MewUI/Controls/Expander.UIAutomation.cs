using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class Expander
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new ExpanderAutomationPeer(this);
}
