namespace Aprillz.MewUI.Controls;

public abstract class MenuEntry
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
    private string _text = string.Empty;
    private string? _cachedDisplayText;
    private char _cachedAccessKey;
    private int _cachedUnderlineIndex = -1;
    private KeyGesture? _shortcut;
    private string? _cachedShortcutDisplayText;

    public MenuItem() { }

    public MenuItem(string text) => Text = text ?? string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;
            _text = value;
            _cachedDisplayText = null;
        }
    }

    /// <summary>
    /// Returns cached access key parse results. Parsed once per Text change.
    /// </summary>
    internal (string displayText, char accessKey, int underlineIndex) GetParsedText()
    {
        if (_cachedDisplayText != null)
            return (_cachedDisplayText, _cachedAccessKey, _cachedUnderlineIndex);

        if (AccessKeyHelper.TryParse(_text, out var key, out var display))
        {
            _cachedAccessKey = key;
            _cachedUnderlineIndex = AccessKeyHelper.GetUnderlineIndex(_text);
        }
        else
        {
            display = _text;
            _cachedAccessKey = default;
            _cachedUnderlineIndex = -1;
        }

        _cachedDisplayText = display;
        return (_cachedDisplayText, _cachedAccessKey, _cachedUnderlineIndex);
    }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional predicate evaluated when the menu opens.
    /// When set, <see cref="IsEnabled"/> is updated automatically.
    /// </summary>
    public Func<bool>? CanClick { get; set; }

    /// <summary>
    /// Keyboard shortcut gesture. Auto-generates display text and registers with Window.KeyBindings.
    /// </summary>
    public KeyGesture? Shortcut
    {
        get => _shortcut;
        set
        {
            if (_shortcut == value) return;
            _shortcut = value;
            _cachedShortcutDisplayText = null;
        }
    }

    /// <summary>
    /// Returns the cached shortcut display string (e.g. "Ctrl+S"), or null if no shortcut is set.
    /// Computed once per <see cref="Shortcut"/> change.
    /// </summary>
    internal string? GetShortcutDisplayText()
    {
        if (_shortcut == null)
            return null;

        return _cachedShortcutDisplayText ??= _shortcut.Value.ToDisplayString();
    }

    public Action? Click { get; set; }

    public Menu? SubMenu { get; set; }

    /// <summary>
    /// Re-evaluates <see cref="CanClick"/> and updates <see cref="IsEnabled"/>.
    /// </summary>
    internal void ReevaluateCanClick()
    {
        if (CanClick != null)
            IsEnabled = CanClick();
    }

    public override string ToString() => Text;
}

public sealed class Menu
{
    private readonly List<MenuEntry> _items = new();

    public IList<MenuEntry> Items => _items;

    /// <summary>
    /// Optional per-menu item height override (in DIP). When NaN, the visual presenter chooses a theme-based default.
    /// </summary>
    public double ItemHeight { get; set; } = double.NaN;

    /// <summary>
    /// Optional per-menu item padding override. When null, the visual presenter chooses a theme-based default.
    /// </summary>
    public Thickness? ItemPadding { get; set; }
}
