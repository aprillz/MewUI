namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared bookkeeping for a window-owned collection of top-level elements (overlays, adorners, popups):
/// storage, parent attach/detach with layout/render invalidation, layout-dirty checks, reverse-order
/// hit-testing, theme/DPI broadcast, and disposal. Kept internal since it is an implementation detail
/// of Window's overlay-style layers, not a public extension point.
/// </summary>
internal sealed class ElementLayer
{
    private readonly Window _window;
    private readonly List<UIElement> _elements = new();

    internal ElementLayer(Window window)
    {
        _window = window;
    }

    internal int Count => _elements.Count;

    internal UIElement this[int index] => _elements[index];

    internal bool Contains(UIElement element) => _elements.Contains(element);

    /// <summary>
    /// Attaches the element to the owning window and adds it to the layer. No-op if already present.
    /// Requests a layout and render pass, matching the overlay/adorner add contract.
    /// </summary>
    internal void Add(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_elements.Contains(element)) return;

        element.Parent = _window;
        _elements.Add(element);
        _window.RequestLayout();
        _window.RequestRender();
    }

    /// <summary>
    /// Detaches the element from the owning window and removes it from the layer.
    /// Requests a layout and render pass if the element was present.
    /// </summary>
    internal bool Remove(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_elements.Remove(element)) return false;

        element.Parent = null;
        _window.RequestLayout();
        _window.RequestRender();
        return true;
    }

    internal bool HasLayoutDirty()
    {
        for (int i = 0; i < _elements.Count; i++)
        {
            if (_elements[i].IsMeasureDirty || _elements[i].IsArrangeDirty)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Hit-tests elements in reverse insertion order (later-added, i.e. topmost, first).
    /// </summary>
    internal UIElement? HitTest(Point point)
    {
        for (int i = _elements.Count - 1; i >= 0; i--)
        {
            var hit = _elements[i].HitTest(point);
            if (hit != null)
                return hit;
        }
        return null;
    }

    internal void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        for (int i = 0; i < _elements.Count; i++)
        {
            if (_elements[i] is FrameworkElement fe)
                fe.NotifyThemeChanged(oldTheme, newTheme);
        }
    }

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        for (int i = 0; i < _elements.Count; i++)
        {
            if (_elements[i] is FrameworkElement fe)
                fe.NotifyDpiChanged(oldDpi, newDpi);

            _elements[i].ClearDpiCacheDeep();
        }
    }

    /// <summary>
    /// Disposes and detaches every element, then clears the layer. Does not dispose any
    /// caller-owned state (e.g. registered services); callers must dispose those first.
    /// </summary>
    internal void Dispose()
    {
        for (int i = 0; i < _elements.Count; i++)
        {
            if (_elements[i] is IDisposable disposable)
                disposable.Dispose();

            _elements[i].Parent = null;
        }
        _elements.Clear();
    }
}
