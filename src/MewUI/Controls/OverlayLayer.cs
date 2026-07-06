using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Marker interface for overlay services registered with <see cref="OverlayLayer"/>.
/// Services are pure logic objects that internally manage their own presenter controls.
/// </summary>
public interface IOverlayService { }

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
    private readonly Dictionary<Type, IOverlayService> _services = new();

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

    /// <summary>
    /// Registers (or replaces) a service of the given type.
    /// </summary>
    public void RegisterService<T>(T service) where T : IOverlayService
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
    }

    /// <summary>
    /// Gets a registered service, creating it with the factory if not yet registered.
    /// The factory receives this <see cref="OverlayLayer"/> so the service can manage its own presenter controls.
    /// </summary>
    public T GetOrCreateService<T>(Func<OverlayLayer, T> factory) where T : IOverlayService
    {
        if (_services.TryGetValue(typeof(T), out var existing))
            return (T)existing;

        var service = factory(this);
        _services[typeof(T)] = service;
        return service;
    }

    /// <summary>
    /// Gets a registered service, or <c>null</c> if not registered.
    /// </summary>
    public T? GetService<T>() where T : class, IOverlayService
    {
        return _services.TryGetValue(typeof(T), out var service) ? (T)service : null;
    }

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

    internal UIElement? HitTest(Point point) => _layer.HitTest(point);

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i] is FrameworkElement fe)
                fe.NotifyDpiChanged(oldDpi, newDpi);

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
        foreach (var service in _services.Values)
        {
            if (service is IDisposable disposable)
                disposable.Dispose();
        }
        _services.Clear();

        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i] is IDisposable disposable)
                disposable.Dispose();

            _overlays[i].Parent = null;
        }
        _overlays.Clear();
    }
}
