using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

internal sealed class PopupManager
{
    private readonly Window _window;
    private readonly List<PopupEntry> _popups = new();

    private ToolTip? _toolTip;
    private UIElement? _toolTipOwner;

    private bool _isClosingPopups;

    public PopupManager(Window window) => _window = window;

    internal int Count => _popups.Count;

    internal UIElement ElementAt(int index) => _popups[index].Element;

    internal bool HasAny => _popups.Count > 0;

    internal bool HasLayoutDirty()
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var element = _popups[i].Element;
            if (element.IsMeasureDirty || element.IsArrangeDirty)
            {
                return true;
            }
        }

        return false;
    }

    internal void LayoutDirtyPopups()
    {
        if (_popups.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!entry.Element.IsVisible)
            {
                continue;
            }

            if (!entry.Element.IsMeasureDirty && !entry.Element.IsArrangeDirty)
            {
                continue;
            }

            LayoutPopup(entry);
        }
    }

    internal void Render(IGraphicsContext context)
    {
        // Popups render last (on top).
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (entry.Chrome != null)
            {
                entry.Chrome.Render(context);
            }
            else
            {
                entry.Element.Render(context);
            }
        }
    }

    internal UIElement? HitTest(Point point)
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            if (!_popups[i].Bounds.Contains(point))
            {
                continue;
            }

            var hit = _popups[i].Element.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return null;
    }

    internal void Dispose()
    {
        foreach (var entry in _popups)
        {
            if (entry.Element is IDisposable disposable)
            {
                disposable.Dispose();
            }

            DetachEntry(entry);
        }

        _popups.Clear();
        _toolTipOwner = null;
        _toolTip = null;
    }

    internal void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (entry.Chrome != null)
            {
                entry.Chrome.NotifyThemeChanged(oldTheme, newTheme);
            }

            if (entry.Element is Control c)
            {
                c.NotifyThemeChanged(oldTheme, newTheme);
            }
        }
    }

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var root = (UIElement?)_popups[i].Chrome ?? _popups[i].Element;
            ApplyPopupDpiChange(root, oldDpi, newDpi);
        }
    }

    internal void CloseAllPopups()
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            CloseAndDetachEntry(i, PopupCloseKind.Lifecycle);
        }

        _window.Invalidate();
    }

    internal void ShowPopup(UIElement owner, UIElement popup, Rect bounds, bool staysOpen = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(popup);

        // Replace if already present.
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element == popup)
            {
                _popups[i].Owner = owner;
                UpdatePopup(popup, bounds);
                return;
            }
        }

        // Popups can be cached/reused (e.g. ComboBox keeps a ListBox instance even while closed).
        // If a popup is moved between windows (or the window DPI differs), ensure the popup updates its DPI-sensitive
        // caches (fonts, layout) before measuring/arranging.
        uint oldDpi = popup.GetDpiCached();
        var oldTheme = popup is FrameworkElement popupElement
            ? popupElement.ThemeInternal
            : _window.ThemeInternal;

        // Wrap in PopupChrome so the drop shadow renders within the chrome's layout bounds,
        // avoiding clipping by ancestor clip regions.
        var chrome = new PopupChrome(popup);
        chrome.Parent = _window;
        chrome.AttachChild();

        ApplyPopupDpiChange(chrome, oldDpi, _window.Dpi);
        ApplyPopupThemeChange(chrome, oldTheme, _window.ThemeInternal);

        // Now that the popup is in the visual tree, inherited properties (e.g. FontFamily)
        // are resolvable. Force style re-resolution and measure invalidation so that any
        // measurement done before attachment (e.g. MeasureToolTip) is corrected.
        ForceStyleAndMeasureRefresh(popup);

        var entry = new PopupEntry { Owner = owner, Element = popup, Chrome = chrome, Bounds = bounds, StaysOpen = staysOpen };
        _popups.Add(entry);
        LayoutPopup(entry);
        
        _window.Invalidate();
    }

    internal void UpdatePopup(UIElement popup, Rect bounds)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].Element != popup)
            {
                continue;
            }

            _popups[i].Bounds = bounds;
            LayoutPopup(_popups[i]);
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
                // Hit-test-invisible popups (e.g. ToolTip) are never "related" — always close on any click.
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
                // Close only context menus on scroll; dropdowns reposition themselves.
                CloseTransientPopups(entry => entry.Element is not ContextMenu);
                break;
        }
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
        var entry = _popups[index];
        DetachEntry(entry);
        _popups.RemoveAt(index);

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

    internal Size MeasureToolTip(string text, Size availableSize)
    {
        _toolTip ??= new ToolTip();
        _toolTip.Content = null;
        _toolTip.Text = text ?? string.Empty;
        EnsureToolTipInheritsFromWindow();
        _toolTip.Measure(availableSize);
        return _toolTip.DesiredSize;
    }

    internal Size MeasureToolTip(Element content, Size availableSize)
    {
        ArgumentNullException.ThrowIfNull(content);

        _toolTip ??= new ToolTip();
        _toolTip.Text = string.Empty;
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

    internal void ShowToolTip(UIElement owner, string text, Rect bounds)
    {
        ArgumentNullException.ThrowIfNull(owner);

        _toolTip ??= new ToolTip();
        _toolTip.Content = null;
        _toolTip.Text = text ?? string.Empty;
        _toolTipOwner = owner;
        ShowPopup(owner, _toolTip, bounds);
    }

    internal void ShowToolTip(UIElement owner, Element content, Rect bounds)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);

        _toolTip ??= new ToolTip();
        _toolTip.Text = string.Empty;
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

    private static void DetachEntry(PopupEntry entry)
    {
        if (entry.Chrome != null)
        {
            entry.Chrome.DetachChild();
            entry.Chrome.Parent = null;
        }
        else
        {
            entry.Element.Parent = null;
        }
    }

    private static void LayoutPopup(PopupEntry entry)
    {
        if (entry.Chrome != null)
        {
            // Chrome bounds include shadow padding around the content area.
            var chromeBounds = entry.Bounds.Inflate(PopupChrome.ShadowPadding);
            entry.Chrome.Measure(new Size(chromeBounds.Width, chromeBounds.Height));
            entry.Chrome.Arrange(chromeBounds);

            // Keep the stored bounds consistent with the child's actually arranged (layout-rounded) bounds,
            // otherwise hit-testing (e.g. mouse wheel on popup content) can miss by sub-pixel rounding.
            entry.Bounds = entry.Chrome.Child.Bounds;
        }
        else
        {
            entry.Element.Measure(new Size(entry.Bounds.Width, entry.Bounds.Height));
            entry.Element.Arrange(entry.Bounds);
            entry.Bounds = entry.Element.Bounds;
        }
    }

    private void EnsureFocusNotInClosedPopup(UIElement popup, UIElement owner)
    {
        var focused = _window.FocusManager.FocusedElement;
        if (focused == null)
        {
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
        }
    }

    private static void ApplyPopupDpiChange(UIElement popup, uint oldDpi, uint newDpi)
    {
        if (oldDpi == 0 || newDpi == 0 || oldDpi == newDpi)
        {
            return;
        }

        // Clear DPI caches again (Parent assignment already does this, but be defensive for future changes),
        // and notify controls so they can recreate DPI-dependent resources (fonts, etc.).
        popup.ClearDpiCacheDeep();
        VisualTree.Visit(popup, e =>
        {
            e.ClearDpiCache();
            if (e is Control c)
            {
                c.NotifyDpiChanged(oldDpi, newDpi);
            }
        });
    }

    private static void ForceStyleAndMeasureRefresh(UIElement popup)
    {
        VisualTree.Visit(popup, e =>
        {
            if (e is Control c)
            {
                c.ResolveAndApplyStyle();
                c.InvalidateFontCache(FontFamilyProperty);
            }

            e.InvalidateMeasure();
        });
    }

    private static readonly MewProperty FontFamilyProperty = Control.FontFamilyProperty;

    private static void ApplyPopupThemeChange(UIElement popup, Theme oldTheme, Theme newTheme)
    {
        if (oldTheme == newTheme)
        {
            return;
        }

        VisualTree.Visit(popup, e =>
        {
            if (e is FrameworkElement element)
            {
                element.NotifyThemeChanged(oldTheme, newTheme);
            }
        });
    }

    internal sealed class PopupEntry
    {
        public required UIElement Element { get; init; }

        public required UIElement Owner { get; set; }

        public Rect Bounds { get; set; }

        public bool StaysOpen { get; set; }

        public PopupChrome? Chrome { get; set; }
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

    private PopupCloseRequest(Trigger trigger, PopupCloseKind closeKind, UIElement? pointerLeaf, UIElement? newFocusedElement)
    {
        TriggerKind = trigger;
        CloseKind = closeKind;
        PointerLeaf = pointerLeaf;
        NewFocusedElement = newFocusedElement;
    }

    internal PopupCloseKind CloseKind { get; }

    internal UIElement? PointerLeaf { get; }

    internal UIElement? NewFocusedElement { get; }

    internal Trigger TriggerKind { get; }

    public static PopupCloseRequest PointerDown(UIElement? pointerLeaf, PopupCloseKind closeKind = PopupCloseKind.Policy)
        => new(Trigger.PointerDown, closeKind, pointerLeaf, newFocusedElement: null);

    public static PopupCloseRequest FocusChanged(UIElement? newFocusedElement, PopupCloseKind closeKind = PopupCloseKind.Policy)
        => new(Trigger.FocusChanged, closeKind, pointerLeaf: null, newFocusedElement);

    public static PopupCloseRequest Explicit(PopupCloseKind closeKind = PopupCloseKind.UserInitiated)
        => new(Trigger.Explicit, closeKind, pointerLeaf: null, newFocusedElement: null);

    public static PopupCloseRequest Lifecycle(PopupCloseKind closeKind = PopupCloseKind.Lifecycle)
        => new(Trigger.Lifecycle, closeKind, pointerLeaf: null, newFocusedElement: null);

    public static PopupCloseRequest Scroll(PopupCloseKind closeKind = PopupCloseKind.Policy)
        => new(Trigger.Scroll, closeKind, pointerLeaf: null, newFocusedElement: null);
}
