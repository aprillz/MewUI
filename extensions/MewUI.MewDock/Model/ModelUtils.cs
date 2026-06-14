namespace Aprillz.MewUI.MewDock.Model;

/// <summary>Selection-index adjustment helpers (port of model/Utils.ts).</summary>
internal static class ModelUtils
{
    // Panes and documents do not dock into each other. Pane-ness is the tab's own IsDocument=false (it travels with
    // the tab); containers derive it from their members, and border residents count by position. Faithful layouts
    // never mark a tab, so everything is Document there and the cross-kind check never blocks anything.
    internal static bool IsPane(Node node) => node switch
    {
        BorderNode => true,
        TabNode tab => !tab.IsDocument || tab.Parent is BorderNode,
        TabSetNode tabSet => !tabSet.IsDocument,
        RowNode row => FindFirstTabSet(row) is { IsDocument: false },
        _ => false,
    };

    // First tabset in a node's subtree (depth-first); a row's kind follows its first tabset (rows are homogeneous).
    private static TabSetNode? FindFirstTabSet(Node node)
    {
        if (node is TabSetNode tabSet)
        {
            return tabSet;
        }
        foreach (var child in node.Children)
        {
            if (FindFirstTabSet(child) is TabSetNode found)
            {
                return found;
            }
        }
        return null;
    }

    // Tear-off: true if the subtree still has a tab that is not the dragged node (nor inside it). A tabset whose only
    // tab is dragged - or a dock/row whose whole content is dragged - has no surviving content and collapses.
    internal static bool HasContent(Node node, Node dragging)
    {
        if (ReferenceEquals(node, dragging))
        {
            return false;
        }
        if (node is TabSetNode tabSet)
        {
            foreach (var child in tabSet.Children)
            {
                if (child is TabNode tab && !ReferenceEquals(tab, dragging))
                {
                    return true;
                }
            }
            return false;
        }
        foreach (var child in node.Children)
        {
            if (HasContent(child, dragging))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Selects the tab's new index in its parent after it was docked there.</summary>
    internal static void AdjustSelectedIndexAfterDock(TabNode node)
    {
        var parent = node.Parent;
        if (parent is TabSetNode or BorderNode)
        {
            var children = parent.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], node))
                {
                    SetSelected(parent, i);
                    return;
                }
            }
        }
    }

    /// <summary>Fixes the parent's selected index after the child at <paramref name="removedIndex"/> was removed.</summary>
    internal static void AdjustSelectedIndex(Node parent, int removedIndex)
    {
        if (parent is not (TabSetNode or BorderNode))
        {
            return;
        }

        int selectedIndex = GetSelected(parent);
        if (selectedIndex == -1)
        {
            return;
        }

        if (removedIndex == selectedIndex && parent.Children.Count > 0)
        {
            if (removedIndex >= parent.Children.Count)
            {
                // Removed the last tab; select the new last tab.
                SetSelected(parent, parent.Children.Count - 1);
            }
            // else leave the selected index as is (selects the next tab after this one).
        }
        else if (removedIndex < selectedIndex)
        {
            SetSelected(parent, selectedIndex - 1);
        }
        else if (removedIndex > selectedIndex)
        {
            // leave the selected index as is
        }
        else
        {
            SetSelected(parent, -1);
        }
    }

    private static int GetSelected(Node node) => node switch
    {
        TabSetNode ts => ts.Selected,
        BorderNode bn => bn.Selected,
        _ => -1,
    };

    private static void SetSelected(Node node, int index)
    {
        if (node is TabSetNode ts)
        {
            ts.SetSelected(index);
        }
        else if (node is BorderNode bn)
        {
            bn.SetSelected(index);
        }
    }
}
