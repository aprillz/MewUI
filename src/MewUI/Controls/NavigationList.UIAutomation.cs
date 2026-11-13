using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class NavigationList
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new ItemsControlAutomationPeer(this);
}
