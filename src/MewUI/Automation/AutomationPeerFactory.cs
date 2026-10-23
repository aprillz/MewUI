using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>Creates the built-in peer of a framework control; a derived type is matched before its base.</summary>
internal static class AutomationPeerFactory
{
    internal static AutomationPeer? Create(UIElement element)
    {
        if (IsBelowLeaf(element))
        {
            return null;
        }

        return CreateForType(element);
    }

    /// <summary>
    /// True when the nearest ancestor in the tree is a leaf. An element below a leaf got no peer, so the walk passes
    /// over it to the leaf itself.
    /// </summary>
    private static bool IsBelowLeaf(UIElement element)
    {
        for (Element? ancestor = element.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is UIElement ancestorElement && ancestorElement.GetAutomationPeer() is UIElementAutomationPeer peer)
            {
                return peer.IsLeaf;
            }
        }

        return false;
    }

    private static AutomationPeer? CreateForType(UIElement element) => element switch
    {
        Window window => new WindowAutomationPeer(window),
        SplitButton or DropDownButton => new MenuButtonAutomationPeer(element),
        Button button => new ButtonAutomationPeer(button),
        CheckBox checkBox => new CheckBoxAutomationPeer(checkBox),
        RadioButton radioButton => new RadioButtonAutomationPeer(radioButton),
        ToggleBase toggle => new ToggleButtonAutomationPeer(toggle),
        SegmentButton segment => new SegmentButtonAutomationPeer(segment),
        TabHeaderButton header => new TabItemAutomationPeer(header),
        TextBlockBase or Label => new TextBlockAutomationPeer(element),
        TextBase text => new TextBoxAutomationPeer(text),

        // The bars of a scroll viewer and the viewer inside an item control are covered by the scroll pattern.
        ScrollBar when element.Parent is ScrollViewer => null,
        RangeBase range => new RangeBaseAutomationPeer(range),
        ScrollViewer when element.Parent is ScrollableItemsBase or TreeView => null,
        ScrollViewer viewer => new ScrollViewerAutomationPeer(viewer),

        GridView grid => new GridViewAutomationPeer(grid),
        GridViewRow row => new GridViewRowAutomationPeer(row),
        ListBox list => new ListBoxAutomationPeer(list),
        ItemsControl or NavigationList => new ItemsControlAutomationPeer((ScrollableItemsBase)element),
        TreeView tree => new TreeViewAutomationPeer(tree),
        TreeRowContainer row => new TreeItemAutomationPeer(row),
        ItemContainer container when ItemsControlAutomationPeer.OwningItemsControl(container) is ListBox
            => new ListBoxItemAutomationPeer(container),
        ItemContainer container => new ItemContainerAutomationPeer(container),

        DropDownBase dropDown => new DropDownAutomationPeer(dropDown),
        MenuBar or ContextMenu => new MenuAutomationPeer(element),
        TabControl tabs => new TabControlAutomationPeer(tabs),
        Expander expander => new ExpanderAutomationPeer(expander),
        GroupBox groupBox => new GroupBoxAutomationPeer(groupBox),
        Image image => new ImageAutomationPeer(image),
        GlyphElement glyph => new ImageAutomationPeer(glyph),
        ToolTip toolTip => new ToolTipAutomationPeer(toolTip),
        ToolBar => new GroupingAutomationPeer(element, AutomationControlType.ToolBar),
        Calendar => new GroupingAutomationPeer(element, AutomationControlType.Calendar),
        Popup => new GroupingAutomationPeer(element, AutomationControlType.Pane),
        _ => null,
    };
}
