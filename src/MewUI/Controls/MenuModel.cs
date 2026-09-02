using System.Collections;

namespace Aprillz.MewUI.Controls;

[Flags]
internal enum MenuModelChange
{
    None = 0,
    Structure = 1 << 0,
    Text = 1 << 1,
    Icon = 1 << 2,
    Enabled = 1 << 3,
    Command = 1 << 4,
    Shortcut = 1 << 5,
    SubMenu = 1 << 6,
    All = Structure | Text | Icon | Enabled | Command | Shortcut | SubMenu,
}

public abstract class MenuEntry : MewObject
{
    internal MenuEntry() { }
}

public sealed class MenuSeparator : MenuEntry
{
    public static readonly MenuSeparator Instance = new();

    private MenuSeparator() { }

    internal static double MenuSeparatorHeight => 3;
}

public sealed class MenuItem : MenuEntry
{
    public static readonly MewProperty<Command?> CommandProperty =
        MewProperty<Command?>.Register<MenuItem>(nameof(Command), null,
            MewPropertyOptions.None,
            static (self, oldValue, newValue) => self.OnCommandChanged(oldValue, newValue));

    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<MenuItem>(nameof(Text), string.Empty,
            MewPropertyOptions.None,
            static (self, _, _) => self.OnTextChanged());

    public static readonly MewProperty<IconTemplate?> IconProperty =
        MewProperty<IconTemplate?>.Register<MenuItem>(nameof(Icon), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.Changed?.Invoke(self, MenuModelChange.Icon));

    public static readonly MewProperty<bool> IsEnabledProperty =
        MewProperty<bool>.Register<MenuItem>(nameof(IsEnabled), true,
            MewPropertyOptions.None,
            static (self, _, _) => self.Changed?.Invoke(self, MenuModelChange.Enabled));

    public static readonly MewProperty<Menu?> SubMenuProperty =
        MewProperty<Menu?>.Register<MenuItem>(nameof(SubMenu), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.Changed?.Invoke(self, MenuModelChange.SubMenu));

    /// <summary>
    /// The value this placement hands its command as the invocation argument, so several items can
    /// share one command and differ only in what they pass. Null lets the argument the menu captured
    /// when it opened (the item it opened over) stand in.
    /// </summary>
    public static readonly MewProperty<object?> CommandDataProperty =
        MewProperty<object?>.Register<MenuItem>(nameof(CommandData), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.Changed?.Invoke(self, MenuModelChange.Command));

    private string? _cachedDisplayText;
    private char _cachedAccessKey;
    private int _cachedUnderlineIndex = -1;
    private bool _commandCanExecute = true;
    private bool _canClick = true;
    private string? _commandShortcutDisplayText;

    public MenuItem() { }

    /// <summary>
    /// Creates a presentation-only item. A single underscore in <paramref name="text"/> marks the
    /// following character as the access key; use a double underscore for a literal underscore.
    /// </summary>
    public MenuItem(string text) => Text = text ?? string.Empty;

    /// <summary>
    /// Creates an item using the command's current default presentation.
    /// </summary>
    public MenuItem(Command command) => Command = command ?? throw new ArgumentNullException(nameof(command));

    /// <summary>
    /// Creates a command item with a presentation and access-key override.
    /// </summary>
    public MenuItem(string text, Command command)
    {
        Text = text ?? string.Empty;
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>
    /// Creates a command item that passes <paramref name="data"/> as the invocation argument.
    /// </summary>
    public MenuItem(string text, Command command, object? data)
        : this(text, command)
    {
        CommandData = data;
    }

    /// <summary>
    /// Gets or sets the semantic command this item invokes.
    /// </summary>
    public Command? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <inheritdoc cref="CommandDataProperty"/>
    public object? CommandData
    {
        get => GetValue(CommandDataProperty);
        set => SetValue(CommandDataProperty, value);
    }

    /// <summary>
    /// Gets or sets this placement's presentation text override. A single underscore marks the
    /// following character as the access key. When this property has no value source, the command's
    /// default presentation supplies the text; an explicitly assigned empty string remains empty.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets this placement's icon override. When this property has no value source, the
    /// command's default icon is used; an explicitly assigned null suppresses that icon.
    /// </summary>
    public IconTemplate? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets the local enabled state. Command CanExecute is combined with this value without
    /// replacing it.
    /// </summary>
    public bool IsEnabled
    {
        get => GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    public Menu? SubMenu
    {
        get => GetValue(SubMenuProperty);
        set => SetValue(SubMenuProperty, value);
    }

    /// <summary>
    /// Asked whether the row can be clicked, for a row whose condition is local enough that a command
    /// would be ceremony. Combined with <see cref="IsEnabled"/> and with the command's own answer rather
    /// than replacing either, so any of the three disables the row.
    /// </summary>
    /// <remarks>
    /// A predicate carries no change signal. It is asked again each time the menu opens, which is the
    /// only moment a row is about to be read; a condition that changes while the menu is up must be
    /// expressed as a binding to <see cref="IsEnabled"/> instead.
    /// </remarks>
    public Func<bool>? CanClick
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                ReevaluateCanClick();
            }
        }
    }

    internal event Action<MenuItem, MenuModelChange>? Changed;

    internal bool IsEffectivelyEnabled => IsEnabled && _commandCanExecute && _canClick;

    /// <summary>
    /// Asks <see cref="CanClick"/> again and reports whether the answer moved. The result is kept rather
    /// than asked per read: the drawing and hit-testing paths read it many times per frame.
    /// </summary>
    internal bool ReevaluateCanClick()
    {
        bool value = CanClick?.Invoke() ?? true;
        if (_canClick == value)
        {
            return false;
        }

        _canClick = value;
        Changed?.Invoke(this, MenuModelChange.Enabled);
        return true;
    }

    internal IconTemplate? ResolveIconTemplate()
        => HasExplicitValue(IconProperty) ? Icon : Command?.Presentation.Icon;

    /// <summary>
    /// Returns cached access-key presentation, using a local item value before the command default.
    /// </summary>
    internal (string displayText, char accessKey, int underlineIndex) GetParsedText()
    {
        if (_cachedDisplayText != null)
        {
            return (_cachedDisplayText, _cachedAccessKey, _cachedUnderlineIndex);
        }

        if (!HasExplicitValue(TextProperty) && Command is Command command)
        {
            var presentation = command.Presentation;
            _cachedDisplayText = presentation.DisplayText ?? string.Empty;
            _cachedAccessKey = presentation.AccessKey;
            _cachedUnderlineIndex = presentation.AccessKeyIndex;
            return (_cachedDisplayText, _cachedAccessKey, _cachedUnderlineIndex);
        }

        var rawText = Text;
        bool hasAccessKey = AccessKeyHelper.TryParse(rawText, out var key, out var display);
        _cachedAccessKey = hasAccessKey ? key : default;
        _cachedUnderlineIndex = hasAccessKey ? AccessKeyHelper.GetUnderlineIndex(rawText) : -1;

        _cachedDisplayText = display;
        return (_cachedDisplayText, _cachedAccessKey, _cachedUnderlineIndex);
    }

    internal string? GetShortcutDisplayText() => _commandShortcutDisplayText;

    internal bool ApplyCommandState(bool canExecute, string? shortcutDisplayText)
    {
        bool enabledChanged = _commandCanExecute != canExecute;
        bool shortcutChanged = _commandShortcutDisplayText != shortcutDisplayText;
        if (!enabledChanged && !shortcutChanged)
        {
            return false;
        }

        _commandCanExecute = canExecute;
        _commandShortcutDisplayText = shortcutDisplayText;
        var change = (enabledChanged ? MenuModelChange.Enabled : MenuModelChange.None) |
            (shortcutChanged ? MenuModelChange.Shortcut : MenuModelChange.None);
        Changed?.Invoke(this, change);
        return true;
    }

    public override string ToString() => GetParsedText().displayText;

    private bool HasExplicitValue(MewProperty property)
        => GetPropertyValueTrace(property).EffectiveSource != ValueSource.Default;

    private void OnTextChanged()
    {
        ClearParsedText();
        Changed?.Invoke(this, MenuModelChange.Text);
    }

    private void OnCommandChanged(Command? oldCommand, Command? newCommand)
    {
        if (oldCommand != null)
        {
            WeakEventManager.RemoveHandler(
                CommandPresentationWeakEvents.Invalidated,
                oldCommand.Presentation,
                this);
        }

        _commandCanExecute = true;
        _commandShortcutDisplayText = null;
        ClearParsedText();

        if (newCommand != null)
        {
            WeakEventManager.AddHandler(
                CommandPresentationWeakEvents.Invalidated,
                newCommand.Presentation,
                this,
                static item => item.OnCommandPresentationChanged());
        }

        Changed?.Invoke(this,
            MenuModelChange.Command | MenuModelChange.Text | MenuModelChange.Icon |
            MenuModelChange.Enabled | MenuModelChange.Shortcut);
    }

    private void OnCommandPresentationChanged()
    {
        ClearParsedText();
        Changed?.Invoke(this, MenuModelChange.Text | MenuModelChange.Icon);
    }

    private void ClearParsedText()
    {
        _cachedDisplayText = null;
        _cachedAccessKey = default;
        _cachedUnderlineIndex = -1;
    }
}

internal sealed class MenuEntryCollection : IList<MenuEntry>
{
    private readonly List<MenuEntry> _items = [];

    internal event Action<MenuModelChange>? Changed;

    public MenuEntry this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var old = _items[index];
            if (ReferenceEquals(old, value)) return;
            Unsubscribe(old);
            _items[index] = value;
            Subscribe(value);
            Changed?.Invoke(MenuModelChange.All);
        }
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public void Add(MenuEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        Subscribe(item);
        Changed?.Invoke(MenuModelChange.Structure);
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        foreach (var item in _items) Unsubscribe(item);
        _items.Clear();
        Changed?.Invoke(MenuModelChange.All);
    }

    public bool Contains(MenuEntry item) => _items.Contains(item);
    public void CopyTo(MenuEntry[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<MenuEntry> GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(MenuEntry item) => _items.IndexOf(item);

    public void Insert(int index, MenuEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        Subscribe(item);
        Changed?.Invoke(MenuModelChange.Structure);
    }

    public bool Remove(MenuEntry item)
    {
        if (!_items.Remove(item)) return false;
        Unsubscribe(item);
        Changed?.Invoke(MenuModelChange.All);
        return true;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        Unsubscribe(item);
        Changed?.Invoke(MenuModelChange.All);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Subscribe(MenuEntry entry)
    {
        if (entry is MenuItem item) item.Changed += OnItemChanged;
    }

    private void Unsubscribe(MenuEntry entry)
    {
        if (entry is MenuItem item) item.Changed -= OnItemChanged;
    }

    private void OnItemChanged(MenuItem _, MenuModelChange change) => Changed?.Invoke(change);
}

internal sealed class MenuBarItemCollection : IList<MenuItem>
{
    private readonly List<MenuItem> _items = [];

    internal event Action<MenuModelChange>? Changed;

    public MenuItem this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var old = _items[index];
            if (ReferenceEquals(old, value)) return;
            old.Changed -= OnItemChanged;
            _items[index] = value;
            value.Changed += OnItemChanged;
            Changed?.Invoke(MenuModelChange.All);
        }
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public void Add(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(MenuModelChange.Structure);
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        foreach (var item in _items) item.Changed -= OnItemChanged;
        _items.Clear();
        Changed?.Invoke(MenuModelChange.All);
    }

    public bool Contains(MenuItem item) => _items.Contains(item);
    public void CopyTo(MenuItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<MenuItem> GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(MenuItem item) => _items.IndexOf(item);

    public void Insert(int index, MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(MenuModelChange.Structure);
    }

    public bool Remove(MenuItem item)
    {
        if (!_items.Remove(item)) return false;
        item.Changed -= OnItemChanged;
        Changed?.Invoke(MenuModelChange.All);
        return true;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        item.Changed -= OnItemChanged;
        Changed?.Invoke(MenuModelChange.All);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void OnItemChanged(MenuItem _, MenuModelChange change) => Changed?.Invoke(change);
}

public sealed class Menu
{
    private readonly MenuEntryCollection _items = new();

    public IList<MenuEntry> Items => _items;

    internal event Action<MenuModelChange> Changed
    {
        add => _items.Changed += value;
        remove => _items.Changed -= value;
    }

    /// <summary>
    /// Optional per-menu item height override (in DIP). When NaN, the presenter uses its default.
    /// </summary>
    public double ItemHeight { get; set; } = double.NaN;

    /// <summary>
    /// Optional per-menu item padding override. When null, the presenter uses its default.
    /// </summary>
    public Thickness? ItemPadding { get; set; }
}
