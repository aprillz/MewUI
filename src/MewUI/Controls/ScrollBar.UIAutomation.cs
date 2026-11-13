using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class ScrollBar
{
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        // The bars of a scroll viewer are covered by its scroll pattern.
        if (Parent is ScrollViewer)
        {
            return null;
        }
        else
        {
            return new RangeBaseAutomationPeer(this);
        }
    }
}
