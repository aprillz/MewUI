using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class RangeBase
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new RangeBaseAutomationPeer(this);
}
