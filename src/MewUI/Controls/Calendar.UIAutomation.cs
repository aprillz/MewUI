using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class Calendar
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new GroupingAutomationPeer(this, AutomationControlType.Calendar);
}
