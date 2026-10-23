using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of a <see cref="GridView"/>: the column headers, then the realized rows with their cells as children.</summary>
internal sealed class GridViewAutomationPeer : ItemsControlAutomationPeer, ISelectionProvider
{
    public GridViewAutomationPeer(GridView owner)
        : base(owner)
    {
    }

    private GridView OwnerGrid => (GridView)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.DataGrid;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        CollectChildren(OwnerGrid.HeaderElement, children);
        children.AddRange(base.GetChildrenCore());
        return children;
    }

    public bool CanSelectMultiple => OwnerGrid.SelectionMode != ItemsSelectionMode.Single;

    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        var selection = new List<AutomationPeer>();
        foreach (var child in GetChildren())
        {
            if (child is GridViewRowAutomationPeer row && row.IsSelected)
            {
                selection.Add(child);
            }
        }

        return selection;
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == GridView.SelectedIndexProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.SelectionProperty, null, OwnerGrid.SelectedIndex);
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>The peer of a <see cref="GridViewRow"/>: a data item named by its row text, with its cells as children.</summary>
internal sealed class GridViewRowAutomationPeer : UIElementAutomationPeer, ISelectionItemProvider, IScrollItemProvider
{
    public GridViewRowAutomationPeer(GridViewRow owner)
        : base(owner)
    {
    }

    private GridViewRow OwnerRow => (GridViewRow)Owner;

    private GridView? OwnerGrid => ItemsControlAutomationPeer.OwningItemsControl(Owner) as GridView;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.DataItem;

    protected override string GetOwnerName()
    {
        var grid = OwnerGrid;
        int index = OwnerRow.Index;
        if (grid != null && (uint)index < (uint)grid.RowItems.Count)
        {
            return grid.RowItems.GetText(index);
        }

        return string.Empty;
    }

    public bool IsSelected => OwnerRow.IsSelected;

    public AutomationPeer? SelectionContainer => OwnerGrid?.GetAutomationPeer();

    public void Select()
    {
        AutomationGuard.ThrowIfDisabled(this);
        RequireGrid().SelectRowFromAutomation(OwnerRow.Index);
    }

    public void AddToSelection()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var grid = RequireGrid();
        var multi = grid.RowMultiView;
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SetSelected(OwnerRow.Index, true);
        }
        else if (grid.SelectedIndex < 0 || grid.SelectedIndex == OwnerRow.Index)
        {
            Select();
        }
        else
        {
            throw new InvalidOperationException("This grid selects one row at a time.");
        }
    }

    public void RemoveFromSelection()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var grid = RequireGrid();
        var multi = grid.RowMultiView;
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SetSelected(OwnerRow.Index, false);
        }
        else if (grid.SelectedIndex == OwnerRow.Index)
        {
            grid.CommitFromAutomation(GridView.SelectedIndexProperty, -1);
        }
    }

    public void ScrollIntoView() => RequireGrid().ScrollIntoView(OwnerRow.Index);

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == GridViewRow.IsSelectedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsSelectedProperty, !IsSelected, IsSelected);
        }
        else if (property == GridViewRow.ItemProperty)
        {
            RaiseRebound();
        }

        base.OnOwnerPropertyChanged(property);
    }

    private GridView RequireGrid()
        => OwnerGrid ?? throw new InvalidOperationException("The row is no longer in a grid.");
}
