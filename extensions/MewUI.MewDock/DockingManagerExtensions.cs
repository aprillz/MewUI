using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewDock;

/// <summary>
/// Fluent setters for <see cref="DockingManager"/> so it configures in a chained style (the control already inherits
/// the generic element extensions like <c>DockTop()</c> / <c>Content()</c>). Setters use a <c>With</c> prefix because
/// several targets are delegate-typed properties - a same-named extension would be shadowed by a delegate invocation.
/// </summary>
public static class DockingManagerExtensions
{
    /// <summary>Sets <see cref="DockingManager.ContentFactory"/> and returns the manager.</summary>
    public static DockingManager WithContentFactory(this DockingManager manager, Func<DockPane, UIElement?> factory)
    {
        manager.ContentFactory = factory;
        return manager;
    }

    /// <summary>Sets <see cref="DockingManager.HeaderFactory"/> and returns the manager.</summary>
    public static DockingManager WithHeaderFactory(this DockingManager manager, Func<DockPane, UIElement?> factory)
    {
        manager.HeaderFactory = factory;
        return manager;
    }

    /// <summary>Sets <see cref="DockingManager.CenterContent"/> and returns the manager.</summary>
    public static DockingManager WithCenterContent(this DockingManager manager, UIElement? content)
    {
        manager.CenterContent = content;
        return manager;
    }

    /// <summary>Subscribes to <see cref="DockingManager.TabMenuOpening"/> and returns the manager.</summary>
    public static DockingManager OnTabMenuOpening(this DockingManager manager, Action<DockTabMenuEventArgs> handler)
    {
        manager.TabMenuOpening += (_, e) => handler(e);
        return manager;
    }

    /// <summary>Subscribes to <see cref="DockingManager.GroupMenuOpening"/> and returns the manager.</summary>
    public static DockingManager OnGroupMenuOpening(this DockingManager manager, Action<DockGroupMenuEventArgs> handler)
    {
        manager.GroupMenuOpening += (_, e) => handler(e);
        return manager;
    }

    /// <summary>Subscribes to <see cref="DockingManager.ActivePaneChanged"/> and returns the manager.</summary>
    public static DockingManager OnActivePaneChanged(this DockingManager manager, Action<DockPane?> handler)
    {
        manager.ActivePaneChanged += (_, pane) => handler(pane);
        return manager;
    }

    /// <summary>Subscribes to <see cref="DockingManager.Changed"/> and returns the manager.</summary>
    public static DockingManager OnChanged(this DockingManager manager, Action handler)
    {
        manager.Changed += (_, _) => handler();
        return manager;
    }
}
