using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class TabHeaderButton
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new TabItemAutomationPeer(this);
}
