using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extensions for menus.
/// </summary>
public static class MenuExtensions
{
    /// <summary>
    /// Sets the item height.
    /// </summary>
    /// <param name="menu">Target menu.</param>
    /// <param name="itemHeight">Item height.</param>
    /// <returns>The menu for chaining.</returns>
    public static Menu ItemHeight(this Menu menu, double itemHeight)
    {
        menu.ItemHeight = itemHeight;
        return menu;
    }

    /// <summary>
    /// Sets the item padding.
    /// </summary>
    /// <param name="menu">Target menu.</param>
    /// <param name="itemPadding">Item padding.</param>
    /// <returns>The menu for chaining.</returns>
    public static Menu ItemPadding(this Menu menu, Thickness itemPadding)
    {
        menu.ItemPadding = itemPadding;
        return menu;
    }

    /// <summary>
    /// Sets the spacing between menu items.
    /// </summary>
    /// <param name="bar">Target menu bar.</param>
    /// <param name="spacing">Spacing value.</param>
    /// <returns>The menu bar for chaining.</returns>
    public static MenuBar Spacing(this MenuBar bar, double spacing)
    {
        bar.Spacing = spacing;
        return bar;
    }

    /// <summary>
    /// Sets the menu items.
    /// </summary>
    /// <param name="bar">Target menu bar.</param>
    /// <param name="items">Menu items.</param>
    /// <returns>The menu bar for chaining.</returns>
    public static MenuBar Items(this MenuBar bar, params MenuItem[] items)
    {
        bar.SetItems(items);
        return bar;
    }

    /// <summary>
    /// Adds a menu item.
    /// </summary>
    /// <param name="bar">Target menu bar.</param>
    /// <param name="item">Menu item to add.</param>
    /// <returns>The menu bar for chaining.</returns>
    public static MenuBar Item(this MenuBar bar, MenuItem item)
    {
        bar.Add(item);
        return bar;
    }

    /// <summary>
    /// Adds a menu item with text and submenu.
    /// </summary>
    /// <param name="bar">Target menu bar.</param>
    /// <param name="text">Menu item text.</param>
    /// <param name="menu">Submenu.</param>
    /// <returns>The menu bar for chaining.</returns>
    public static MenuBar Item(this MenuBar bar, string text, Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        bar.Add(new MenuItem(text).Menu(menu));
        return bar;
    }

    /// <summary>
    /// Sets the menu item text.
    /// </summary>
    /// <param name="item">Target menu item.</param>
    /// <param name="text">Item text.</param>
    /// <returns>The menu item for chaining.</returns>
    public static MenuItem Text(this MenuItem item, string text)
    {
        item.Text = text ?? string.Empty;
        return item;
    }

    /// <summary>
    /// Sets the submenu.
    /// </summary>
    /// <param name="item">Target menu item.</param>
    /// <param name="menu">Submenu.</param>
    /// <returns>The menu item for chaining.</returns>
    public static MenuItem Menu(this MenuItem item, Menu? menu)
    {
        item.SubMenu = menu;
        return item;
    }

    /// <summary>
    /// Sets the shortcut text.
    /// </summary>
    /// <param name="item">Target menu item.</param>
    /// <param name="shortcutText">Shortcut text.</param>
    /// <returns>The menu item for chaining.</returns>
    public static MenuItem Shortcut(this MenuItem item, string? shortcutText)
    {
        item.ShortcutText = shortcutText;
        return item;
    }


    /// <summary>
    /// Adds an entry to the menu.
    /// </summary>
    /// <param name="menu">Target menu.</param>
    /// <param name="entry">Menu entry to add.</param>
    /// <returns>The menu for chaining.</returns>
    public static Menu Add(this Menu menu, MenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(entry);
        menu.Items.Add(entry);
        return menu;
    }

    /// <summary>
    /// Adds a clickable menu item.
    /// </summary>
    /// <param name="menu">Target menu.</param>
    /// <param name="text">Menu item text.</param>
    /// <param name="onClick">Click handler.</param>
    /// <param name="isEnabled">Whether the item is enabled.</param>
    /// <param name="shortcutText">Shortcut display text (optional).</param>
    /// <returns>The menu for chaining.</returns>
    public static Menu Item(this Menu menu, string text, Action? onClick = null, bool isEnabled = true, string? shortcutText = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.Items.Add(new MenuItem
        {
            Text = text ?? string.Empty,
            Click = onClick,
            IsEnabled = isEnabled,
            ShortcutText = shortcutText
        });
        return menu;
    }

    /// <summary>
    /// Adds a submenu item.
    /// </summary>
    /// <param name="menu">Target menu.</param>
    /// <param name="text">Menu item text.</param>
    /// <param name="subMenu">Submenu.</param>
    /// <param name="isEnabled">Whether the item is enabled.</param>
    /// <param name="shortcutText">Shortcut display text (optional).</param>
    /// <returns>The menu for chaining.</returns>
    public static Menu SubMenu(this Menu menu, string text, Menu subMenu, bool isEnabled = true, string? shortcutText = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(subMenu);

        menu.Items.Add(new MenuItem
        {
            Text = text ?? string.Empty,
            IsEnabled = isEnabled,
            ShortcutText = shortcutText,
            SubMenu = subMenu
        });
        return menu;
    }

    /// <summary>
    /// Adds a separator.
    /// </summary>
    /// <param name="menu">Target menu.</param>
    /// <returns>The menu for chaining.</returns>
    public static Menu Separator(this Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.Items.Add(MenuSeparator.Instance);
        return menu;
    }
}

