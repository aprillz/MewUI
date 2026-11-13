using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class Popup
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new GroupingAutomationPeer(this, AutomationControlType.Pane);
}
