using System.Text;

namespace Aprillz.MewUI;

/// <summary>
/// Platform-specific keyboard configuration: primary modifier key and gesture display formatting.
/// The default instance uses Windows/Linux conventions (Ctrl, "Ctrl+S" format).
/// macOS platform overrides this at registration time.
/// </summary>
public class PlatformKeyConfiguration
{
    /// <summary>
    /// Gets or sets the current platform configuration.
    /// Defaults to Windows/Linux conventions. macOS platform sets this during registration.
    /// </summary>
    public static PlatformKeyConfiguration Current { get; set; } = new();

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
