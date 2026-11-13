using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform.Win32.Automation;

/// <summary>
/// The UI Automation provider of one automation peer. It answers every pattern interface, and reports through
/// <see cref="IRawElementProviderSimple.GetPatternProvider"/> only the patterns its peer supports. UI Automation calls
/// it from its own threads, so each member does its work on the UI thread.
/// </summary>
[GeneratedComClass]
internal partial class UiaNode :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IUiaInvokeProvider,
    IUiaToggleProvider,
    IUiaValueProvider,
    IUiaRangeValueProvider,
    IUiaSelectionProvider,
    IUiaSelectionItemProvider,
    IUiaExpandCollapseProvider,
    IUiaScrollProvider,
    IUiaScrollItemProvider,
    IUiaTextProvider
{
    private const string FRAMEWORK_ID = "MewUI";

    internal UiaNode(AutomationPeer peer, int id)
    {
        Peer = peer;
        Id = id;
    }

    internal AutomationPeer Peer { get; }

    /// <summary>The number that makes this node's runtime identifier unique in the process.</summary>
    internal int Id { get; }

    /// <summary>True for the node of a window, which UI Automation places through its native window.</summary>
    protected virtual bool IsRoot => false;

    /// <summary>Runs <paramref name="body"/> on the UI thread while the peer is still shown in a window.</summary>
    internal int Call(Func<int> body)
        => UiaThread.Run(() => AutomationSurface.Of(Peer) is Window window && window.Handle != 0
            ? body()
            : UiaNative.UIA_E_ELEMENTNOTAVAILABLE);

    /// <summary>Reads a value on the UI thread.</summary>
    internal int Read<T>(Func<T> getter, out T value)
    {
        T result = default!;
        int hresult = Call(() =>
        {
            result = getter();
            return UiaNative.S_OK;
        });
        value = result;
        return hresult;
    }

    /// <summary>Runs a pattern action on the UI thread, after checking the peer accepts input.</summary>
    private int Act(Action action) => Call(() =>
    {
        if (!Peer.IsEnabled())
        {
            return UiaNative.UIA_E_ELEMENTNOTENABLED;
        }

        action();
        return UiaNative.S_OK;
    });

    internal T Pattern<T>() where T : class
        => Peer as T ?? throw new InvalidOperationException($"The element does not support {typeof(T).Name}.");

    private static int Flag(bool value) => value ? 1 : 0;

    int IRawElementProviderSimple.GetProviderOptions(out int options)
    {
        options = UiaNative.PROVIDER_OPTIONS_SERVER_SIDE;
        return UiaNative.S_OK;
    }

    int IRawElementProviderSimple.GetPatternProvider(int patternId, out IRawElementProviderSimple? provider)
    {
        int hresult = Read(() => SupportsPattern(patternId), out bool supported);
        provider = supported ? this : null;
        return hresult;
    }

    int IRawElementProviderSimple.GetPropertyValue(int propertyId, out UiaVariant value)
        => Read(() => ReadProperty(propertyId), out value);

    int IRawElementProviderSimple.GetHostRawElementProvider(out nint provider) => GetHostProvider(out provider);

    /// <summary>The provider of the native window that hosts this node; none for a node below a window's.</summary>
    protected virtual int GetHostProvider(out nint provider)
    {
        provider = 0;
        return UiaNative.S_OK;
    }

    private bool SupportsPattern(int patternId) => patternId switch
    {
        UiaNative.UIA_INVOKE_PATTERN_ID => Peer is IInvokeProvider,
        UiaNative.UIA_TOGGLE_PATTERN_ID => Peer is IToggleProvider,
        UiaNative.UIA_VALUE_PATTERN_ID => Peer is IValueProvider,
        UiaNative.UIA_RANGE_VALUE_PATTERN_ID => Peer is IRangeValueProvider,
        UiaNative.UIA_SELECTION_PATTERN_ID => Peer is ISelectionProvider,
        UiaNative.UIA_SELECTION_ITEM_PATTERN_ID => Peer is ISelectionItemProvider,
        UiaNative.UIA_EXPAND_COLLAPSE_PATTERN_ID => Peer is IExpandCollapseProvider,
        UiaNative.UIA_SCROLL_PATTERN_ID => Peer is IScrollProvider,
        UiaNative.UIA_SCROLL_ITEM_PATTERN_ID => Peer is IScrollItemProvider,
        UiaNative.UIA_TEXT_PATTERN_ID => Peer is ITextProvider,
        _ => false,
    };

    private UiaVariant ReadProperty(int propertyId)
    {
        switch (propertyId)
        {
            case UiaNative.UIA_CONTROL_TYPE_PROPERTY_ID:
                return UiaVariant.Create(UiaMapping.ControlTypeId(Peer.GetControlType()));
            case UiaNative.UIA_NAME_PROPERTY_ID:
                return UiaVariant.Create(Peer.GetName());
            case UiaNative.UIA_AUTOMATION_ID_PROPERTY_ID:
                return UiaVariant.Create(Peer.GetAutomationId());
            case UiaNative.UIA_HELP_TEXT_PROPERTY_ID:
                return UiaVariant.Create(Peer.GetHelpText());
            case UiaNative.UIA_IS_ENABLED_PROPERTY_ID:
                return UiaVariant.Create(Peer.IsEnabled());
            case UiaNative.UIA_IS_OFFSCREEN_PROPERTY_ID:
                return UiaVariant.Create(Peer.IsOffscreen());
            case UiaNative.UIA_IS_KEYBOARD_FOCUSABLE_PROPERTY_ID:
                return UiaVariant.Create(Peer.IsKeyboardFocusable());
            case UiaNative.UIA_HAS_KEYBOARD_FOCUS_PROPERTY_ID:
                return UiaVariant.Create(Peer.HasKeyboardFocus());
            case UiaNative.UIA_FRAMEWORK_ID_PROPERTY_ID:
                return UiaVariant.Create(FRAMEWORK_ID);
            case UiaNative.UIA_IS_CONTROL_ELEMENT_PROPERTY_ID:
            case UiaNative.UIA_IS_CONTENT_ELEMENT_PROPERTY_ID:
                // Elements outside both views (raw) never get a node: the tree passes over them.
                return UiaVariant.Create(true);
            case UiaNative.UIA_IS_PASSWORD_PROPERTY_ID:
                return UiaVariant.Create(Peer is UIElementAutomationPeer elementPeer && elementPeer.Owner is PasswordBox);
            default:
                return default;
        }
    }

    int IRawElementProviderFragment.Navigate(int direction, out IRawElementProviderFragment? element)
    {
        int hresult = Read(() => NavigateFrom(direction), out var target);
        element = target;
        return hresult;
    }

    private UiaNode? NavigateFrom(int direction)
    {
        switch (direction)
        {
            case UiaNative.NAVIGATE_PARENT:
                return AutomationSurface.ParentOf(Peer) is AutomationPeer parent ? UiaProvider.NodeOf(parent) : null;
            case UiaNative.NAVIGATE_FIRST_CHILD:
            case UiaNative.NAVIGATE_LAST_CHILD:
            {
                var children = AutomationSurface.ChildrenOf(Peer);
                if (children.Count == 0)
                {
                    return null;
                }

                return UiaProvider.NodeOf(direction == UiaNative.NAVIGATE_FIRST_CHILD ? children[0] : children[^1]);
            }
            case UiaNative.NAVIGATE_NEXT_SIBLING:
            case UiaNative.NAVIGATE_PREVIOUS_SIBLING:
            {
                if (AutomationSurface.ParentOf(Peer) is not AutomationPeer siblingParent)
                {
                    return null;
                }

                var siblings = AutomationSurface.ChildrenOf(siblingParent);
                int index = IndexOf(siblings, Peer);
                int target = direction == UiaNative.NAVIGATE_NEXT_SIBLING ? index + 1 : index - 1;
                if (index < 0 || (uint)target >= (uint)siblings.Count)
                {
                    return null;
                }

                return UiaProvider.NodeOf(siblings[target]);
            }
            default:
                return null;
        }
    }

    private static int IndexOf(IReadOnlyList<AutomationPeer> peers, AutomationPeer peer)
    {
        for (int index = 0; index < peers.Count; index++)
        {
            if (ReferenceEquals(peers[index], peer))
            {
                return index;
            }
        }

        return -1;
    }

    int IRawElementProviderFragment.GetRuntimeId(out nint runtimeId)
    {
        // A window's node gets its runtime identifier from its native window.
        runtimeId = IsRoot ? 0 : UiaNative.CreateInt32Array([UiaNative.UIA_APPEND_RUNTIME_ID, Id]);
        return UiaNative.S_OK;
    }

    int IRawElementProviderFragment.GetBoundingRectangle(out UiaRect bounds)
    {
        int hresult = Read(() => AutomationSurface.ScreenBoundsOf(Peer), out var rect);
        bounds = new UiaRect { Left = rect.X, Top = rect.Y, Width = rect.Width, Height = rect.Height };
        return hresult;
    }

    int IRawElementProviderFragment.GetEmbeddedFragmentRoots(out nint roots)
    {
        roots = 0;
        return UiaNative.S_OK;
    }

    int IRawElementProviderFragment.SetFocus() => Call(() =>
    {
        Peer.SetFocus();
        return UiaNative.S_OK;
    });

    int IRawElementProviderFragment.GetFragmentRoot(out IRawElementProviderFragmentRoot? root)
    {
        int hresult = Read(() => AutomationSurface.Of(Peer)?.GetAutomationPeer() is AutomationPeer windowPeer
            ? UiaProvider.NodeOf(windowPeer) as UiaRootNode
            : null, out var rootNode);
        root = rootNode;
        return hresult;
    }

    int IUiaInvokeProvider.Invoke() => Call(() =>
    {
        var invoke = Pattern<IInvokeProvider>();
        if (!Peer.IsEnabled())
        {
            return UiaNative.UIA_E_ELEMENTNOTENABLED;
        }

        // Invoking must not wait for what the action starts, such as a dialog with its own loop.
        UiaThread.Post(invoke.Invoke);
        return UiaNative.S_OK;
    });

    int IUiaToggleProvider.Toggle() => Act(() => Pattern<IToggleProvider>().Toggle());

    int IUiaToggleProvider.GetToggleState(out int state)
        => Read(() => UiaMapping.ToggleStateValue(Pattern<IToggleProvider>().ToggleState), out state);

    int IUiaValueProvider.SetValue(nint value)
    {
        string text = Marshal.PtrToStringUni(value) ?? string.Empty;
        return Act(() => Pattern<IValueProvider>().SetValue(text));
    }

    int IUiaValueProvider.GetValue(out nint value)
    {
        int hresult = Read(() => Pattern<IValueProvider>().Value, out string? text);
        value = hresult == UiaNative.S_OK ? Marshal.StringToBSTR(text ?? string.Empty) : 0;
        return hresult;
    }

    int IUiaValueProvider.GetIsReadOnly(out int isReadOnly)
        => Read(() => Flag(Pattern<IValueProvider>().IsReadOnly), out isReadOnly);

    int IUiaRangeValueProvider.SetValue(double value) => Act(() => Pattern<IRangeValueProvider>().SetValue(value));

    int IUiaRangeValueProvider.GetValue(out double value) => Read(() => Pattern<IRangeValueProvider>().Value, out value);

    int IUiaRangeValueProvider.GetIsReadOnly(out int isReadOnly)
        => Read(() => Flag(Pattern<IRangeValueProvider>().IsReadOnly), out isReadOnly);

    int IUiaRangeValueProvider.GetMaximum(out double maximum) => Read(() => Pattern<IRangeValueProvider>().Maximum, out maximum);

    int IUiaRangeValueProvider.GetMinimum(out double minimum) => Read(() => Pattern<IRangeValueProvider>().Minimum, out minimum);

    int IUiaRangeValueProvider.GetLargeChange(out double change) => Read(() => Pattern<IRangeValueProvider>().LargeChange, out change);

    int IUiaRangeValueProvider.GetSmallChange(out double change) => Read(() => Pattern<IRangeValueProvider>().SmallChange, out change);

    int IUiaSelectionProvider.GetSelection(out nint selection)
    {
        int hresult = Read(() =>
        {
            var nodes = new List<IRawElementProviderSimple>();
            foreach (var selected in Pattern<ISelectionProvider>().GetSelection())
            {
                nodes.Add(UiaProvider.NodeOf(selected));
            }

            return nodes;
        }, out var selectedNodes);
        selection = hresult == UiaNative.S_OK ? UiaNative.CreateInterfaceArray(selectedNodes) : 0;
        return hresult;
    }

    int IUiaSelectionProvider.GetCanSelectMultiple(out int canSelectMultiple)
        => Read(() => Flag(Pattern<ISelectionProvider>().CanSelectMultiple), out canSelectMultiple);

    int IUiaSelectionProvider.GetIsSelectionRequired(out int isSelectionRequired)
        => Read(() => Flag(Pattern<ISelectionProvider>().IsSelectionRequired), out isSelectionRequired);

    int IUiaSelectionItemProvider.Select() => Act(() => Pattern<ISelectionItemProvider>().Select());

    int IUiaSelectionItemProvider.AddToSelection() => Act(() => Pattern<ISelectionItemProvider>().AddToSelection());

    int IUiaSelectionItemProvider.RemoveFromSelection() => Act(() => Pattern<ISelectionItemProvider>().RemoveFromSelection());

    int IUiaSelectionItemProvider.GetIsSelected(out int isSelected)
        => Read(() => Flag(Pattern<ISelectionItemProvider>().IsSelected), out isSelected);

    int IUiaSelectionItemProvider.GetSelectionContainer(out IRawElementProviderSimple? container)
    {
        int hresult = Read(() => Pattern<ISelectionItemProvider>().SelectionContainer is AutomationPeer containerPeer
            ? UiaProvider.NodeOf(containerPeer)
            : null, out var containerNode);
        container = containerNode;
        return hresult;
    }

    int IUiaExpandCollapseProvider.Expand() => Act(() => Pattern<IExpandCollapseProvider>().Expand());

    int IUiaExpandCollapseProvider.Collapse() => Act(() => Pattern<IExpandCollapseProvider>().Collapse());

    int IUiaExpandCollapseProvider.GetExpandCollapseState(out int state)
        => Read(() => UiaMapping.ExpandCollapseStateValue(Pattern<IExpandCollapseProvider>().ExpandCollapseState), out state);

    int IUiaScrollProvider.Scroll(int horizontalAmount, int verticalAmount)
        => Act(() => Pattern<IScrollProvider>().Scroll(UiaMapping.ScrollAmountOf(horizontalAmount), UiaMapping.ScrollAmountOf(verticalAmount)));

    int IUiaScrollProvider.SetScrollPercent(double horizontalPercent, double verticalPercent)
        => Act(() => Pattern<IScrollProvider>().SetScrollPercent(horizontalPercent, verticalPercent));

    int IUiaScrollProvider.GetHorizontalScrollPercent(out double percent)
        => Read(() => Pattern<IScrollProvider>().HorizontalScrollPercent, out percent);

    int IUiaScrollProvider.GetVerticalScrollPercent(out double percent)
        => Read(() => Pattern<IScrollProvider>().VerticalScrollPercent, out percent);

    int IUiaScrollProvider.GetHorizontalViewSize(out double size)
        => Read(() => Pattern<IScrollProvider>().HorizontalViewSize, out size);

    int IUiaScrollProvider.GetVerticalViewSize(out double size)
        => Read(() => Pattern<IScrollProvider>().VerticalViewSize, out size);

    int IUiaScrollProvider.GetHorizontallyScrollable(out int scrollable)
        => Read(() => Flag(Pattern<IScrollProvider>().HorizontallyScrollable), out scrollable);

    int IUiaScrollProvider.GetVerticallyScrollable(out int scrollable)
        => Read(() => Flag(Pattern<IScrollProvider>().VerticallyScrollable), out scrollable);

    int IUiaScrollItemProvider.ScrollIntoView() => Act(() => Pattern<IScrollItemProvider>().ScrollIntoView());

    int IUiaTextProvider.GetSelection(out nint ranges)
    {
        int hresult = Read(() =>
        {
            var text = Pattern<ITextProvider>();
            var selected = new List<IUiaTextRangeProvider>();
            foreach (var range in text.GetSelection())
            {
                selected.Add(new UiaTextRange(this, range.Start, range.Start + range.Length));
            }

            // With nothing selected, the selection is the caret: an empty range where it stands.
            if (selected.Count == 0 && text.CaretOffset >= 0)
            {
                selected.Add(new UiaTextRange(this, text.CaretOffset, text.CaretOffset));
            }

            return selected;
        }, out var selection);
        ranges = hresult == UiaNative.S_OK ? UiaNative.CreateInterfaceArray(selection) : 0;
        return hresult;
    }

    int IUiaTextProvider.GetVisibleRanges(out nint ranges)
    {
        int hresult = Read(() => new List<IUiaTextRangeProvider> { DocumentRange() }, out var visible);
        ranges = hresult == UiaNative.S_OK ? UiaNative.CreateInterfaceArray(visible) : 0;
        return hresult;
    }

    int IUiaTextProvider.RangeFromChild(nint childElement, out IUiaTextRangeProvider? range)
    {
        // The text holds no embedded elements.
        range = null;
        return UiaNative.E_INVALIDARG;
    }

    int IUiaTextProvider.RangeFromPoint(UiaPoint point, out IUiaTextRangeProvider? range)
    {
        int hresult = Read(() =>
        {
            int offset = Pattern<ITextProvider>().GetOffsetAt(AutomationSurface.ScreenToClient(Peer, new Point(point.X, point.Y)));
            return new UiaTextRange(this, offset, offset);
        }, out var found);
        range = found;
        return hresult;
    }

    int IUiaTextProvider.GetDocumentRange(out IUiaTextRangeProvider? range)
    {
        int hresult = Read(DocumentRange, out var document);
        range = document;
        return hresult;
    }

    int IUiaTextProvider.GetSupportedTextSelection(out int selection)
        => Read(() => Pattern<ITextProvider>().CaretOffset >= 0 ? 1 : 0, out selection);

    private UiaTextRange DocumentRange() => new(this, 0, Pattern<ITextProvider>().Text.Length);
}
