using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a part a control draws itself (a menu entry, a calendar day) rather than an element. Its parent is the
/// control's peer, and its state defaults to the control's.
/// </summary>
internal abstract class PartAutomationPeer : AutomationPeer
{
    protected PartAutomationPeer(UIElementAutomationPeer hostPeer)
    {
        HostPeer = hostPeer;
    }

    internal UIElementAutomationPeer HostPeer { get; }

    protected UIElement Host => HostPeer.Owner;

    /// <summary>The part's rectangle in the host's bounds space, or false when it is not laid out.</summary>
    protected abstract bool TryGetBoundsInHost(out Rect bounds);

    protected override string GetAutomationIdCore() => string.Empty;

    protected override string GetHelpTextCore() => string.Empty;

    protected override bool IsEnabledCore() => HostPeer.IsEnabled();

    protected override bool IsOffscreenCore()
        => HostPeer.IsOffscreen() || !TryGetBoundsInHost(out var bounds) || bounds.Width <= 0 || bounds.Height <= 0;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override bool HasKeyboardFocusCore() => false;

    protected override Rect GetBoundingRectangleCore()
        => TryGetBoundsInHost(out var bounds) ? bounds : Rect.Empty;

    protected override Window? GetWindowCore() => HostPeer.GetWindow();

    protected override AutomationPeer? GetParentCore() => HostPeer;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => [];

    protected override void SetFocusCore() => HostPeer.SetFocus();
}
