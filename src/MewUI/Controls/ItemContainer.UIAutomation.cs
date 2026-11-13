using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class ItemContainer
{
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        if (ItemsControlAutomationPeer.OwningItemsControl(this) is ListBox)
        {
            return new ListBoxItemAutomationPeer(this);
        }
        else
        {
            return new ItemContainerAutomationPeer(this);
        }
    }
}
