using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of a <see cref="TabControl"/>: its children are the tab headers, then the selected tab's content.</summary>
internal sealed class TabControlAutomationPeer : UIElementAutomationPeer, ISelectionProvider
{
    public TabControlAutomationPeer(TabControl owner)
        : base(owner)
    {
    }

    private TabControl OwnerTabs => (TabControl)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Tab;

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => true;

    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        var selection = new List<AutomationPeer>();
        foreach (var child in GetChildren())
        {
            if (child is TabItemAutomationPeer tab && tab.IsSelected)
            {
                selection.Add(child);
            }
        }

        return selection;
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == TabControl.SelectedIndexProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.SelectionProperty, null, OwnerTabs.SelectedIndex);
            RaiseStructureChangedEvent();
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>The peer of a tab header, named by the tab's header text.</summary>
internal sealed class TabItemAutomationPeer : UIElementAutomationPeer, ISelectionItemProvider
{
    public TabItemAutomationPeer(TabHeaderButton owner)
        : base(owner)
    {
    }

    private TabHeaderButton OwnerHeader => (TabHeaderButton)Owner;

    private TabControl? OwnerTabs => OwnerHeader.OwnerTabControl;

    private TabItem? Tab
    {
        get
        {
            var tabs = OwnerTabs;
            int index = OwnerHeader.Index;
            return tabs != null && (uint)index < (uint)tabs.Tabs.Count ? tabs.Tabs[index] : null;
        }
    }

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.TabItem;

    protected override string GetOwnerName()
    {
        string? headerText = Tab?.HeaderText;
        return string.IsNullOrEmpty(headerText) ? AutomationText.Of(OwnerHeader.EffectiveContent) : headerText;
    }

    internal override bool IsLeaf => OwnerHeader.EffectiveContent is TextBlockBase or Label;

    public bool IsSelected => OwnerHeader.IsSelected;

    public AutomationPeer? SelectionContainer => OwnerTabs?.GetAutomationPeer();

    public void Select()
    {
        AutomationGuard.ThrowIfDisabled(this);
        var tabs = OwnerTabs ?? throw new InvalidOperationException("The tab is no longer in a tab control.");
        tabs.SelectFromAutomation(OwnerHeader.Index);
    }

    public void AddToSelection()
    {
        if (!IsSelected)
        {
            Select();
        }
    }

    public void RemoveFromSelection()
    {
        if (IsSelected)
        {
            throw new InvalidOperationException("A tab control always has one tab selected.");
        }
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == TabHeaderButton.IsSelectedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsSelectedProperty, !IsSelected, IsSelected);
        }

        base.OnOwnerPropertyChanged(property);
    }
}
