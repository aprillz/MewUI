using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class TreeRowContainer
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new TreeItemAutomationPeer(this);
}
