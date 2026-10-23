using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of an <see cref="Expander"/>: a group named by its header that shows and hides its content.</summary>
internal sealed class ExpanderAutomationPeer : UIElementAutomationPeer, IExpandCollapseProvider
{
    public ExpanderAutomationPeer(Expander owner)
        : base(owner)
    {
    }

    private Expander OwnerExpander => (Expander)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Group;

    protected override string GetOwnerName() => AutomationText.Of(OwnerExpander.Header);

    public ExpandCollapseState ExpandCollapseState
        => OwnerExpander.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        AutomationGuard.ThrowIfDisabled(this);
        Owner.CommitFromAutomation(Expander.IsExpandedProperty, true);
    }

    public void Collapse()
    {
        AutomationGuard.ThrowIfDisabled(this);
        Owner.CommitFromAutomation(Expander.IsExpandedProperty, false);
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == Expander.IsExpandedProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ExpandCollapseStateProperty, null, ExpandCollapseState);
            RaiseStructureChangedEvent();
        }
        else if (property == HeaderedContentControl.HeaderProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.NameProperty, null, GetName());
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>The peer of a <see cref="GroupBox"/>: a group named by its header.</summary>
internal sealed class GroupBoxAutomationPeer : UIElementAutomationPeer
{
    public GroupBoxAutomationPeer(GroupBox owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Group;

    protected override string GetOwnerName() => AutomationText.Of(((GroupBox)Owner).Header);

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == HeaderedContentControl.HeaderProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.NameProperty, null, GetName());
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>
/// The peer of an <see cref="Image"/> or a <see cref="GlyphElement"/>. An image has no text of its own; it is named
/// through <see cref="AutomationProperties.NameProperty"/>.
/// </summary>
internal sealed class ImageAutomationPeer : UIElementAutomationPeer
{
    public ImageAutomationPeer(FrameworkElement owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Image;

    internal override bool IsLeaf => true;
}
