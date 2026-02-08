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
}
