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
    /// Sets what entries build a tooltip from when they supply none of their own.
    /// </summary>
    /// <param name="bar">Target toolbar.</param>
    /// <param name="mode">Parts of the command to build from.</param>
    /// <returns>The toolbar for chaining.</returns>
    public static ToolBar ItemToolTipMode(this ToolBar bar, CommandToolTipMode mode)
    {
        ArgumentNullException.ThrowIfNull(bar);
        bar.ItemToolTipMode = mode;
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
    public static ToolBarGroup Separator(this ToolBarGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Items.Add(new ToolBarSeparator());
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
    /// Adds the given elements as entries, each hosted as it is. For a toolbar built from controls rather
    /// than from commands: the elements keep their own content, handlers and bindings, and the band
    /// collapses them into its overflow popup like any other entry.
    /// </summary>
    /// <param name="group">Target group.</param>
    /// <param name="elements">Elements to host, left to right.</param>
    /// <returns>The group for chaining.</returns>
    public static ToolBarGroup Items(this ToolBarGroup group, params Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(elements);

        foreach (var element in elements)
        {
            ApplyToolBarLook(element);
            group.Items.Add(new ToolBarHost(element));
        }

        return group;
    }

    /// <summary>
    /// Names the toolbar style for the kinds of control a toolbar has a look for. Only <see cref="Items"/>
    /// does this: it is the entry point an application chooses when it wants a toolbar built from
    /// controls, so what goes through it should look like one. A control that already names a style keeps
    /// it, and <see cref="Host"/> leaves everything alone.
    /// </summary>
    private static void ApplyToolBarLook(Element element)
    {
        if (element is not Control control || control.StyleName != null)
        {
            return;
        }

        control.StyleName = control switch
        {
            Controls.ToggleButton => BuiltInStyles.ToolBarToggleButton,
            Controls.Button => BuiltInStyles.ToolBarButton,
            Controls.Label => BuiltInStyles.ToolBarLabel,
            _ => null,
        };
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

    /// <summary>Binds the text this label entry shows.</summary>
    public static ToolBarLabelItem BindText(this ToolBarLabelItem item, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(ToolBarLabelItem.TextProperty, source, BindingMode.OneWay);
        return item;
    }

    /// <summary>Binds the text this menu entry shows.</summary>
    public static ToolBarMenuItem BindText(this ToolBarMenuItem item, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(ToolBarMenuItem.TextProperty, source, BindingMode.OneWay);
        return item;
    }

    /// <summary>Binds the icon this menu entry shows.</summary>
    public static ToolBarMenuItem BindIcon(this ToolBarMenuItem item, ObservableValue<IconTemplate?> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(ToolBarMenuItem.IconProperty, source, BindingMode.OneWay);
        return item;
    }

    /// <summary>Binds the command this entry runs.</summary>
    public static T BindCommand<T>(this T item, ObservableValue<Command?> source)
        where T : ToolBarItem
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(ToolBarItem.CommandProperty, source, BindingMode.OneWay);
        return item;
    }

    /// <summary>
    /// Binds whether this entry reads as on. Two-way: the toolbar writes the entry back when the button
    /// it made is pressed, so the bound value follows the user as well as the application.
    /// </summary>
    public static ToolBarToggleItem BindIsChecked(this ToolBarToggleItem item, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        item.SetBinding(ToolBarToggleItem.IsCheckedProperty, source, BindingMode.TwoWay);
        return item;
    }

    /// <summary>
    /// Sets what this entry shows when the pointer rests on it, instead of what its command would say.
    /// </summary>
    /// <param name="entry">Target entry.</param>
    /// <param name="text">Tooltip text, or null to go back to the command.</param>
    /// <returns>The entry for chaining.</returns>
    public static T ToolTip<T>(this T entry, string? text)
        where T : ToolBarEntry
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.ToolTip = string.IsNullOrEmpty(text) ? null : new TextBlock { Text = text };
        return entry;
    }

    /// <summary>
    /// Sets what this entry shows when the pointer rests on it, instead of what its command would say.
    /// </summary>
    /// <param name="entry">Target entry.</param>
    /// <param name="content">Tooltip content, or null to go back to the command.</param>
    /// <returns>The entry for chaining.</returns>
    public static T ToolTip<T>(this T entry, Element? content)
        where T : ToolBarEntry
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.ToolTip = content;
        return entry;
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
