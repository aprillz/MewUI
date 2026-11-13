using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class ScrollViewer
{
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        // The viewer inside an item control is covered by the item control's scroll pattern.
        if (Parent is ScrollableItemsBase or TreeView)
        {
            return null;
        }
        else
        {
            return new ScrollViewerAutomationPeer(this);
        }
    }
}
