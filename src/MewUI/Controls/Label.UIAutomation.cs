using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class Label
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new TextBlockAutomationPeer(this);
}
