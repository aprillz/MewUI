using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class ItemsControl
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new ItemsControlAutomationPeer(this);
}
