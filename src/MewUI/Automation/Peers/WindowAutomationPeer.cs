using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>The root of a window's automation tree: its content, then its open popups in the order they opened.</summary>
internal sealed class WindowAutomationPeer : UIElementAutomationPeer
{
    public WindowAutomationPeer(Window owner)
        : base(owner)
    {
    }

    private Window OwnerWindow => (Window)Owner;

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Window;

    protected override string GetOwnerName() => OwnerWindow.Title;

    protected override bool IsOffscreenCore() => !OwnerWindow.IsVisible;

    protected override Rect GetBoundingRectangleCore()
    {
        var size = OwnerWindow.ClientSize;
        return new Rect(0, 0, size.Width, size.Height);
    }

    protected override Window? GetWindowCore() => OwnerWindow;

    protected override AutomationPeer? GetParentCore() => null;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>(base.GetChildrenCore());
        for (int index = 0; index < OwnerWindow.OpenPopupCount; index++)
        {
            var popup = OwnerWindow.OpenPopupAt(index);
            if (!popup.IsVisible)
            {
                continue;
            }

            var peer = popup.GetAutomationPeer();
            if (peer != null && AutomationProperties.GetAccessibilityView(popup) != AccessibilityView.Raw)
            {
                children.Add(peer);
            }
            else
            {
                CollectChildren(popup, children);
            }
        }

        return children;
    }

    protected override void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == Window.TitleProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.NameProperty, null, GetName());
        }

        base.OnOwnerPropertyChanged(property);
    }
}
