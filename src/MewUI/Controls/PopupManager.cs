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
            var entry = _popups[i];
            entry.Chrome?.ClearDpiCacheDeep();

            if (entry.Element is Control c)
            {
                c.NotifyDpiChanged(oldDpi, newDpi);
            }

            entry.Element.ClearDpiCacheDeep();
        }
    }

    internal void CloseAllPopups()
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            DetachEntry(entry);
            if (entry.Owner is IPopupOwner owner)
            {
                owner.OnPopupClosed(entry.Element, PopupCloseKind.Lifecycle);
            }

            EnsureFocusNotInClosedPopup(entry.Element, entry.Owner);
        }

        _popups.Clear();
        _toolTipOwner = null;
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

            var entry = _popups[i];
            DetachEntry(entry);
            _popups.RemoveAt(i);
            if (entry.Owner is IPopupOwner owner)
            {
                owner.OnPopupClosed(entry.Element, kind);
            }

            EnsureFocusNotInClosedPopup(entry.Element, entry.Owner);

            _window.Invalidate();
            if (ReferenceEquals(popup, _toolTip))
            {
                _toolTipOwner = null;
            }
            return;
        }
    }

    internal void CloseNonStaysOpenPopups()
    {
        if (_popups.Count == 0)
        {
            return;
        }

        if (_isClosingPopups)
        {
            return;
        }

        _isClosingPopups = true;
        try
        {
            var focused = _window.FocusManager.FocusedElement;
            UIElement? restoreFocusTo = null;

            bool removedAny = false;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                if (_popups[i].StaysOpen)
                {
                    continue;
                }

                var entry = _popups[i];

                if (focused != null && (focused == entry.Element || VisualTree.IsInSubtreeOf(focused, entry.Element)))
                {
                    restoreFocusTo = entry.Owner;
                }

                DetachEntry(entry);
                if (entry.Owner is IPopupOwner owner)
                {
                    owner.OnPopupClosed(entry.Element, PopupCloseKind.Policy);
                }

                _popups.RemoveAt(i);
                removedAny = true;
            }

            if (restoreFocusTo != null)
            {
                _window.FocusManager.SetFocus(restoreFocusTo);
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

    internal void RequestClosePopups(PopupCloseRequest request)
    {
        // Keep the method as the single entry point so policy evolves without changing call sites.
        if (_popups.Count == 0)
        {
            return;
        }

        if (_isClosingPopups)
        {
            return;
        }

        if (request.TriggerKind == PopupCloseRequest.Trigger.PointerDown)
        {
            CloseTransientPopupsExceptPointerRelated(request.PointerLeaf);
            return;
        }

        if (request.TriggerKind == PopupCloseRequest.Trigger.FocusChanged)
        {
            CloseTransientPopupsIfFocusMovedOutside(request.NewFocusedElement);
            return;
        }

        if (request.TriggerKind == PopupCloseRequest.Trigger.Explicit)
        {
            CloseNonStaysOpenPopups();
            return;
        }

        if (request.TriggerKind == PopupCloseRequest.Trigger.Lifecycle)
        {
            CloseAllPopups();
            return;
        }
    }

    private void CloseTransientPopupsExceptPointerRelated(UIElement? pointerLeaf)
    {
        // Click on empty area (or unknown target) => close all transient popups.
        if (pointerLeaf == null)
        {
            CloseNonStaysOpenPopups();
            return;
        }

        _isClosingPopups = true;
        try
        {
            var focused = _window.FocusManager.FocusedElement;
            UIElement? restoreFocusTo = null;

            bool removedAny = false;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var entry = _popups[i];

                if (entry.StaysOpen)
                {
                    continue;
                }

                // Close hit-test-invisible popups unconditionally (e.g. ToolTip).
                // They are not user-interactive and should not block normal click behavior.
                if (!entry.Element.IsHitTestVisible)
                {
                    if (focused != null && (focused == entry.Element || VisualTree.IsInSubtreeOf(focused, entry.Element)))
                    {
                        restoreFocusTo = entry.Owner;
                    }

                    DetachEntry(entry);
                    _popups.RemoveAt(i);
                    if (entry.Owner is IPopupOwner popupOwner)
                    {
                        popupOwner.OnPopupClosed(entry.Element, PopupCloseKind.Policy);
                    }

                    EnsureFocusNotInClosedPopup(entry.Element, entry.Owner);

                    if (ReferenceEquals(entry.Element, _toolTip))
                    {
                        _toolTipOwner = null;
                    }

                    removedAny = true;
                    continue;
                }

                // If the click is related to this popup, keep it.
                // "Related" means the click happened on the owner or inside the popup (bubble chain crosses popup->owner).
                if (IsPointerRelated(pointerLeaf, entry))
                {
                    continue;
                }

                // Close any non-related popup.
                if (focused != null && (focused == entry.Element || VisualTree.IsInSubtreeOf(focused, entry.Element)))
                {
                    restoreFocusTo = entry.Owner;
                }

                DetachEntry(entry);
                _popups.RemoveAt(i);
                if (entry.Owner is IPopupOwner popupOwner2)
                {
                    popupOwner2.OnPopupClosed(entry.Element, PopupCloseKind.Policy);
                }

                removedAny = true;

                EnsureFocusNotInClosedPopup(entry.Element, entry.Owner);
            }

            if (restoreFocusTo != null)
            {
                _window.FocusManager.SetFocus(restoreFocusTo);
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

    private bool IsPointerRelated(UIElement clickLeaf, PopupEntry entry)
    {
        for (var current = clickLeaf; current != null; current = WindowInputRouter.GetInputBubbleParent(_window, current))
        {
            if (ReferenceEquals(current, entry.Owner) || ReferenceEquals(current, entry.Element))
            {
                return true;
            }
        }

        return false;
    }

    private void CloseTransientPopupsIfFocusMovedOutside(UIElement? newFocusedElement)
    {
        if (_popups.Count == 0)
        {
            return;
        }

        if (_isClosingPopups)
        {
            return;
        }

        _isClosingPopups = true;
        try
        {
            var focused = _window.FocusManager.FocusedElement;
            UIElement? restoreFocusTo = null;

            bool removedAny = false;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var entry = _popups[i];
                if (entry.StaysOpen)
                {
                    continue;
                }

                if (newFocusedElement != null)
                {
                    if (ReferenceEquals(newFocusedElement, entry.Element) || VisualTree.IsInSubtreeOf(newFocusedElement, entry.Element))
                    {
                        continue;
                    }

                    if (ReferenceEquals(newFocusedElement, entry.Owner) || VisualTree.IsInSubtreeOf(newFocusedElement, entry.Owner))
                    {
                        continue;
                    }
                }

                if (focused != null && (focused == entry.Element || VisualTree.IsInSubtreeOf(focused, entry.Element)))
                {
                    restoreFocusTo = entry.Owner;
                }

                DetachEntry(entry);
                if (entry.Owner is IPopupOwner owner)
                {
                    owner.OnPopupClosed(entry.Element, PopupCloseKind.Policy);
                }

                _popups.RemoveAt(i);
                removedAny = true;
            }

            if (restoreFocusTo != null)
            {
                _window.FocusManager.SetFocus(restoreFocusTo);
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
        _toolTip.Measure(availableSize);
        return _toolTip.DesiredSize;
    }

    internal Size MeasureToolTip(Element content, Size availableSize)
    {
        ArgumentNullException.ThrowIfNull(content);

        _toolTip ??= new ToolTip();
        _toolTip.Text = string.Empty;
        _toolTip.Content = content;
        _toolTip.Measure(availableSize);
        return _toolTip.DesiredSize;
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
}
