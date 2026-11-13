using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class GroupBox
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new GroupBoxAutomationPeer(this);
}
