using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a <see cref="TreeView"/>. Its realized rows are nested by depth: a row is the child of the row of its
/// parent node when that row is realized, otherwise of the tree.
/// </summary>
internal sealed class TreeViewAutomationPeer : UIElementAutomationPeer, ISelectionProvider, IScrollProvider
{
    public TreeViewAutomationPeer(TreeView owner)
        : base(owner)
    {
    }

    private TreeView OwnerTree => (TreeView)Owner;

    private ScrollViewer Viewer => OwnerTree.ScrollHost;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Tree;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        var rows = RealizedRows(OwnerTree);
        var children = new List<AutomationPeer>();
        foreach (var (index, row) in rows)
        {
            int parentIndex = ParentIndex(OwnerTree.ItemsSource, index);
            if (parentIndex < 0 || !rows.ContainsKey(parentIndex))
            {
                AddPeer(row, children);
            }
        }

        return children;
    }

    public bool CanSelectMultiple => OwnerTree.SelectionMode != ItemsSelectionMode.Single;

    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        var selection = new List<AutomationPeer>();
        foreach (var (index, row) in RealizedRows(OwnerTree))
        {
            if (OwnerTree.IsSelected(index) && row.GetAutomationPeer() is AutomationPeer peer)
            {
                selection.Add(peer);
            }
        }

        return selection;
    }

    public bool HorizontallyScrollable => ScrollViewerScroll.CanScroll(Viewer, horizontal: true);

    public bool VerticallyScrollable => ScrollViewerScroll.CanScroll(Viewer, horizontal: false);

    public double HorizontalScrollPercent => ScrollViewerScroll.Percent(Viewer, horizontal: true);

    public double VerticalScrollPercent => ScrollViewerScroll.Percent(Viewer, horizontal: false);

    public double HorizontalViewSize => ScrollViewerScroll.ViewSize(Viewer, horizontal: true);

    public double VerticalViewSize => ScrollViewerScroll.ViewSize(Viewer, horizontal: false);

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
        => ScrollViewerScroll.Scroll(this, Viewer, horizontalAmount, verticalAmount);

    public void SetScrollPercent(double horizontalPercent, double verticalPercent)
        => ScrollViewerScroll.SetPercent(this, Viewer, horizontalPercent, verticalPercent);

    /// <summary>The realized rows by flat index, in index order.</summary>
    internal static SortedDictionary<int, ItemContainer> RealizedRows(TreeView tree)
    {
        var rows = new SortedDictionary<int, ItemContainer>();
        tree.VisitRealizedContainers((index, element) =>
        {
            if (element is ItemContainer container && container.IsVisible)
            {
                rows[index] = container;
            }
        });
        return rows;
    }

    /// <summary>The flat index of the node's parent: the nearest earlier row one level up, or -1 at the top level.</summary>
    internal static int ParentIndex(ITreeItemsView items, int index)
    {
        int depth = items.GetDepth(index);
        for (int candidate = index - 1; candidate >= 0 && depth > 0; candidate--)
        {
            if (items.GetDepth(candidate) < depth)
            {
                return candidate;
            }
        }

        return -1;
    }

    internal static void AddPeer(ItemContainer row, List<AutomationPeer> children)
    {
        var peer = row.GetAutomationPeer();
        if (peer != null)
        {
            children.Add(peer);
        }
    }
}

/// <summary>The peer of a tree row: a tree item that expands, collapses, and is selected like a click on its text.</summary>
internal sealed class TreeItemAutomationPeer : UIElementAutomationPeer, IExpandCollapseProvider, ISelectionItemProvider, IScrollItemProvider
{
    public TreeItemAutomationPeer(ItemContainer owner)
        : base(owner)
    {
    }

    private ItemContainer OwnerRow => (ItemContainer)Owner;

    private TreeView? OwnerTree
    {
        get
        {
            for (Element? current = Owner.Parent; current != null; current = current.Parent)
            {
                if (current is TreeView tree)
                {
                    return tree;
                }
            }

            return null;
        }
    }

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.TreeItem;

    protected override string GetOwnerName()
    {
        var tree = OwnerTree;
        int index = OwnerRow.Index;
        if (tree != null && (uint)index < (uint)tree.ItemsSource.Count)
        {
            string text = tree.ItemsSource.GetText(index);
            if (text.Length > 0)
            {
                return text;
            }
        }

        return AutomationText.Of(OwnerRow.EffectiveContent);
    }

    protected override AutomationPeer? GetParentCore()
    {
        var tree = OwnerTree;
        if (tree == null)
        {
            return base.GetParentCore();
        }

        int parentIndex = TreeViewAutomationPeer.ParentIndex(tree.ItemsSource, OwnerRow.Index);
        if (parentIndex >= 0 && TreeViewAutomationPeer.RealizedRows(tree).TryGetValue(parentIndex, out var parentRow))
        {
            return parentRow.GetAutomationPeer();
        }

        return tree.GetAutomationPeer();
    }

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        var tree = OwnerTree;
        if (tree == null)
        {
            return children;
        }

        int index = OwnerRow.Index;
        int depth = tree.ItemsSource.GetDepth(index);
        foreach (var (rowIndex, row) in TreeViewAutomationPeer.RealizedRows(tree))
        {
            if (rowIndex <= index)
            {
                continue;
            }

            int rowDepth = tree.ItemsSource.GetDepth(rowIndex);
            if (rowDepth <= depth)
            {
                break;
            }

            if (rowDepth == depth + 1)
            {
                TreeViewAutomationPeer.AddPeer(row, children);
            }
        }

        return children;
    }

    public ExpandCollapseState ExpandCollapseState
    {
        get
        {
            var tree = OwnerTree;
            int index = OwnerRow.Index;
            if (tree == null || (uint)index >= (uint)tree.ItemsSource.Count || !tree.ItemsSource.GetHasChildren(index))
            {
                return ExpandCollapseState.LeafNode;
            }

            return tree.ItemsSource.GetIsExpanded(index) ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        }
    }

    public void Expand() => SetExpanded(true);

    public void Collapse() => SetExpanded(false);

    public bool IsSelected => OwnerRow.IsSelected;

    public AutomationPeer? SelectionContainer => OwnerTree?.GetAutomationPeer();

    public void Select()
    {
        AutomationGuard.ThrowIfDisabled(this);
        RequireTree().SelectFromAutomation(OwnerRow.Index);
    }

    public void AddToSelection()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var tree = RequireTree();
        var multi = tree.ItemsSource.AsMultiSelectable();
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SetSelected(OwnerRow.Index, true);
        }
        else if (tree.ItemsSource.SelectedIndex < 0 || tree.ItemsSource.SelectedIndex == OwnerRow.Index)
        {
            Select();
        }
        else
        {
            throw new InvalidOperationException("This tree selects one item at a time.");
        }
    }

    public void RemoveFromSelection()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var tree = RequireTree();
        var multi = tree.ItemsSource.AsMultiSelectable();
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SetSelected(OwnerRow.Index, false);
        }
        else if (tree.ItemsSource.SelectedIndex == OwnerRow.Index)
        {
            tree.ItemsSource.SelectedIndex = -1;
        }
    }

    public void ScrollIntoView() => RequireTree().ScrollIntoView(OwnerRow.Index);

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ItemContainer.IsSelectedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsSelectedProperty, !IsSelected, IsSelected);
        }
        else if (property == ItemContainer.IndexProperty || property == ItemContainer.ItemProperty)
        {
            RaiseRebound();
        }

        base.OnOwnerPropertyChanged(property);
    }

    private void SetExpanded(bool expanded)
    {
        AutomationGuard.ThrowIfDisabled(this);
        if (ExpandCollapseState == ExpandCollapseState.LeafNode)
        {
            throw new InvalidOperationException($"'{GetName()}' has no children to show.");
        }

        if (RequireTree().SetExpandedFromAutomation(OwnerRow.Index, expanded))
        {
            RaisePropertyChangedEvent(AutomationProperty.ExpandCollapseStateProperty, null, ExpandCollapseState);
        }
    }

    private TreeView RequireTree()
        => OwnerTree ?? throw new InvalidOperationException("The item is no longer in a tree.");
}
