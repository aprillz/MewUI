using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Platform.MacOS.Automation;

/// <summary>
/// Serves each window's automation tree to NSAccessibility. The view holding a window's content lists the window's
/// top-level peers as its children; every peer is a native element of a runtime-registered NSAccessibilityElement
/// subclass whose methods read and operate the peer. Change notifications are posted as NSAccessibility
/// notifications.
/// </summary>
internal sealed unsafe class AxProvider : IAutomationEventSink
{
    private const string ELEMENT_CLASS_NAME = "MewUIAccessibilityElement";

    private static readonly ConditionalWeakTable<AutomationPeer, AxNode> _nodes = new();
    private static readonly ConditionalWeakTable<AutomationPeer, AxNode>.CreateValueCallback _createNode = CreateNode;
    // Native element to node; weak so a peer that is gone takes its node with it.
    private static readonly Dictionary<nint, WeakReference<AxNode>> _byHandle = new();
    private static nint _elementClass;
    private static bool _installed;

    private AxProvider()
    {
    }

    /// <summary>Registers the element class, the content view's accessibility methods, and the event sink, once.</summary>
    internal static void EnsureInstalled()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        nint viewClass = MacOSWindowInterop.EnsureContentViewClass();
        nint elementBase = ObjC.GetClass("NSAccessibilityElement");
        if (viewClass == 0 || elementBase == 0)
        {
            return;
        }

        _elementClass = ObjC.GetClass(ELEMENT_CLASS_NAME);
        if (_elementClass == 0)
        {
            _elementClass = ObjC.AllocateClassPair(elementBase, ELEMENT_CLASS_NAME);
            AddElementMethods(_elementClass);
            ObjC.RegisterClassPair(_elementClass);
        }

        AddViewMethods(viewClass);
        AutomationEvents.AddSink(new AxProvider());
    }

    internal static AxNode NodeOf(AutomationPeer peer) => _nodes.GetValue(peer, _createNode);

    private static AxNode CreateNode(AutomationPeer peer)
    {
        nint handle = ObjC.MsgSend_nint(ObjC.MsgSend_nint(_elementClass, ObjC.Sel("alloc")), ObjC.Sel("init"));
        var node = new AxNode(peer, handle);
        _byHandle[handle] = new WeakReference<AxNode>(node);
        return node;
    }

    private static AxNode? NodeForHandle(nint handle)
        => _byHandle.TryGetValue(handle, out var reference) && reference.TryGetTarget(out var node) ? node : null;

    /// <summary>Drops a collected node's native element; runs on the finalizer thread, so the release goes to the UI thread.</summary>
    internal static void ReleaseElement(nint handle)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        dispatcher?.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_byHandle.TryGetValue(handle, out var reference) && !reference.TryGetTarget(out _))
            {
                _byHandle.Remove(handle);
            }

            ObjC.MsgSend_void(handle, ObjC.Sel("release"));
        });
    }

    internal static nint ElementArray(IReadOnlyList<AutomationPeer> peers)
    {
        var handles = new List<nint>(peers.Count);
        foreach (var peer in peers)
        {
            handles.Add(NodeOf(peer).Handle);
        }

        return AxObjects.Array(handles);
    }

    /// <summary>Queues <paramref name="action"/> on the UI thread, ignoring a peer that stopped accepting it meanwhile.</summary>
    internal static void Post(Action action)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        dispatcher?.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                // The element changed (disabled, removed) after the reader was told the press was accepted.
            }
        });
    }

    /// <summary>The window whose content view is <paramref name="view"/>: a top-level window or a popup window.</summary>
    private static Window? WindowOfView(nint view)
    {
        foreach (var window in Application.Current.AllWindows)
        {
            if (window.Handle == view)
            {
                return window;
            }

            for (int index = 0; index < window.OpenPopupCount; index++)
            {
                if (window.OpenPopupAt(index).ResolveInputHostWindow() is Window surface && surface.Handle == view)
                {
                    return surface;
                }
            }
        }

        return null;
    }

    private static void AddElementMethods(nint cls)
    {
        Add(cls, "accessibilityRole", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementRole, "@@:");
        Add(cls, "accessibilitySubrole", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementSubrole, "@@:");
        Add(cls, "accessibilityLabel", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementLabel, "@@:");
        Add(cls, "accessibilityIdentifier", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementIdentifier, "@@:");
        Add(cls, "accessibilityHelp", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementHelp, "@@:");
        Add(cls, "accessibilityValue", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementValue, "@@:");
        Add(cls, "accessibilityMinValue", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementMinValue, "@@:");
        Add(cls, "accessibilityMaxValue", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementMaxValue, "@@:");
        Add(cls, "setAccessibilityValue:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ElementSetValue, "v@:@");
        Add(cls, "accessibilityParent", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementParent, "@@:");
        Add(cls, "accessibilityChildren", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementChildren, "@@:");
        Add(cls, "accessibilityWindow", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementTopLevel, "@@:");
        Add(cls, "accessibilityTopLevelUIElement", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementTopLevel, "@@:");
        Add(cls, "accessibilityFrame", (nint)(delegate* unmanaged<nint, nint, NSRect>)&ElementFrame, "{CGRect={CGPoint=dd}{CGSize=dd}}@:");
        Add(cls, "isAccessibilityElement", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementIsElement, "B@:");
        Add(cls, "isAccessibilityEnabled", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementIsEnabled, "B@:");
        Add(cls, "isAccessibilityFocused", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementIsFocused, "B@:");
        Add(cls, "setAccessibilityFocused:", (nint)(delegate* unmanaged<nint, nint, byte, void>)&ElementSetFocused, "v@:B");
        Add(cls, "isAccessibilitySelected", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementIsSelected, "B@:");
        Add(cls, "setAccessibilitySelected:", (nint)(delegate* unmanaged<nint, nint, byte, void>)&ElementSetSelected, "v@:B");
        Add(cls, "isAccessibilityExpanded", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementIsExpanded, "B@:");
        Add(cls, "setAccessibilityExpanded:", (nint)(delegate* unmanaged<nint, nint, byte, void>)&ElementSetExpanded, "v@:B");
        Add(cls, "accessibilitySelectedChildren", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementSelectedChildren, "@@:");
        Add(cls, "accessibilityPerformPress", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementPress, "B@:");
        Add(cls, "accessibilityPerformIncrement", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementIncrement, "B@:");
        Add(cls, "accessibilityPerformDecrement", (nint)(delegate* unmanaged<nint, nint, byte>)&ElementDecrement, "B@:");
        Add(cls, "isAccessibilitySelectorAllowed:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&ElementAllowsSelector, "B@::");
        Add(cls, "accessibilityNumberOfCharacters", (nint)(delegate* unmanaged<nint, nint, long>)&ElementNumberOfCharacters, "q@:");
        Add(cls, "accessibilitySelectedText", (nint)(delegate* unmanaged<nint, nint, nint>)&ElementSelectedText, "@@:");
        Add(cls, "accessibilitySelectedTextRange", (nint)(delegate* unmanaged<nint, nint, NSRange>)&ElementSelectedTextRange, "{_NSRange=QQ}@:");
        Add(cls, "setAccessibilitySelectedTextRange:", (nint)(delegate* unmanaged<nint, nint, NSRange, void>)&ElementSetSelectedTextRange, "v@:{_NSRange=QQ}");
        Add(cls, "accessibilityVisibleCharacterRange", (nint)(delegate* unmanaged<nint, nint, NSRange>)&ElementVisibleCharacterRange, "{_NSRange=QQ}@:");
        Add(cls, "accessibilityInsertionPointLineNumber", (nint)(delegate* unmanaged<nint, nint, long>)&ElementInsertionPointLineNumber, "q@:");
        Add(cls, "accessibilityStringForRange:", (nint)(delegate* unmanaged<nint, nint, NSRange, nint>)&ElementStringForRange, "@@:{_NSRange=QQ}");
        Add(cls, "accessibilityFrameForRange:", (nint)(delegate* unmanaged<nint, nint, NSRange, NSRect>)&ElementFrameForRange, "{CGRect={CGPoint=dd}{CGSize=dd}}@:{_NSRange=QQ}");
        Add(cls, "accessibilityRangeForPosition:", (nint)(delegate* unmanaged<nint, nint, NSPoint, NSRange>)&ElementRangeForPosition, "{_NSRange=QQ}@:{CGPoint=dd}");
        Add(cls, "accessibilityLineForIndex:", (nint)(delegate* unmanaged<nint, nint, long, long>)&ElementLineForIndex, "q@:q");
        Add(cls, "accessibilityRangeForLine:", (nint)(delegate* unmanaged<nint, nint, long, NSRange>)&ElementRangeForLine, "{_NSRange=QQ}@:q");
        Add(cls, "accessibilityRangeForIndex:", (nint)(delegate* unmanaged<nint, nint, long, NSRange>)&ElementRangeForIndex, "{_NSRange=QQ}@:q");
    }

    /// <summary>Reads a text value from the node of <paramref name="self"/>; the default when it is gone or has no text pattern.</summary>
    private static T ReadText<T>(nint self, Func<AxNode, T> read) where T : struct
    {
        try
        {
            return NodeForHandle(self) is AxNode node && node.Surface != null && node.Peer is ITextProvider ? read(node) : default;
        }
        catch
        {
            return default;
        }
    }

    [UnmanagedCallersOnly]
    private static long ElementNumberOfCharacters(nint self, nint selector) => ReadText(self, static node => node.NumberOfCharacters());

    [UnmanagedCallersOnly]
    private static nint ElementSelectedText(nint self, nint selector) => ReadText(self, static node => node.SelectedText());

    [UnmanagedCallersOnly]
    private static NSRange ElementSelectedTextRange(nint self, nint selector) => ReadText(self, static node => node.SelectedTextRange());

    [UnmanagedCallersOnly]
    private static void ElementSetSelectedTextRange(nint self, nint selector, NSRange range)
        => ReadText(self, node =>
        {
            node.SetSelectedTextRange(range);
            return 0;
        });

    [UnmanagedCallersOnly]
    private static NSRange ElementVisibleCharacterRange(nint self, nint selector) => ReadText(self, static node => node.VisibleCharacterRange());

    [UnmanagedCallersOnly]
    private static long ElementInsertionPointLineNumber(nint self, nint selector) => ReadText(self, static node => node.InsertionPointLineNumber());

    [UnmanagedCallersOnly]
    private static nint ElementStringForRange(nint self, nint selector, NSRange range) => ReadText(self, node => node.StringForRange(range));

    [UnmanagedCallersOnly]
    private static NSRect ElementFrameForRange(nint self, nint selector, NSRange range) => ReadText(self, node => node.FrameForRange(range));

    [UnmanagedCallersOnly]
    private static NSRange ElementRangeForPosition(nint self, nint selector, NSPoint point) => ReadText(self, node => node.RangeForPosition(point));

    [UnmanagedCallersOnly]
    private static long ElementLineForIndex(nint self, nint selector, long index) => ReadText(self, node => node.LineForIndex(index));

    [UnmanagedCallersOnly]
    private static NSRange ElementRangeForLine(nint self, nint selector, long line) => ReadText(self, node => node.RangeForLine(line));

    [UnmanagedCallersOnly]
    private static NSRange ElementRangeForIndex(nint self, nint selector, long index) => ReadText(self, node => node.RangeForIndex(index));

    private static void AddViewMethods(nint cls)
    {
        Add(cls, "isAccessibilityElement", (nint)(delegate* unmanaged<nint, nint, byte>)&ViewIsElement, "B@:");
        Add(cls, "accessibilityChildren", (nint)(delegate* unmanaged<nint, nint, nint>)&ViewChildren, "@@:");
        Add(cls, "accessibilityHitTest:", (nint)(delegate* unmanaged<nint, nint, NSPoint, nint>)&ViewHitTest, "@@:{CGPoint=dd}");
        Add(cls, "accessibilityFocusedUIElement", (nint)(delegate* unmanaged<nint, nint, nint>)&ViewFocusedElement, "@@:");
    }

    private static void Add(nint cls, string selector, nint implementation, string types)
        => _ = ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, types);

    private static nint Read(nint self, Func<AxNode, nint> read)
    {
        try
        {
            return NodeForHandle(self) is AxNode node && node.Surface != null ? read(node) : 0;
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
            return 0;
        }
    }

    private static byte Test(nint self, Func<AxNode, bool> test)
    {
        try
        {
            return NodeForHandle(self) is AxNode node && node.Surface != null && test(node) ? (byte)1 : (byte)0;
        }
        catch
        {
            return 0;
        }
    }

    private static void Act(nint self, Action<AxNode> action)
    {
        try
        {
            if (NodeForHandle(self) is AxNode node && node.Surface != null)
            {
                action(node);
            }
        }
        catch
        {
            // A disabled or read-only peer refuses; AppKit has no error to report it through.
        }
    }

    [UnmanagedCallersOnly]
    private static nint ElementRole(nint self, nint selector) => Read(self, static node => node.Role());

    [UnmanagedCallersOnly]
    private static nint ElementSubrole(nint self, nint selector) => Read(self, static node => node.Subrole());

    [UnmanagedCallersOnly]
    private static nint ElementLabel(nint self, nint selector) => Read(self, static node => node.Label());

    [UnmanagedCallersOnly]
    private static nint ElementIdentifier(nint self, nint selector) => Read(self, static node => node.Identifier());

    [UnmanagedCallersOnly]
    private static nint ElementHelp(nint self, nint selector) => Read(self, static node => node.Help());

    [UnmanagedCallersOnly]
    private static nint ElementValue(nint self, nint selector) => Read(self, static node => node.Value());

    [UnmanagedCallersOnly]
    private static nint ElementMinValue(nint self, nint selector) => Read(self, static node => node.MinValue());

    [UnmanagedCallersOnly]
    private static nint ElementMaxValue(nint self, nint selector) => Read(self, static node => node.MaxValue());

    [UnmanagedCallersOnly]
    private static void ElementSetValue(nint self, nint selector, nint value) => Act(self, node => node.SetValue(value));

    [UnmanagedCallersOnly]
    private static nint ElementParent(nint self, nint selector) => Read(self, static node => node.Parent());

    [UnmanagedCallersOnly]
    private static nint ElementChildren(nint self, nint selector) => Read(self, static node => node.Children());

    [UnmanagedCallersOnly]
    private static nint ElementTopLevel(nint self, nint selector) => Read(self, static node => node.TopLevel());

    [UnmanagedCallersOnly]
    private static NSRect ElementFrame(nint self, nint selector)
    {
        try
        {
            return NodeForHandle(self) is AxNode node && node.Surface != null ? node.Frame() : default;
        }
        catch
        {
            return default;
        }
    }

    [UnmanagedCallersOnly]
    private static byte ElementIsElement(nint self, nint selector) => 1;

    [UnmanagedCallersOnly]
    private static byte ElementIsEnabled(nint self, nint selector) => Test(self, static node => node.Peer.IsEnabled());

    [UnmanagedCallersOnly]
    private static byte ElementIsFocused(nint self, nint selector) => Test(self, static node => node.IsFocused());

    [UnmanagedCallersOnly]
    private static void ElementSetFocused(nint self, nint selector, byte focused) => Act(self, node => node.SetFocused(focused != 0));

    [UnmanagedCallersOnly]
    private static byte ElementIsSelected(nint self, nint selector) => Test(self, static node => node.IsSelected());

    [UnmanagedCallersOnly]
    private static void ElementSetSelected(nint self, nint selector, byte selected) => Act(self, node => node.SetSelected(selected != 0));

    [UnmanagedCallersOnly]
    private static byte ElementIsExpanded(nint self, nint selector) => Test(self, static node => node.IsExpanded());

    [UnmanagedCallersOnly]
    private static void ElementSetExpanded(nint self, nint selector, byte expanded) => Act(self, node => node.SetExpanded(expanded != 0));

    [UnmanagedCallersOnly]
    private static nint ElementSelectedChildren(nint self, nint selector) => Read(self, static node => node.SelectedChildren());

    [UnmanagedCallersOnly]
    private static byte ElementPress(nint self, nint selector) => Test(self, static node => node.Press());

    [UnmanagedCallersOnly]
    private static byte ElementIncrement(nint self, nint selector) => Test(self, static node => node.Step(increment: true));

    [UnmanagedCallersOnly]
    private static byte ElementDecrement(nint self, nint selector) => Test(self, static node => node.Step(increment: false));

    [UnmanagedCallersOnly]
    private static byte ElementAllowsSelector(nint self, nint selector, nint asked)
    {
        try
        {
            if (NodeForHandle(self) is not AxNode node)
            {
                return 0;
            }

            return node.AllowsSelector(ObjC.GetSelectorName(asked)) ? (byte)1 : (byte)0;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static byte ViewIsElement(nint self, nint selector) => 0;

    [UnmanagedCallersOnly]
    private static nint ViewChildren(nint self, nint selector)
    {
        try
        {
            if (WindowOfView(self)?.GetAutomationPeer() is AutomationPeer root)
            {
                return ElementArray(AutomationSurface.ChildrenOf(root));
            }

            return AxObjects.Array([]);
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static nint ViewHitTest(nint self, nint selector, NSPoint point)
    {
        try
        {
            if (WindowOfView(self)?.GetAutomationPeer() is AutomationPeer root)
            {
                var hit = AutomationSurface.HitTest(root, MacOSInterop.CocoaToScreenPixels(point));
                return ReferenceEquals(hit, root) ? self : NodeOf(hit).Handle;
            }

            return self;
        }
        catch
        {
            return self;
        }
    }

    [UnmanagedCallersOnly]
    private static nint ViewFocusedElement(nint self, nint selector)
    {
        try
        {
            if (WindowOfView(self) is Window window && AutomationSurface.FocusedIn(window) is AutomationPeer focused
                && focused is not WindowAutomationPeer)
            {
                return NodeOf(focused).Handle;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsShown(AutomationPeer peer) => AutomationSurface.Of(peer) is Window window && window.Handle != 0;

    public void OnFocusChanged(AutomationPeer peer)
    {
        if (IsShown(peer) && peer is not WindowAutomationPeer)
        {
            AxObjects.Post(NodeOf(peer).Handle, "AXFocusedUIElementChanged");
        }
    }

    public void OnStructureChanged(AutomationPeer peer)
    {
        if (IsShown(peer) && peer is not WindowAutomationPeer)
        {
            AxObjects.Post(NodeOf(peer).Handle, "AXLayoutChanged");
        }
    }

    public void OnEvent(AutomationPeer peer, AutomationEventKind kind)
    {
        if (!IsShown(peer))
        {
            return;
        }

        string? notification = kind switch
        {
            AutomationEventKind.MenuOpened => "AXMenuOpened",
            AutomationEventKind.MenuClosed => "AXMenuClosed",
            AutomationEventKind.TextChanged => "AXValueChanged",
            AutomationEventKind.TextSelectionChanged => "AXSelectedTextChanged",
            _ => null,
        };
        if (notification != null)
        {
            AxObjects.Post(NodeOf(peer).Handle, notification);
        }
    }

    public void OnPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        if (!IsShown(peer) || peer is WindowAutomationPeer)
        {
            return;
        }

        if (property == AutomationProperty.NameProperty)
        {
            AxObjects.Post(NodeOf(peer).Handle, "AXTitleChanged");
        }
        else if (property == AutomationProperty.ValueProperty || property == AutomationProperty.RangeValueProperty
            || property == AutomationProperty.ToggleStateProperty)
        {
            AxObjects.Post(NodeOf(peer).Handle, "AXValueChanged");
        }
        else if (property == AutomationProperty.IsSelectedProperty)
        {
            if (AxMapping.SelectionIsValue(peer))
            {
                AxObjects.Post(NodeOf(peer).Handle, "AXValueChanged");
            }

            if (peer is ISelectionItemProvider item && item.SelectionContainer is AutomationPeer container && IsShown(container))
            {
                AxObjects.Post(NodeOf(container).Handle, "AXSelectedChildrenChanged");
            }
        }
        else if (property == AutomationProperty.ExpandCollapseStateProperty && peer.GetControlType() == AutomationControlType.TreeItem)
        {
            bool expanded = peer is IExpandCollapseProvider expander && expander.ExpandCollapseState == ExpandCollapseState.Expanded;
            AxObjects.Post(NodeOf(peer).Handle, expanded ? "AXRowExpanded" : "AXRowCollapsed");
        }
    }
}
