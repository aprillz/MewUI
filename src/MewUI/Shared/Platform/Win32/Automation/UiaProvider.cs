using System.Runtime.CompilerServices;

using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Platform.Win32.Automation;

/// <summary>
/// Serves a window's automation tree to UI Automation: answers the native window's request for its root provider,
/// keeps one provider per peer, and passes the tree's change notifications on as UI Automation events. Nothing is
/// set up until a client first asks a window for its tree.
/// </summary>
internal sealed unsafe class UiaProvider : IAutomationEventSink
{
    private static readonly ConditionalWeakTable<AutomationPeer, UiaNode> _nodes = new();
    private static readonly ConditionalWeakTable<AutomationPeer, UiaNode>.CreateValueCallback _createNode = CreateNode;
    private static int _lastId;
    private static UiaProvider? _sink;

    private UiaProvider()
    {
    }

    /// <summary>The provider of <paramref name="peer"/>, created on first use.</summary>
    internal static UiaNode NodeOf(AutomationPeer peer) => _nodes.GetValue(peer, _createNode);

    private static UiaNode CreateNode(AutomationPeer peer)
    {
        int id = Interlocked.Increment(ref _lastId);
        if (peer is WindowAutomationPeer)
        {
            return new UiaRootNode(peer, id);
        }
        else
        {
            return new UiaNode(peer, id);
        }
    }

    /// <summary>
    /// Answers a window's object request when UI Automation asks for its root provider; false for any other request,
    /// which the window passes on.
    /// </summary>
    internal static bool TryAnswerGetObject(Window window, nint hwnd, nint wParam, nint lParam, out nint result)
    {
        // The object identifier is a 32-bit value in the low half of lParam.
        if ((int)lParam != UiaNative.UIA_ROOT_OBJECT_ID || window.GetAutomationPeer() is not AutomationPeer peer)
        {
            result = 0;
            return false;
        }

        if (_sink == null)
        {
            _sink = new UiaProvider();
            AutomationEvents.AddSink(_sink);
        }

        result = UiaNative.UiaReturnRawElementProvider(hwnd, wParam, lParam, NodeOf(peer));
        return true;
    }

    /// <summary>Tells UI Automation a window it served is going away, so clients drop its providers.</summary>
    internal static void OnWindowDestroying(Window window, nint hwnd)
    {
        if (_sink == null)
        {
            return;
        }

        _ = UiaNative.UiaReturnRawElementProvider(hwnd, 0, 0, null);
        if (window.GetAutomationPeer() is AutomationPeer peer && _nodes.TryGetValue(peer, out var node))
        {
            _ = UiaNative.UiaDisconnectProvider(node);
        }
    }

    private static bool IsListening(AutomationPeer peer)
        => UiaNative.UiaClientsAreListening() != 0 && AutomationSurface.Of(peer) is Window window && window.Handle != 0;

    public void OnFocusChanged(AutomationPeer peer)
    {
        if (IsListening(peer))
        {
            _ = UiaNative.UiaRaiseAutomationEvent(NodeOf(peer), UiaNative.UIA_AUTOMATION_FOCUS_CHANGED_EVENT_ID);
        }
    }

    public void OnStructureChanged(AutomationPeer peer)
    {
        if (!IsListening(peer))
        {
            return;
        }

        var node = NodeOf(peer);
        int* runtimeId = stackalloc int[] { UiaNative.UIA_APPEND_RUNTIME_ID, node.Id };
        _ = UiaNative.UiaRaiseStructureChangedEvent(node, UiaNative.STRUCTURE_CHILDREN_INVALIDATED, runtimeId, 2);
    }

    public void OnEvent(AutomationPeer peer, AutomationEventKind kind)
    {
        if (IsListening(peer))
        {
            _ = UiaNative.UiaRaiseAutomationEvent(NodeOf(peer), UiaMapping.EventId(kind));
        }
    }

    public void OnPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        if (!IsListening(peer))
        {
            return;
        }

        var node = NodeOf(peer);
        if (property == AutomationProperty.ScrollPercentProperty)
        {
            if (peer is IScrollProvider scroll)
            {
                RaiseProperty(node, UiaNative.UIA_SCROLL_HORIZONTAL_SCROLL_PERCENT_PROPERTY_ID, null, scroll.HorizontalScrollPercent);
                RaiseProperty(node, UiaNative.UIA_SCROLL_VERTICAL_SCROLL_PERCENT_PROPERTY_ID, null, scroll.VerticalScrollPercent);
            }

            return;
        }

        int propertyId = UiaMapping.PropertyId(peer, property);
        if (propertyId != 0)
        {
            RaiseProperty(node, propertyId, oldValue, newValue);
        }

        if (property == AutomationProperty.IsSelectedProperty)
        {
            RaiseSelectionItemEvent(node, peer, newValue is true);
        }
    }

    private static void RaiseProperty(UiaNode node, int propertyId, object? oldValue, object? newValue)
    {
        using var oldVariant = UiaMapping.VariantOf(oldValue);
        using var newVariant = UiaMapping.VariantOf(newValue);
        _ = UiaNative.UiaRaiseAutomationPropertyChangedEvent(node, propertyId, oldVariant, newVariant);
    }

    private static void RaiseSelectionItemEvent(UiaNode node, AutomationPeer peer, bool selected)
    {
        bool multiple = peer is ISelectionItemProvider item
            && item.SelectionContainer is ISelectionProvider container
            && container.CanSelectMultiple;
        if (multiple)
        {
            int eventId = selected
                ? UiaNative.UIA_ELEMENT_ADDED_TO_SELECTION_EVENT_ID
                : UiaNative.UIA_ELEMENT_REMOVED_FROM_SELECTION_EVENT_ID;
            _ = UiaNative.UiaRaiseAutomationEvent(node, eventId);
        }
        else if (selected)
        {
            _ = UiaNative.UiaRaiseAutomationEvent(node, UiaNative.UIA_ELEMENT_SELECTED_EVENT_ID);
        }
    }
}
