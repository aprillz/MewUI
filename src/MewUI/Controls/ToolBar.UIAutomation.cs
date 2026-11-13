using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class ToolBar
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new GroupingAutomationPeer(this, AutomationControlType.ToolBar);
}
