using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a scrolling item control (<see cref="ListBox"/>, <see cref="ItemsControl"/>,
/// <see cref="NavigationList"/>): a list whose children are its realized item containers.
/// </summary>
internal class ItemsControlAutomationPeer : UIElementAutomationPeer, IScrollProvider
{
    public ItemsControlAutomationPeer(ScrollableItemsBase owner)
        : base(owner)
    {
    }

    private ScrollViewer Viewer => ((ScrollableItemsBase)Owner).ScrollHost;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.List;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => ScrollViewerScroll.ContentChildren(Viewer);

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

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ScrollableItemsBase.VerticalOffsetProperty || property == ScrollableItemsBase.HorizontalOffsetProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ScrollPercentProperty, null, null);
        }

        base.OnOwnerPropertyChanged(property);
    }

    /// <summary>The nearest item control above <paramref name="element"/>, or null.</summary>
    internal static ScrollableItemsBase? OwningItemsControl(Element element)
    {
        for (Element? current = element.Parent; current != null; current = current.Parent)
        {
            if (current is ScrollableItemsBase items)
            {
                return items;
            }
        }

        return null;
    }
}

/// <summary>The peer of a <see cref="ListBox"/>: a list that reports and changes its selection.</summary>
internal sealed class ListBoxAutomationPeer : ItemsControlAutomationPeer, ISelectionProvider
{
    public ListBoxAutomationPeer(ListBox owner)
        : base(owner)
    {
    }

    private ListBox OwnerList => (ListBox)Owner;

    public bool CanSelectMultiple => OwnerList.SelectionMode != ItemsSelectionMode.Single;

    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        var selection = new List<AutomationPeer>();
        foreach (var child in GetChildren())
        {
            if (child is ISelectionItemProvider item && item.IsSelected)
            {
                selection.Add(child);
            }
        }

        return selection;
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ListBox.SelectedIndexProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.SelectionProperty, null, OwnerList.SelectedIndex);
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>The peer of an item container: a list item named by its item's text, which its list can scroll to.</summary>
internal class ItemContainerAutomationPeer : UIElementAutomationPeer, IScrollItemProvider
{
    public ItemContainerAutomationPeer(ItemContainer owner)
        : base(owner)
    {
    }

    private protected ItemContainer OwnerContainer => (ItemContainer)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetOwnerName()
    {
        int index = OwnerContainer.Index;
        var source = ItemsControlAutomationPeer.OwningItemsControl(Owner) switch
        {
            ListBox list => list.ItemsSource,
            NavigationList navigation => navigation.ItemsSource,
            _ => null,
        };

        if (source != null && (uint)index < (uint)source.Count)
        {
            string text = source.GetText(index);
            if (text.Length > 0)
            {
                return text;
            }
        }

        return AutomationText.Of(OwnerContainer.EffectiveContent);
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ItemContainer.IndexProperty || property == ItemContainer.ItemProperty)
        {
            RaiseRebound();
        }

        base.OnOwnerPropertyChanged(property);
    }

    public void ScrollIntoView()
    {
        int index = OwnerContainer.Index;
        switch (ItemsControlAutomationPeer.OwningItemsControl(Owner))
        {
            case ListBox list:
                list.ScrollIntoView(index);
                break;
            case ItemsControl items:
                items.ScrollIntoView(index);
                break;
            case NavigationList navigation:
                navigation.ScrollIntoView(index);
                break;
            default:
                throw new InvalidOperationException("The item is not in a scrolling list.");
        }
    }
}

/// <summary>The peer of a <see cref="ListBox"/> item: selecting it selects the item as a click would.</summary>
internal sealed class ListBoxItemAutomationPeer : ItemContainerAutomationPeer, ISelectionItemProvider
{
    public ListBoxItemAutomationPeer(ItemContainer owner)
        : base(owner)
    {
    }

    private ListBox? OwnerList => ItemsControlAutomationPeer.OwningItemsControl(Owner) as ListBox;

    public bool IsSelected => OwnerContainer.IsSelected;

    public AutomationPeer? SelectionContainer => OwnerList?.GetAutomationPeer();

    public void Select()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var list = RequireList();
        var multi = list.ItemsSource.AsMultiSelectable();
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SelectSingle(OwnerContainer.Index);
        }
        else
        {
            list.CommitFromAutomation(ListBox.SelectedIndexProperty, OwnerContainer.Index);
        }
    }

    public void AddToSelection()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var list = RequireList();
        var multi = list.ItemsSource.AsMultiSelectable();
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SetSelected(OwnerContainer.Index, true);
        }
        else if (list.SelectedIndex < 0 || list.SelectedIndex == OwnerContainer.Index)
        {
            Select();
        }
        else
        {
            throw new InvalidOperationException("This list selects one item at a time.");
        }
    }

    public void RemoveFromSelection()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var list = RequireList();
        var multi = list.ItemsSource.AsMultiSelectable();
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SetSelected(OwnerContainer.Index, false);
        }
        else if (list.SelectedIndex == OwnerContainer.Index)
        {
            list.CommitFromAutomation(ListBox.SelectedIndexProperty, -1);
        }
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ItemContainer.IsSelectedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsSelectedProperty, !IsSelected, IsSelected);
        }

        base.OnOwnerPropertyChanged(property);
    }

    private ListBox RequireList()
        => OwnerList ?? throw new InvalidOperationException("The item is no longer in a list.");
}
