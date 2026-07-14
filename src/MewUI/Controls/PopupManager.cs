using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Per-window popup policy layer: owns the open-popup list, close policy, owner
/// notification, focus restoration, and tooltip caching, and delegates the actual
/// visual hosting to an <see cref="IPopupHost"/>.
/// </summary>
internal sealed class PopupManager
{
    /// <summary>
    /// Host popups in their own OS windows (native) instead of the owner surface. Internal, not a public
    /// policy surface (see agent/popup-native-window/plan.md). Headless tests set this false to keep the
    /// in-surface path as their baseline.
    /// </summary>
    internal static bool PreferNativePopups = true;

    private readonly Window _window;
    private readonly List<PopupEntry> _popups = new();
    private readonly InSurfacePopupHost _inSurfaceHost;
    private readonly NativePopupHost _nativeHost;

    private ToolTip? _toolTip;
    private UIElement? _toolTipOwner;

    private bool _isClosingPopups;

    public PopupManager(Window window)
    {
        _window = window;
        _inSurfaceHost = new InSurfacePopupHost(window, _popups);
        _nativeHost = new NativePopupHost(window, _popups);
    }

    private IPopupHost ResolveHost() => PreferNativePopups ? _nativeHost : _inSurfaceHost;

    internal int Count => _popups.Count;

    internal UIElement ElementAt(int index) => _popups[index].Element;

    internal bool HasAny => _popups.Count > 0;

    internal bool HasLayoutDirty() => _inSurfaceHost.HasLayoutDirty() || _nativeHost.HasLayoutDirty();

    internal void LayoutDirtyPopups()
    {
        _inSurfaceHost.LayoutDirty();
        _nativeHost.LayoutDirty();
    }

    // Native popups draw into their own windows, so only the in-surface host paints/hit-tests the owner surface.
    internal void Render(IGraphicsContext context) => _inSurfaceHost.Render(context);

    internal UIElement? HitTest(Point point) => _inSurfaceHost.HitTest(point);

    internal void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        _inSurfaceHost.NotifyThemeChanged(oldTheme, newTheme);
        _nativeHost.NotifyThemeChanged(oldTheme, newTheme);
    }

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        _inSurfaceHost.NotifyDpiChanged(oldDpi, newDpi);
        _nativeHost.NotifyDpiChanged(oldDpi, newDpi);
    }

    internal void Dispose()
    {
        foreach (var entry in _popups)
        {
            if (entry.Element is IDisposable disposable)
            {
                disposable.Dispose();
            }

            entry.Host?.Detach(entry);
        }

        _popups.Clear();
        _toolTipOwner = null;
        _toolTip = null;
    }

    internal void CloseAllPopups()
    {
        if (_isClosingPopups)
        {
            return;
        }

        // Guard against re-entrant closes: detaching a popup can clear focus, which may request another
        // close sweep. The guard keeps that sweep from mutating _popups while this loop walks it by index.
        _isClosingPopups = true;
        try
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                CloseAndDetachEntry(i, PopupCloseKind.Lifecycle);
            }
        }
        finally
        {
            _isClosingPopups = false;
        }

        _window.Invalidate();
    }

    internal Rect ShowPopup(UIElement owner, UIElement popup, Rect bounds, bool sizeToContent = false, bool staysOpen = false)
        => ShowPopup(owner, popup, _ => bounds, sizeToContent, staysOpen);

    /// <summary>
    /// Opens <paramref name="popup"/>, computing its placement via <paramref name="measureBounds"/> only
    /// after the popup is rooted in this window (chrome attached, style/inherited/DPI/theme resolved), so
    /// placement measures the fully-styled popup instead of a pre-attach one whose named style and fonts
    /// have not resolved yet. Returns the measured placement bounds.
    /// </summary>
    internal Rect ShowPopup(UIElement owner, UIElement popup, Func<Window, Rect> measureBounds, bool sizeToContent = false, bool staysOpen = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(popup);

        // Opening an interactive popup is a user interaction with this window, but not every platform
        // activates an inactive window on the triggering click (e.g. right-click), so anchor activation
        // here: keyboard focus and the deactivation-close policy both depend on the owner being active.
        // Hover popups (ToolTip, hit-test-invisible) must not steal activation.
        if (popup.IsHitTestVisible && !_window.IsActive)
        {
            _window.Activate();
        }

        // Replace if already present.
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element == popup)
            {
                _popups[i].Owner = owner;
                _popups[i].Host?.OnOwnerChanged(_popups[i]);
                var updatedBounds = measureBounds(_window);
                UpdatePopup(popup, updatedBounds);
                return updatedBounds;
            }
        }

        var host = ResolveHost();
        var entry = new PopupEntry { Owner = owner, Element = popup, StaysOpen = staysOpen, Host = host };

        // Root the popup before measuring it for placement: its named style (e.g. ComboBoxPopup) and
        // inherited properties (fonts) only resolve once its context chain reaches this window's
        // stylesheet, which is what attaching the chrome establishes. Measuring first (as callers used
        // to) sized the popup from unstyled metrics - a zero border thickness that forced a spurious
        // scrollbar, or a fallback font that clipped content.
        PopupHostSupport.AttachChrome(_window, entry);
        var measuredBounds = measureBounds(_window);
        entry.Bounds = measuredBounds;
        // Register before showing the native surface: when a sibling popup (submenu) shows and takes
        // the platform watch during ShowSurface, the parent's watch-transfer check scans _popups to
        // recognize it as a popup surface. If the new popup is not yet listed, the parent gets a
        // spurious dismiss and the whole chain closes.
        _popups.Add(entry);
        host.Attach(entry, sizeToContent);
        host.Layout(entry);

        _window.Invalidate();
        return measuredBounds;
    }

    internal void UpdatePopup(UIElement popup, Rect bounds)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element != popup)
            {
                continue;
            }

            _popups[i].Host?.UpdateBounds(_popups[i], bounds);
            _window.Invalidate();
            return;
        }
    }

    internal void ClosePopup(UIElement popup)
    {
        ClosePopup(popup, PopupCloseKind.UserInitiated);
    }

    internal void ClosePopup(UIElement popup, PopupCloseKind kind)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element != popup)
            {
                continue;
            }

            CloseAndDetachEntry(i, kind);
            _window.Invalidate();
            return;
        }
    }

    internal void RequestClosePopups(PopupCloseRequest request)
    {
        if (_popups.Count == 0 || _isClosingPopups)
        {
            return;
        }

        switch (request.TriggerKind)
        {
            case PopupCloseRequest.Trigger.PointerDown:
            {
                var leaf = request.PointerLeaf;
                // Hit-test-invisible popups (e.g. ToolTip) are never "related" - always close on any click.
                CloseTransientPopups(leaf == null
                    ? null
                    : entry => entry.Element.IsHitTestVisible && IsRelated(leaf, entry, applyContextMenuOwnerPolicy: true));
                break;
            }
            case PopupCloseRequest.Trigger.FocusChanged:
            {
                var focused = request.NewFocusedElement;
                CloseTransientPopups(focused == null
                    ? null
                    : entry => IsRelated(focused, entry, applyContextMenuOwnerPolicy: false));
                break;
            }
            case PopupCloseRequest.Trigger.Explicit:
                CloseTransientPopups(shouldKeep: null);
                break;
            case PopupCloseRequest.Trigger.Lifecycle:
                CloseAllPopups();
                break;
            case PopupCloseRequest.Trigger.Scroll:
            {
                var src = request.Source;
                CloseTransientPopups(src == null
                    ? null
                    : entry => IsAncestor(entry.Element, src));
                break;
            }
        }
    }

    /// <summary>
    /// Returns true when <paramref name="possibleAncestor"/> appears in the visual parent chain of <paramref name="descendant"/>.
    /// </summary>
    private static bool IsAncestor(UIElement possibleAncestor, UIElement descendant)
    {
        for (var p = descendant.Parent as UIElement; p != null; p = p.Parent as UIElement)
        {
            if (ReferenceEquals(p, possibleAncestor)) return true;
        }
        return false;
    }

    /// <summary>
    /// Walks the popup owner chain (via <see cref="WindowInputRouter.GetInputBubbleParent"/>)
    /// to determine whether <paramref name="leaf"/> is logically related to <paramref name="entry"/>.
    /// </summary>
    /// <param name="leaf">The element to start walking the popup owner chain from.</param>
    /// <param name="entry">The popup entry to check against.</param>
    /// <param name="applyContextMenuOwnerPolicy">
    /// When true (pointer-triggered close), context menus close when clicking their non-ContextMenu owner
    /// (common desktop UX: click to toggle). When false (focus-triggered close), the owner always counts as related.
    /// </param>
    private bool IsRelated(UIElement leaf, PopupEntry entry, bool applyContextMenuOwnerPolicy)
    {
        bool ownerCounts = !applyContextMenuOwnerPolicy
            || entry.Element is not ContextMenu
            || entry.Owner is ContextMenu;

        for (var current = leaf; current != null; current = WindowInputRouter.GetInputBubbleParent(_window, current))
        {
            if (ReferenceEquals(current, entry.Element) || (ownerCounts && ReferenceEquals(current, entry.Owner)))
            {
                return true;
            }
        }

        return false;
    }

    private void CloseTransientPopups(Func<PopupEntry, bool>? shouldKeep)
    {
        if (_popups.Count == 0 || _isClosingPopups)
        {
            return;
        }

        _isClosingPopups = true;
        try
        {
            bool removedAny = false;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var entry = _popups[i];
                if (entry.StaysOpen)
                {
                    continue;
                }

                if (shouldKeep != null && shouldKeep(entry))
                {
                    continue;
                }

                CloseAndDetachEntry(i, PopupCloseKind.Policy);
                removedAny = true;
            }

            if (removedAny)
            {
                _window.Invalidate();
            }
        }
        finally
        {
            _isClosingPopups = false;
        }
    }

    private void CloseAndDetachEntry(int index, PopupCloseKind kind)
    {
        // Remove from the list before the side-effecting detach: severing the popup's Parent can run
        // focus/close side effects that re-enter this manager, and a stale index would strand RemoveAt.
        var entry = _popups[index];
        _popups.RemoveAt(index);
        entry.Host?.Detach(entry);

        if (entry.Owner is IPopupOwner owner)
        {
            owner.OnPopupClosed(entry.Element, kind);
        }

        EnsureFocusNotInClosedPopup(entry.Element, entry.Owner);

        if (ReferenceEquals(entry.Element, _toolTip))
        {
            _toolTipOwner = null;
        }
    }

    internal bool TryGetPopupOwner(UIElement popup, out UIElement owner)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element == popup)
            {
                owner = _popups[i].Owner;
                return true;
            }
        }

        owner = popup;
        return false;
    }

    /// <summary>
    /// Finds the open popup whose subtree contains <paramref name="element"/>, so tab navigation can be
    /// scoped to that popup. Returns false when the element is not inside any open popup.
    /// </summary>
    internal bool TryGetEnclosingPopup(UIElement element, out UIElement popupRoot)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (current is UIElement candidate && IsPopupElement(candidate))
            {
                popupRoot = candidate;
                return true;
            }
        }

        popupRoot = element;
        return false;
    }

    private bool IsPopupElement(UIElement element)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            if (ReferenceEquals(_popups[i].Element, element))
            {
                return true;
            }
        }

        return false;
    }

    internal Size MeasureToolTip(Element content, Size availableSize)
    {
        ArgumentNullException.ThrowIfNull(content);

        _toolTip ??= new ToolTip();
        _toolTip.Content = content;
        EnsureToolTipInheritsFromWindow();
        _toolTip.Measure(availableSize);
        return _toolTip.DesiredSize;
    }

    /// <summary>
    /// Ensures the tooltip can resolve inherited properties (e.g. FontFamily) before
    /// it is added to the visual tree via ShowPopup. Without this, the tooltip measures
    /// with the registered default font ("Segoe UI") instead of the platform/theme font.
    /// </summary>
    private void EnsureToolTipInheritsFromWindow()
    {
        if (_toolTip == null)
        {
            return;
        }

        // If the tooltip is not in the visual tree, temporarily parent it to the window
        // so inherited properties and styles resolve correctly during measurement.
        if (_toolTip.Parent == null)
        {
            _toolTip.Parent = _window;
            _toolTip.ResolveAndApplyStyle();
            _toolTip.InvalidateMeasure();
        }
    }

    internal void ShowToolTip(UIElement owner, Element content, Rect bounds)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);

        _toolTip ??= new ToolTip();
        _toolTip.Content = content;
        _toolTipOwner = owner;
        ShowPopup(owner, _toolTip, bounds);
    }

    internal void CloseToolTip(UIElement? owner = null)
    {
        if (_toolTip == null)
        {
            return;
        }

        if (owner != null && !ReferenceEquals(_toolTipOwner, owner))
        {
            return;
        }

        ClosePopup(_toolTip);
        _toolTipOwner = null;
    }

    private void EnsureFocusNotInClosedPopup(UIElement popup, UIElement owner)
    {
        var focused = _window.FocusManager.FocusedElement;
        if (focused == null)
        {
            // Even if no element is focused, the popup detach may have left a stale IsFocusWithin
            // on the owner because the parent chain was severed before ClearFocus could walk it.
            if (owner.IsFocusWithin)
            {
                owner.SetFocusWithin(false);
            }

            return;
        }

        if (focused != popup && !VisualTree.IsInSubtreeOf(focused, popup))
        {
            return;
        }

        // Prefer restoring focus to the owner, otherwise clear focus to avoid leaving focus on a detached popup.
        if (owner.Focusable && owner.IsEffectivelyEnabled && owner.IsVisible)
        {
            _window.FocusManager.SetFocus(owner);
        }
        else
        {
            _window.FocusManager.ClearFocus();

            // The popup element is already detached (Parent = null), so ClearFocus cannot walk
            // the parent chain to clear IsFocusWithin on the owner. Clear it explicitly.
            if (owner.IsFocusWithin)
            {
                owner.SetFocusWithin(false);
            }
        }
    }
}

public enum PopupCloseKind
{
    UserInitiated,
    Policy,
    Lifecycle,
}

/// <summary>
/// Describes a popup close policy request. Use the static factory methods to create instances.
/// </summary>
internal readonly struct PopupCloseRequest
{
    internal enum Trigger
    {
        PointerDown,
        FocusChanged,
        Explicit,
        Lifecycle,
        Scroll,
    }

    private PopupCloseRequest(Trigger trigger, PopupCloseKind closeKind, UIElement? pointerLeaf, UIElement? newFocusedElement, UIElement? source)
    {
        TriggerKind = trigger;
        CloseKind = closeKind;
        PointerLeaf = pointerLeaf;
        NewFocusedElement = newFocusedElement;
        Source = source;
    }

    internal PopupCloseKind CloseKind { get; }

    internal UIElement? PointerLeaf { get; }

    internal UIElement? NewFocusedElement { get; }

    internal UIElement? Source { get; }

    internal Trigger TriggerKind { get; }

    public static PopupCloseRequest PointerDown(UIElement? pointerLeaf, PopupCloseKind closeKind = PopupCloseKind.Policy)
        => new(Trigger.PointerDown, closeKind, pointerLeaf, newFocusedElement: null, source: null);

    public static PopupCloseRequest FocusChanged(UIElement? newFocusedElement, PopupCloseKind closeKind = PopupCloseKind.Policy)
        => new(Trigger.FocusChanged, closeKind, pointerLeaf: null, newFocusedElement, source: null);

    public static PopupCloseRequest Explicit(PopupCloseKind closeKind = PopupCloseKind.UserInitiated)
        => new(Trigger.Explicit, closeKind, pointerLeaf: null, newFocusedElement: null, source: null);

    public static PopupCloseRequest Lifecycle(PopupCloseKind closeKind = PopupCloseKind.Lifecycle)
        => new(Trigger.Lifecycle, closeKind, pointerLeaf: null, newFocusedElement: null, source: null);

    public static PopupCloseRequest Scroll(UIElement? source = null, PopupCloseKind closeKind = PopupCloseKind.Policy)
        => new(Trigger.Scroll, closeKind, pointerLeaf: null, newFocusedElement: null, source);
}
