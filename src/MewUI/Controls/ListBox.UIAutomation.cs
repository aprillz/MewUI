using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class ListBox
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new ListBoxAutomationPeer(this);
}
