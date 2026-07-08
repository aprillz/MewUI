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
    private readonly ElementLayer _layer;
    private readonly Dictionary<Type, IOverlayService> _services = new();

    internal OverlayLayer(Window window)
    {
        _layer = new ElementLayer(window);
    }

    /// <summary>
    /// Gets the number of active overlays.
    /// </summary>
    public int Count => _layer.Count;

    /// <summary>
    /// Adds an overlay. Later-added overlays render on top.
    /// </summary>
    public void Add(UIElement overlay) => _layer.Add(overlay);

    /// <summary>
    /// Removes a previously added overlay.
    /// </summary>
    public bool Remove(UIElement overlay) => _layer.Remove(overlay);

    /// <summary>
    /// Checks whether the specified overlay is currently in this layer.
    /// </summary>
    public bool Contains(UIElement overlay) => _layer.Contains(overlay);

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

    internal bool HasLayoutDirty() => _layer.HasLayoutDirty();

    internal void Layout(Size clientSize)
    {
        for (int i = 0; i < _layer.Count; i++)
        {
            var overlay = _layer[i];
            if (!overlay.IsVisible) continue;

            overlay.Measure(clientSize);
            overlay.Arrange(new Rect(0, 0, clientSize.Width, clientSize.Height));
        }
    }

    internal void Render(IGraphicsContext context)
    {
        for (int i = 0; i < _layer.Count; i++)
        {
            _layer[i].Render(context);
        }
    }

    internal UIElement? HitTest(Point point) => _layer.HitTest(point);

    internal void NotifyThemeChanged(Theme oldTheme, Theme newTheme) => _layer.NotifyThemeChanged(oldTheme, newTheme);

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi) => _layer.NotifyDpiChanged(oldDpi, newDpi);

    internal void VisitAll(Action<Element> visitor)
    {
        for (int i = 0; i < _layer.Count; i++)
        {
            Window.VisitVisualTree(_layer[i], visitor);
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

        _layer.Dispose();
    }
}
