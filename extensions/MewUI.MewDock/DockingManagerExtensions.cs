using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewDock;

/// <summary>
/// Fluent setters for <see cref="DockingManager"/> so it configures in a chained style (the control already inherits
/// the generic element extensions like <c>DockTop()</c> / <c>Content()</c>).
/// </summary>
/// <remarks>
/// A delegate-typed property is invocable, so <c>manager.ContentFactory(x)</c> binds to the property and never reaches an
/// extension; such properties use the <c>With</c> prefix and declare the property with <c>FluentSetsMember</c>.
/// </remarks>
public static class DockingManagerExtensions
{
    /// <summary>Sets <see cref="DockingManager.ContentFactory"/> and returns the manager.</summary>
    /// <param name="manager">Target manager.</param>
    /// <param name="factory">Factory that builds a pane's content, or null to clear it.</param>
    /// <returns>The manager for chaining.</returns>
    public static DockingManager WithContentFactory(
        this DockingManager manager,
        [FluentSetsMember(nameof(DockingManager.ContentFactory))] Func<DockPane, UIElement?>? factory)
    {
        manager.ContentFactory = factory;
        return manager;
    }

    /// <summary>Sets <see cref="DockingManager.HeaderFactory"/> and returns the manager.</summary>
    /// <param name="manager">Target manager.</param>
    /// <param name="factory">Factory that builds a pane's tab header, or null for the default header.</param>
    /// <returns>The manager for chaining.</returns>
    public static DockingManager WithHeaderFactory(
        this DockingManager manager,
        [FluentSetsMember(nameof(DockingManager.HeaderFactory))] Func<DockPane, UIElement?>? factory)
    {
        manager.HeaderFactory = factory;
        return manager;
    }

    /// <summary>Obsolete name of <see cref="DockingManagerMarkupExtensions.CenterContent(DockingManager, UIElement)"/>.</summary>
    [Obsolete("Use CenterContent.")]
    public static DockingManager WithCenterContent(this DockingManager manager, UIElement? content)
    {
        manager.CenterContent = content;
        return manager;
    }

    /// <summary>Adds a tab menu opening event handler.</summary>
    public static DockingManager OnTabMenuOpening(this DockingManager manager, Action<DockTabMenuEventArgs> handler)
    {
        manager.TabMenuOpening += (_, e) => handler(e);
        return manager;
    }

    /// <summary>Adds a group menu opening event handler.</summary>
    public static DockingManager OnGroupMenuOpening(this DockingManager manager, Action<DockGroupMenuEventArgs> handler)
    {
        manager.GroupMenuOpening += (_, e) => handler(e);
        return manager;
    }

    /// <summary>Adds an active pane changed event handler.</summary>
    public static DockingManager OnActivePaneChanged(this DockingManager manager, Action<DockPane?> handler)
    {
        manager.ActivePaneChanged += (_, pane) => handler(pane);
        return manager;
    }

    /// <summary>Adds a changed event handler.</summary>
    public static DockingManager OnChanged(this DockingManager manager, Action handler)
    {
        manager.Changed += (_, _) => handler();
        return manager;
    }
}
