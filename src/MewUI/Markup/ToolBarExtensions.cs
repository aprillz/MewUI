using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extensions for toolbars.
/// </summary>
public static class ToolBarExtensions
{
    /// <summary>
    /// Sets the bands, top to bottom.
    /// </summary>
    /// <param name="bar">Target toolbar.</param>
    /// <param name="bands">Bands to show.</param>
    /// <returns>The toolbar for chaining.</returns>
    public static ToolBar Bands(this ToolBar bar, params ToolBarBand[] bands)
    {
        ArgumentNullException.ThrowIfNull(bar);
        bar.Bands.Clear();
        foreach (var band in bands)
        {
            bar.Bands.Add(band);
        }

        return bar;
    }

    /// <summary>
    /// Adds a band holding the given groups.
    /// </summary>
    /// <param name="bar">Target toolbar.</param>
    /// <param name="groups">Groups on the new band, left to right.</param>
    /// <returns>The toolbar for chaining.</returns>
    public static ToolBar Band(this ToolBar bar, params ToolBarGroup[] groups)
    {
        ArgumentNullException.ThrowIfNull(bar);
        bar.Bands.Add(new ToolBarBand(groups));
        return bar;
    }

    /// <summary>
    /// Sets how entries show their commands unless an entry overrides it.
    /// </summary>
    /// <param name="bar">Target toolbar.</param>
    /// <param name="presentation">Presentation mode.</param>
    /// <returns>The toolbar for chaining.</returns>
    public static ToolBar ItemPresentation(this ToolBar bar, CommandPresentationMode presentation)
    {
        ArgumentNullException.ThrowIfNull(bar);
        bar.ItemPresentation = presentation;
        return bar;
    }

    /// <summary>
    /// Sets whether groups may be dragged into a different place.
    /// </summary>
    /// <param name="bar">Target toolbar.</param>
    /// <param name="value">Whether reordering is allowed.</param>
    /// <returns>The toolbar for chaining.</returns>
    public static ToolBar CanReorderGroups(this ToolBar bar, bool value = true)
    {
        ArgumentNullException.ThrowIfNull(bar);
        bar.CanReorderGroups = value;
        return bar;
    }

    /// <summary>
    /// Adds a group holding the given entries.
    /// </summary>
    /// <param name="band">Target band.</param>
    /// <param name="entries">Entries in the new group, left to right.</param>
    /// <returns>The band for chaining.</returns>
    public static ToolBarBand Group(this ToolBarBand band, params ToolBarEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(band);
        band.Groups.Add(new ToolBarGroup(entries));
        return band;
    }

    /// <summary>
    /// Adds an entry that runs a command.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <param name="command">Command the entry runs.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Item(this ToolBarGroup group, Command command)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarItem(command));
        return group;
    }

    /// <summary>
    /// Adds an entry that stays visibly pressed while its state is on.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <param name="command">Command the entry runs.</param>
    /// <param name="isChecked">Whether the entry starts on.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Toggle(this ToolBarGroup group, Command command, bool isChecked = false)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarToggleItem(command) { IsChecked = isChecked });
        return group;
    }

    /// <summary>
    /// Adds an entry that runs a command and opens a menu from its chevron.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <param name="command">Primary command.</param>
    /// <param name="menu">Menu the chevron opens.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Split(this ToolBarGroup group, Command command, Menu menu)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarSplitItem(command) { DropDownMenu = menu });
        return group;
    }

    /// <summary>
    /// Adds an entry with no primary action that opens a menu.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <param name="text">Text shown on the entry.</param>
    /// <param name="menu">Menu the entry opens.</param>
    /// <param name="icon">Icon shown on the entry.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Menu(this ToolBarGroup group, string text, Menu menu, IconTemplate? icon = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarMenuItem { Text = text ?? string.Empty, DropDownMenu = menu, Icon = icon });
        return group;
    }

    /// <summary>
    /// Adds static text annotating the entry beside it.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <param name="text">Text to show.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Label(this ToolBarGroup group, string text)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarLabelItem(text));
        return group;
    }

    /// <summary>
    /// Adds a rule dividing the entries before it from the ones after.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Splitter(this ToolBarGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarSplitter());
        return group;
    }

    /// <summary>
    /// Adds an entry hosting an arbitrary element.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <param name="content">Element to host.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Host(this ToolBarGroup group, Element content)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarHost(content));
        return group;
    }

    /// <summary>
    /// Sets how this entry shows its command, overriding the toolbar's own presentation.
    /// </summary>
    /// <param name="item">Target entry.</param>
    /// <param name="presentation">Presentation mode.</param>
    /// <returns>The entry for chaining.</returns>
    public static T Presentation<T>(this T item, CommandPresentationMode presentation)
        where T : ToolBarItem
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Presentation = presentation;
        return item;
    }

    /// <summary>
    /// Sets the menu the entry's chevron opens.
    /// </summary>
    /// <param name="item">Target entry.</param>
    /// <param name="menu">Menu to open.</param>
    /// <returns>The entry for chaining.</returns>
    public static ToolBarSplitItem DropDownMenu(this ToolBarSplitItem item, Menu menu)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.DropDownMenu = menu;
        return item;
    }

    /// <summary>
    /// Sets the menu the entry opens.
    /// </summary>
    /// <param name="item">Target entry.</param>
    /// <param name="menu">Menu to open.</param>
    /// <returns>The entry for chaining.</returns>
    public static ToolBarMenuItem DropDownMenu(this ToolBarMenuItem item, Menu menu)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.DropDownMenu = menu;
        return item;
    }

    /// <summary>
    /// Sets whether the entry reads as on.
    /// </summary>
    /// <param name="item">Target entry.</param>
    /// <param name="value">Whether the entry is on.</param>
    /// <returns>The entry for chaining.</returns>
    public static ToolBarToggleItem IsChecked(this ToolBarToggleItem item, bool value = true)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.IsChecked = value;
        return item;
    }
}
