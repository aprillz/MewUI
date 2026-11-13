using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class ToolTip
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new ToolTipAutomationPeer(this);
}
