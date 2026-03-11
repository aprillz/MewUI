using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Window-level overlay layer for elements that render on top of normal content
/// but are positioned relative to the window (not a specific element).
/// Examples: toast notifications, progress rings, dim backgrounds.
/// Overlays render in insertion order (later = on top).
/// </summary>
public sealed class OverlayLayer
{
    private readonly Window _window;
    private readonly List<UIElement> _overlays = new();

    internal OverlayLayer(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Gets the number of active overlays.
    /// </summary>
    public int Count => _overlays.Count;

    /// <summary>
    /// Adds an overlay. Later-added overlays render on top.
    /// </summary>
    public void Add(UIElement overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (_overlays.Contains(overlay)) return;

        overlay.Parent = _window;
        _overlays.Add(overlay);
        _window.RequestLayout();
        _window.RequestRender();
    }

    /// <summary>
    /// Removes a previously added overlay.
    /// </summary>
    public bool Remove(UIElement overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        if (!_overlays.Remove(overlay)) return false;

        overlay.Parent = null;
        _window.RequestLayout();
        _window.RequestRender();
        return true;
    }

    /// <summary>
    /// Checks whether the specified overlay is currently in this layer.
    /// </summary>
    public bool Contains(UIElement overlay) => _overlays.Contains(overlay);

    internal bool HasLayoutDirty()
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i].IsMeasureDirty || _overlays[i].IsArrangeDirty)
                return true;
        }
        return false;
    }

    internal void Layout(Size clientSize)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            var overlay = _overlays[i];
            if (!overlay.IsVisible) continue;

            overlay.Measure(clientSize);
            overlay.Arrange(new Rect(0, 0, clientSize.Width, clientSize.Height));
        }
    }

    internal void Render(IGraphicsContext context)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            _overlays[i].Render(context);
        }
    }

    internal UIElement? HitTest(Point point)
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            var hit = _overlays[i].HitTest(point);
            if (hit != null)
                return hit;
        }
        return null;
    }

    internal void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i] is Control c)
                c.NotifyThemeChanged(oldTheme, newTheme);
        }
    }

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i] is Control c)
                c.NotifyDpiChanged(oldDpi, newDpi);

            _overlays[i].ClearDpiCacheDeep();
        }
    }

    internal void VisitAll(Action<Element> visitor)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            Window.VisitVisualTree(_overlays[i], visitor);
        }
    }

    internal void Dispose()
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i] is IDisposable disposable)
                disposable.Dispose();

            _overlays[i].Parent = null;
        }
        _overlays.Clear();
    }
}
