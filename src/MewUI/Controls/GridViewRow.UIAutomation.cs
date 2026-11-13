using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class GridViewRow
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new GridViewRowAutomationPeer(this);
}
