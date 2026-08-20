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
    public static Menu ItemPadding(this Menu menu, Thickness? itemPadding)
    {
        menu.ItemPadding = itemPadding;
        return menu;
    }

    /// <summary>
    /// Sets whether the menu bar draws a bottom separator.
    /// </summary>
    /// <param name="bar">Target menu bar.</param>
    /// <param name="value">Whether to draw the separator.</param>
    /// <returns>The menu bar for chaining.</returns>
    public static MenuBar DrawBottomSeparator(this MenuBar bar, bool value = true)
    {
        bar.DrawBottomSeparator = value;
        return bar;
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
    /// Adds a command item using the command's normalized text and default access key.
    /// </summary>
    public static MenuBar Item(this MenuBar bar, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        bar.Add(new MenuItem(command));
        return bar;
    }

    /// <summary>
    /// Adds a command item with a presentation and access-key override. A single underscore marks
    /// the following character as the access key; use a double underscore for a literal underscore.
    /// </summary>
    public static MenuBar Item(this MenuBar bar, string text, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        bar.Add(new MenuItem(text, command));
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
    /// Sets the menu item's presentation and access-key override. A single underscore marks the
    /// following character as the access key; use a double underscore for a literal underscore.
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
    /// Binds this placement's access-key-aware text override to an observable source.
    /// </summary>
    public static MenuItem BindText(this MenuItem item, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(MenuItem.TextProperty, source, BindingMode.OneWay);
        return item;
    }

    /// <summary>Sets the semantic command invoked by this item.</summary>
    public static MenuItem Command(this MenuItem item, Command? command)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Command = command;
        return item;
    }

    /// <summary>Binds the semantic command invoked by this item.</summary>
    public static MenuItem BindCommand(this MenuItem item, ObservableValue<Command?> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(MenuItem.CommandProperty, source, BindingMode.OneWay);
        return item;
    }

    /// <summary>Sets the menu item's icon presentation override.</summary>
    public static MenuItem Icon(this MenuItem item, IconTemplate? icon)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Icon = icon;
        return item;
    }

    /// <summary>Binds this placement's icon override to an observable source.</summary>
    public static MenuItem BindIcon(this MenuItem item, ObservableValue<IconTemplate?> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(MenuItem.IconProperty, source, BindingMode.OneWay);
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
    /// Sets whether the menu item is enabled.
    /// </summary>
    /// <param name="item">Target menu item.</param>
    /// <param name="value">Whether the item is enabled.</param>
    /// <returns>The menu item for chaining.</returns>
    public static MenuItem IsEnabled(this MenuItem item, bool value = true)
    {
        item.IsEnabled = value;
        return item;
    }

    /// <summary>
    /// Sets the predicate asked whether the row can be clicked. Asked again each time the menu opens.
    /// </summary>
    /// <param name="item">Target menu item.</param>
    /// <param name="predicate">Predicate, or null to ask nothing.</param>
    /// <returns>The menu item for chaining.</returns>
    public static MenuItem OnCanClick(this MenuItem item, Func<bool>? predicate)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.CanClick = predicate;
        return item;
    }

    /// <summary>
    /// Binds the local enabled value. Command CanExecute is combined with this value and does not
    /// replace the binding.
    /// </summary>
    public static MenuItem BindIsEnabled(this MenuItem item, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(MenuItem.IsEnabledProperty, source, BindingMode.OneWay);
        return item;
    }

    /// <summary>
    /// Sets the nested submenu.
    /// </summary>
    /// <param name="item">Target menu item.</param>
    /// <param name="value">Nested submenu.</param>
    /// <returns>The menu item for chaining.</returns>
    public static MenuItem SubMenu(this MenuItem item, Menu? value)
    {
        item.SubMenu = value;
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

    /// <summary>Adds a semantic command item using the command's normalized text and default access key.</summary>
    public static Menu Item(this Menu menu, Command command)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(command);
        menu.Items.Add(new MenuItem(command));
        return menu;
    }

    /// <summary>Adds a non-executable presentation item.</summary>
    public static Menu Item(this Menu menu, string text, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.Items.Add(new MenuItem(text) { IsEnabled = isEnabled });
        return menu;
    }

    /// <summary>
    /// Adds a semantic command item with a presentation and access-key override. A single
    /// underscore marks the following character as the access key; use a double underscore for a
    /// literal underscore.
    /// </summary>
    public static Menu Item(this Menu menu, string text, Command command)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(command);
        menu.Items.Add(new MenuItem(text, command));
        return menu;
    }

    /// <summary>
    /// Adds a submenu item.
    /// </summary>
    /// <param name="menu">Target menu.</param>
    /// <param name="text">Menu item text.</param>
    /// <param name="subMenu">Submenu.</param>
    /// <param name="isEnabled">Whether the item is enabled.</param>
    /// <returns>The menu for chaining.</returns>
    public static Menu SubMenu(this Menu menu, string text, Menu subMenu, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(subMenu);

        menu.Items.Add(new MenuItem
        {
            Text = text ?? string.Empty,
            IsEnabled = isEnabled,
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
