using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a <see cref="ScrollViewer"/>: a pane that scrolls. Its scroll bars are not presented; the scroll
/// pattern covers what they do.
/// </summary>
internal sealed class ScrollViewerAutomationPeer : UIElementAutomationPeer, IScrollProvider
{
    public ScrollViewerAutomationPeer(ScrollViewer owner)
        : base(owner)
    {
    }

    private ScrollViewer OwnerViewer => (ScrollViewer)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Pane;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => ScrollViewerScroll.ContentChildren(OwnerViewer);

    public bool HorizontallyScrollable => ScrollViewerScroll.CanScroll(OwnerViewer, horizontal: true);

    public bool VerticallyScrollable => ScrollViewerScroll.CanScroll(OwnerViewer, horizontal: false);

    public double HorizontalScrollPercent => ScrollViewerScroll.Percent(OwnerViewer, horizontal: true);

    public double VerticalScrollPercent => ScrollViewerScroll.Percent(OwnerViewer, horizontal: false);

    public double HorizontalViewSize => ScrollViewerScroll.ViewSize(OwnerViewer, horizontal: true);

    public double VerticalViewSize => ScrollViewerScroll.ViewSize(OwnerViewer, horizontal: false);

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
        => ScrollViewerScroll.Scroll(this, OwnerViewer, horizontalAmount, verticalAmount);

    public void SetScrollPercent(double horizontalPercent, double verticalPercent)
        => ScrollViewerScroll.SetPercent(this, OwnerViewer, horizontalPercent, verticalPercent);

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == ScrollViewer.VerticalOffsetProperty || property == ScrollViewer.HorizontalOffsetProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.ScrollPercentProperty, null, null);
        }

        base.OnOwnerPropertyChanged(property);
    }
}

/// <summary>The scroll pattern over a <see cref="ScrollViewer"/>, shared by the viewer and the item controls that host one.</summary>
internal static class ScrollViewerScroll
{
    internal static IReadOnlyList<AutomationPeer> ContentChildren(ScrollViewer viewer)
    {
        var children = new List<AutomationPeer>();
        if (viewer.Content is UIElement content && content.IsVisible)
        {
            var peer = content.GetAutomationPeer();
            if (peer != null && AutomationProperties.GetAccessibilityView(content) != AccessibilityView.Raw)
            {
                children.Add(peer);
            }
            else
            {
                UIElementAutomationPeer.CollectChildren(content, children);
            }
        }

        return children;
    }

    internal static bool CanScroll(ScrollViewer viewer, bool horizontal)
    {
        var mode = horizontal ? viewer.HorizontalScroll : viewer.VerticalScroll;
        return mode != ScrollMode.Disabled && Range(viewer, horizontal) > 0;
    }

    internal static double Percent(ScrollViewer viewer, bool horizontal)
    {
        if (!CanScroll(viewer, horizontal))
        {
            return IScrollProvider.NoScroll;
        }

        double offset = horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset;
        return Math.Clamp(offset / Range(viewer, horizontal) * 100, 0, 100);
    }

    internal static double ViewSize(ScrollViewer viewer, bool horizontal)
    {
        double extent = horizontal ? viewer.Extent.Width : viewer.Extent.Height;
        double viewport = horizontal ? viewer.ViewportWidth : viewer.ViewportHeight;
        if (extent <= 0)
        {
            return 100;
        }

        return Math.Clamp(viewport / extent * 100, 0, 100);
    }

    internal static void Scroll(AutomationPeer peer, ScrollViewer viewer, ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        AutomationGuard.ThrowIfDisabled(peer);
        double horizontal = Step(viewer, horizontal: true, horizontalAmount);
        double vertical = Step(viewer, horizontal: false, verticalAmount);
        viewer.SetScrollOffsets(viewer.HorizontalOffset + horizontal, viewer.VerticalOffset + vertical);
    }

    internal static void SetPercent(AutomationPeer peer, ScrollViewer viewer, double horizontalPercent, double verticalPercent)
    {
        AutomationGuard.ThrowIfDisabled(peer);
        double horizontal = Target(viewer, horizontal: true, horizontalPercent, viewer.HorizontalOffset);
        double vertical = Target(viewer, horizontal: false, verticalPercent, viewer.VerticalOffset);
        viewer.SetScrollOffsets(horizontal, vertical);
    }

    private static double Range(ScrollViewer viewer, bool horizontal)
    {
        double extent = horizontal ? viewer.Extent.Width : viewer.Extent.Height;
        double viewport = horizontal ? viewer.ViewportWidth : viewer.ViewportHeight;
        return Math.Max(0, extent - viewport);
    }

    private static double Step(ScrollViewer viewer, bool horizontal, ScrollAmount amount)
    {
        if (amount == ScrollAmount.NoAmount)
        {
            return 0;
        }

        if (!CanScroll(viewer, horizontal))
        {
            throw new InvalidOperationException("The content does not scroll in that direction.");
        }

        double viewport = horizontal ? viewer.ViewportWidth : viewer.ViewportHeight;
        double small = viewer.ThemeInternal.Metrics.ScrollWheelStep;
        return amount switch
        {
            ScrollAmount.LargeDecrement => -viewport,
            ScrollAmount.SmallDecrement => -small,
            ScrollAmount.LargeIncrement => viewport,
            _ => small,
        };
    }

    private static double Target(ScrollViewer viewer, bool horizontal, double percent, double current)
    {
        if (percent == IScrollProvider.NoScroll)
        {
            return current;
        }

        if (percent < 0 || percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "A scroll percent lies between 0 and 100.");
        }

        if (!CanScroll(viewer, horizontal))
        {
            throw new InvalidOperationException("The content does not scroll in that direction.");
        }

        return Range(viewer, horizontal) * percent / 100;
    }
}
