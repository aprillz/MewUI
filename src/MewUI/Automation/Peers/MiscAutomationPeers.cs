using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The peer of a <see cref="SegmentButton"/>: invoking it activates the segment as a click does.</summary>
internal sealed class SegmentButtonAutomationPeer : UIElementAutomationPeer, IInvokeProvider
{
    public SegmentButtonAutomationPeer(SegmentButton owner)
        : base(owner)
    {
    }

    private SegmentButton OwnerSegment => (SegmentButton)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Button;

    protected override string GetOwnerName() => AutomationText.Of(OwnerSegment.EffectiveContent);

    internal override bool IsLeaf => OwnerSegment.EffectiveContent is TextBlockBase or Label;

    public void Invoke()
    {
        AutomationGuard.ThrowIfDisabled(this);
        OwnerSegment.ActivateFromAutomation();
    }
}

/// <summary>The peer of a control that only groups others under a role (tool bar, calendar, popup).</summary>
internal sealed class GroupingAutomationPeer : UIElementAutomationPeer
{
    private readonly AutomationControlType _controlType;

    public GroupingAutomationPeer(UIElement owner, AutomationControlType controlType)
        : base(owner)
    {
        _controlType = controlType;
    }

    protected override AutomationControlType GetControlTypeCore() => _controlType;
}

/// <summary>The peer of a <see cref="ToolTip"/>, named by its text.</summary>
internal sealed class ToolTipAutomationPeer : UIElementAutomationPeer
{
    public ToolTipAutomationPeer(ToolTip owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.ToolTip;

    protected override string GetOwnerName() => AutomationText.Of(((ToolTip)Owner).EffectiveContent);

    internal override bool IsLeaf => ((ToolTip)Owner).EffectiveContent is TextBlockBase or Label;
}
