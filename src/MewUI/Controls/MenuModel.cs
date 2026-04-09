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
    public MenuItem() { }

    public MenuItem(string text) => Text = text ?? string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional predicate evaluated when the menu opens.
    /// When set, <see cref="IsEnabled"/> is updated automatically.
    /// </summary>
    public Func<bool>? CanClick { get; set; }

    /// <summary>
    /// Keyboard shortcut gesture. Auto-generates display text and registers with Window.KeyBindings.
    /// </summary>
    public KeyGesture? Shortcut { get; set; }

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
