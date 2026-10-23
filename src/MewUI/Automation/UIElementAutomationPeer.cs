using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a <see cref="UIElement"/>: reads its state, places it under the nearest ancestor that has a peer,
/// and presents the children of descendants without a peer (panels, decorators) as its own.
/// </summary>
public class UIElementAutomationPeer : AutomationPeer
{
    public UIElementAutomationPeer(UIElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
    }

    public UIElement Owner { get; }

    /// <summary>The peer of <paramref name="element"/>, or null when automation is off or the element has none.</summary>
    public static AutomationPeer? FromElement(UIElement element) => element.GetAutomationPeer();

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Custom;

    protected override string GetNameCore()
    {
        string name = AutomationProperties.GetName(Owner);
        if (name.Length > 0)
        {
            return name;
        }

        var label = AutomationProperties.GetLabeledBy(Owner);
        if (label != null)
        {
            name = AutomationText.Of(label);
            if (name.Length > 0)
            {
                return name;
            }
        }

        return GetOwnerName();
    }

    /// <summary>The name the control derives from its own content when none is set explicitly.</summary>
    protected virtual string GetOwnerName() => string.Empty;

    protected override string GetAutomationIdCore() => AutomationProperties.GetAutomationId(Owner);

    protected override string GetHelpTextCore() => AutomationProperties.GetHelpText(Owner);

    protected override bool IsEnabledCore() => Owner.IsEffectivelyEnabled;

    protected override bool IsOffscreenCore()
    {
        if (Owner.RenderSize.Width <= 0 || Owner.RenderSize.Height <= 0)
        {
            return true;
        }

        for (Element? current = Owner; current != null; current = current.Parent)
        {
            if (current is UIElement element && !element.IsVisible)
            {
                return true;
            }
        }

        return false;
    }

    protected override bool IsKeyboardFocusableCore() => Owner.Focusable && Owner.IsEffectivelyEnabled;

    protected override bool HasKeyboardFocusCore() => Owner.IsFocused;

    protected override Rect GetBoundingRectangleCore()
    {
        if (Owner.FindVisualRoot() is not Window window)
        {
            return Rect.Empty;
        }

        var size = Owner.RenderSize;
        return Owner.TranslateRect(new Rect(0, 0, size.Width, size.Height), window);
    }

    protected override Window? GetWindowCore() => Owner.FindVisualRoot() as Window;

    protected override AutomationPeer? GetParentCore()
    {
        for (Element? ancestor = Owner.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is UIElement element && IsPresented(element, out var peer))
            {
                return peer;
            }
        }

        return null;
    }

    /// <summary>
    /// True for a control whose name, value, or drawn parts already say what its content shows. No element below
    /// it gets a peer, and its children are only the parts it presents itself (none by default).
    /// </summary>
    internal virtual bool IsLeaf => false;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        if (IsLeaf)
        {
            return [];
        }

        var children = new List<AutomationPeer>();
        CollectChildren(Owner, children);
        return children;
    }

    protected override void SetFocusCore() => Owner.Focus();

    /// <summary>Called when a property of the owner changes; a control's peer announces the changes it presents.</summary>
    protected virtual void OnOwnerPropertyChanged(MewProperty property)
    {
        if (property == UIElement.IsFocusedProperty)
        {
            if (Owner.IsFocused)
            {
                RaiseFocusChangedEvent();
            }

            RaisePropertyChangedEvent(AutomationProperty.HasKeyboardFocusProperty, !Owner.IsFocused, Owner.IsFocused);
        }
        else if (property == UIElement.IsEffectivelyEnabledProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsEnabledProperty, !Owner.IsEffectivelyEnabled, Owner.IsEffectivelyEnabled);
        }
        else if (property == UIElement.IsVisibleProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.IsOffscreenProperty, Owner.IsVisible, !Owner.IsVisible);
            GetParentCore()?.RaiseStructureChangedFromTree();
        }
        else if (property == AutomationProperties.NameProperty || property == ContentControl.EffectiveContentProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.NameProperty, null, GetName());
        }
        else if (property == AutomationProperties.HelpTextProperty)
        {
            RaisePropertyChangedEvent(AutomationProperty.HelpTextProperty, null, GetHelpText());
        }
    }

    internal override void NotifyOwnerPropertyChanged(MewProperty property) => OnOwnerPropertyChanged(property);

    /// <summary>Announces that a recycled container now shows another item: its name and its parent's children changed.</summary>
    private protected void RaiseRebound()
    {
        RaisePropertyChangedEvent(AutomationProperty.NameProperty, null, GetName());
        GetParent()?.RaiseStructureChangedFromTree();
    }

    /// <summary>
    /// Adds the peers below <paramref name="element"/> in reading order: a child with a peer is added, and a child
    /// without one (or left out by <see cref="AccessibilityView.Raw"/>) is passed through to its own children.
    /// </summary>
    internal static void CollectChildren(Element element, List<AutomationPeer> children)
    {
        if (element is not IVisualTreeHost host)
        {
            return;
        }

        host.VisitChildren(child =>
        {
            // A host can list a child it lent to another parent (a tool bar's overflow); the child belongs there.
            if (!ReferenceEquals(child.Parent, element))
            {
                return true;
            }

            if (child is UIElement childElement)
            {
                if (!childElement.IsVisible)
                {
                    return true;
                }

                if (IsPresented(childElement, out var peer))
                {
                    children.Add(peer);
                    return true;
                }
            }

            CollectChildren(child, children);
            return true;
        });
    }

    private static bool IsPresented(UIElement element, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AutomationPeer? peer)
    {
        peer = element.GetAutomationPeer();
        if (peer != null && AutomationProperties.GetAccessibilityView(element) == AccessibilityView.Raw)
        {
            peer = null;
        }

        return peer != null;
    }
}
