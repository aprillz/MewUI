using System.Runtime.InteropServices.Marshalling;

using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Platform.Win32.Automation;

/// <summary>
/// The provider of a window: the root of the tree its native window shows. A popup window is a root of its own,
/// holding the popup content it shows for its owner.
/// </summary>
[GeneratedComClass]
internal sealed partial class UiaRootNode : UiaNode, IRawElementProviderFragmentRoot
{
    internal UiaRootNode(AutomationPeer peer, int id)
        : base(peer, id)
    {
    }

    private Window Window => (Window)((UIElementAutomationPeer)Peer).Owner;

    protected override bool IsRoot => true;

    protected override int GetHostProvider(out nint provider)
    {
        int hresult = Read(() => Window.Handle, out nint handle);
        if (hresult != UiaNative.S_OK || handle == 0)
        {
            provider = 0;
            return hresult == UiaNative.S_OK ? UiaNative.UIA_E_ELEMENTNOTAVAILABLE : hresult;
        }

        return UiaNative.UiaHostProviderFromHwnd(handle, out provider);
    }

    int IRawElementProviderFragmentRoot.ElementProviderFromPoint(double x, double y, out IRawElementProviderFragment? element)
    {
        int hresult = Read(() => UiaProvider.NodeOf(AutomationSurface.HitTest(Peer, new Point(x, y))), out var hit);
        element = hit;
        return hresult;
    }

    int IRawElementProviderFragmentRoot.GetFocus(out IRawElementProviderFragment? element)
    {
        int hresult = Read(() => AutomationSurface.FocusedIn(Window) is AutomationPeer focused && !ReferenceEquals(focused, Peer)
            ? UiaProvider.NodeOf(focused)
            : null, out var focusedNode);
        element = focusedNode;
        return hresult;
    }
}
