using System.Text;

namespace Aprillz.MewUI.Platform;

/// <summary>
/// Platform-specific UI conventions: the primary command modifier, gesture display formatting,
/// access-key support, and dialog button ordering.
/// The default instance uses Windows/Linux conventions. The macOS platform overrides this at registration time.
/// </summary>
public class PlatformConventions
{
    /// <summary>
    /// Gets or sets the current platform conventions.
    /// Defaults to Windows/Linux conventions. The macOS platform sets this during registration.
    /// </summary>
    public static PlatformConventions Current { get; set; } = new();

    /// <summary>
    /// The platform's primary command modifier: Ctrl on Windows/Linux, Cmd (Meta) on macOS.
    /// </summary>
    public virtual ModifierKeys PrimaryModifier => ModifierKeys.Control;

    /// <summary>
    /// Whether Alt-based access keys (mnemonics) are supported on this platform.
    /// False on macOS where Option is used for special character input.
    /// </summary>
    public virtual bool SupportsAccessKeys => true;

    /// <summary>
    /// Whether the dialog button row is laid out in reverse of the standard order.
    /// Standard order places the primary action first; false on Windows/Linux, true on macOS.
    /// </summary>
    public virtual bool ReverseButtonOrder => false;

    /// <summary>
    /// Formats a <see cref="KeyGesture"/> for display (e.g. "Ctrl+S").
    /// </summary>
    public virtual string FormatGesture(KeyGesture gesture)
    {
        var sb = new StringBuilder();
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (gesture.Modifiers.HasFlag(ModifierKeys.Meta)) sb.Append("Win+");
        sb.Append(FormatKey(gesture.Key));
        return sb.ToString();
    }

    /// <summary>
    /// Formats a single key for display.
    /// </summary>
    protected virtual string FormatKey(Key key) => key switch
    {
        Key.D0 => "0",
        Key.D1 => "1",
        Key.D2 => "2",
        Key.D3 => "3",
        Key.D4 => "4",
        Key.D5 => "5",
        Key.D6 => "6",
        Key.D7 => "7",
        Key.D8 => "8",
        Key.D9 => "9",
        Key.Add => "+",
        Key.Subtract => "-",
        Key.Multiply => "*",
        Key.Divide => "/",
        Key.Decimal => ".",
        Key.Backspace => "Backspace",
        Key.Delete => "Del",
        Key.Enter => "Enter",
        Key.Escape => "Esc",
        Key.Tab => "Tab",
        Key.Space => "Space",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        _ => key.ToString(),
    };
}
