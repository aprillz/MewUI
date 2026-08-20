using System.Collections;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// An entry in a <see cref="ToolBarGroup"/>. Entries are models: the toolbar materializes a control for
/// each one, and hands the ones a band cannot fit to that band's overflow popup, which shows the very
/// controls it made rather than rebuilding them.
/// </summary>
public abstract class ToolBarEntry : MewObject
{
    /// <summary>
    /// What to show when the pointer rests on this entry. Content, not a policy: when it is left empty the
    /// entry builds one from its command according to <see cref="ToolBar.ItemToolTipMode"/>.
    /// </summary>
    public static readonly MewProperty<Element?> ToolTipProperty =
        MewProperty<Element?>.Register<ToolBarEntry>(nameof(ToolTip), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    internal ToolBarEntry() { }

    /// <inheritdoc cref="ToolTipProperty"/>
    public Element? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    internal ToolBarGroup? Owner { get; set; }

    private protected void NotifyChanged() => Owner?.NotifyChanged();
}

/// <summary>Static text that annotates the entry beside it.</summary>
public sealed class ToolBarLabelItem : ToolBarEntry
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<ToolBarLabelItem>(nameof(Text), string.Empty,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    public ToolBarLabelItem() { }

    public ToolBarLabelItem(string text) => Text = text ?? string.Empty;

    /// <summary>Gets or sets the displayed text.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>
/// A rule between two runs of entries in one group. Groups state which entries belong together, so a
/// separator divides a group that travels as one rather than standing between groups.
/// </summary>
public sealed class ToolBarSeparator : ToolBarEntry
{
}

/// <summary>An entry that runs a command.</summary>
public class ToolBarItem : ToolBarEntry
{
    public static readonly MewProperty<Command?> CommandProperty =
        MewProperty<Command?>.Register<ToolBarItem>(nameof(Command), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    /// <summary>
    /// How this entry shows its command, or null to follow <see cref="ToolBar.ItemPresentation"/>.
    /// </summary>
    public static readonly MewProperty<CommandPresentationMode?> PresentationProperty =
        MewProperty<CommandPresentationMode?>.Register<ToolBarItem>(nameof(Presentation), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    public ToolBarItem() { }

    public ToolBarItem(Command command) => Command = command ?? throw new ArgumentNullException(nameof(command));

    /// <summary>Gets or sets the command this entry runs.</summary>
    public Command? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <inheritdoc cref="PresentationProperty"/>
    public CommandPresentationMode? Presentation
    {
        get => GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }
}

/// <summary>An entry that runs a command and stays visibly pressed while its state is on.</summary>
public sealed class ToolBarToggleItem : ToolBarItem
{
    public static readonly MewProperty<bool> IsCheckedProperty =
        MewProperty<bool>.Register<ToolBarToggleItem>(nameof(IsChecked), false,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    public ToolBarToggleItem() { }

    public ToolBarToggleItem(Command command) : base(command) { }

    /// <summary>Gets or sets whether the entry reads as on.</summary>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }
}

/// <summary>An entry that runs a command, with a chevron that opens a menu of related commands.</summary>
public sealed class ToolBarSplitItem : ToolBarItem
{
    public static readonly MewProperty<Menu?> DropDownMenuProperty =
        MewProperty<Menu?>.Register<ToolBarSplitItem>(nameof(DropDownMenu), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    public ToolBarSplitItem() { }

    public ToolBarSplitItem(Command command) : base(command) { }

    /// <summary>Gets or sets the menu the chevron opens.</summary>
    public Menu? DropDownMenu
    {
        get => GetValue(DropDownMenuProperty);
        set => SetValue(DropDownMenuProperty, value);
    }
}

/// <summary>An entry with no primary action: every part of it opens a menu.</summary>
public sealed class ToolBarMenuItem : ToolBarEntry
{
    public static readonly MewProperty<Menu?> DropDownMenuProperty =
        MewProperty<Menu?>.Register<ToolBarMenuItem>(nameof(DropDownMenu), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    public static readonly MewProperty<IconTemplate?> IconProperty =
        MewProperty<IconTemplate?>.Register<ToolBarMenuItem>(nameof(Icon), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<ToolBarMenuItem>(nameof(Text), string.Empty,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    /// <summary>Gets or sets the menu this entry opens.</summary>
    public Menu? DropDownMenu
    {
        get => GetValue(DropDownMenuProperty);
        set => SetValue(DropDownMenuProperty, value);
    }

    /// <summary>Gets or sets the icon shown on the entry.</summary>
    public IconTemplate? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Gets or sets the text shown on the entry.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>
/// An entry that hosts an arbitrary element. The overflow popup shows the element itself, so a hosted
/// control collapses like any other entry and keeps what it holds while it is there.
/// </summary>
public sealed class ToolBarHost : ToolBarEntry
{
    public static readonly MewProperty<Element?> ContentProperty =
        MewProperty<Element?>.Register<ToolBarHost>(nameof(Content), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.NotifyChanged());

    public ToolBarHost() { }

    public ToolBarHost(Element content) => Content = content;

    /// <summary>Gets or sets the hosted element.</summary>
    public Element? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}

/// <summary>
/// A run of entries that travels as one. A group is what a drag moves and what the plate is drawn
/// around, so the entries that belong together are stated by containment rather than by separators the
/// framework would have to keep in step with the order.
/// </summary>
public sealed class ToolBarGroup
{
    private readonly List<ToolBarEntry> _items = new();

    public ToolBarGroup() { }

    public ToolBarGroup(params ToolBarEntry[] items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    internal ToolBarBand? Owner { get; set; }

    /// <summary>Gets the entries, in the order they appear.</summary>
    public IList<ToolBarEntry> Items => _view ??= new EntryCollection(this);

    private EntryCollection? _view;

    internal IReadOnlyList<ToolBarEntry> ItemsInternal => _items;

    internal void NotifyChanged() => Owner?.NotifyChanged();

    private sealed class EntryCollection(ToolBarGroup owner) : IList<ToolBarEntry>
    {
        private List<ToolBarEntry> Items => owner._items;

        public ToolBarEntry this[int index]
        {
            get => Items[index];
            set
            {
                Attach(value);
                Items[index] = value;
                owner.NotifyChanged();
            }
        }

        public int Count => Items.Count;

        public bool IsReadOnly => false;

        private void Attach(ToolBarEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Owner = owner;
        }

        public void Add(ToolBarEntry item)
        {
            Attach(item);
            Items.Add(item);
            owner.NotifyChanged();
        }

        public void Clear()
        {
            Items.Clear();
            owner.NotifyChanged();
        }

        public bool Contains(ToolBarEntry item) => Items.Contains(item);

        public void CopyTo(ToolBarEntry[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);

        public IEnumerator<ToolBarEntry> GetEnumerator() => Items.GetEnumerator();

        public int IndexOf(ToolBarEntry item) => Items.IndexOf(item);

        public void Insert(int index, ToolBarEntry item)
        {
            Attach(item);
            Items.Insert(index, item);
            owner.NotifyChanged();
        }

        public bool Remove(ToolBarEntry item)
        {
            bool removed = Items.Remove(item);
            if (removed)
            {
                owner.NotifyChanged();
            }

            return removed;
        }

        public void RemoveAt(int index)
        {
            Items.RemoveAt(index);
            owner.NotifyChanged();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>
/// One row of a toolbar. Bands are explicit, so a row never appears or disappears because the width
/// changed: a band that runs out of room hides its trailing groups behind its own overflow button instead.
/// </summary>
public sealed class ToolBarBand
{
    private readonly List<ToolBarGroup> _groups = new();

    public ToolBarBand() { }

    public ToolBarBand(params ToolBarGroup[] groups)
    {
        foreach (var group in groups)
        {
            Groups.Add(group);
        }
    }

    internal ToolBar? Owner { get; set; }

    /// <summary>Gets the groups, left to right.</summary>
    public IList<ToolBarGroup> Groups => _view ??= new GroupCollection(this);

    private GroupCollection? _view;

    internal IReadOnlyList<ToolBarGroup> GroupsInternal => _groups;

    internal void NotifyChanged() => Owner?.OnBandsChanged();

    private sealed class GroupCollection(ToolBarBand owner) : IList<ToolBarGroup>
    {
        private List<ToolBarGroup> Groups => owner._groups;

        public ToolBarGroup this[int index]
        {
            get => Groups[index];
            set
            {
                Attach(value);
                Groups[index] = value;
                owner.NotifyChanged();
            }
        }

        public int Count => Groups.Count;

        public bool IsReadOnly => false;

        private void Attach(ToolBarGroup group)
        {
            ArgumentNullException.ThrowIfNull(group);
            group.Owner = owner;
        }

        public void Add(ToolBarGroup item)
        {
            Attach(item);
            Groups.Add(item);
            owner.NotifyChanged();
        }

        public void Clear()
        {
            Groups.Clear();
            owner.NotifyChanged();
        }

        public bool Contains(ToolBarGroup item) => Groups.Contains(item);

        public void CopyTo(ToolBarGroup[] array, int arrayIndex) => Groups.CopyTo(array, arrayIndex);

        public IEnumerator<ToolBarGroup> GetEnumerator() => Groups.GetEnumerator();

        public int IndexOf(ToolBarGroup item) => Groups.IndexOf(item);

        public void Insert(int index, ToolBarGroup item)
        {
            Attach(item);
            Groups.Insert(index, item);
            owner.NotifyChanged();
        }

        public bool Remove(ToolBarGroup item)
        {
            bool removed = Groups.Remove(item);
            if (removed)
            {
                owner.NotifyChanged();
            }

            return removed;
        }

        public void RemoveAt(int index)
        {
            Groups.RemoveAt(index);
            owner.NotifyChanged();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
