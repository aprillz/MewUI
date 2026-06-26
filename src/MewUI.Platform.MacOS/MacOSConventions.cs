using System.Text;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// macOS UI conventions: Cmd as primary modifier, symbol-based gesture formatting,
/// no Alt access keys, and primary dialog button placed last (rightmost).
/// </summary>
internal sealed class MacOSConventions : PlatformConventions
{
    public override ModifierKeys PrimaryModifier => ModifierKeys.Meta;

    public override bool SupportsAccessKeys => false;

    public override bool ReverseButtonOrder => true;

    public override string FormatGesture(KeyGesture gesture)
    {
        var sb = new StringBuilder();
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control))
        {
            sb.Append('⌃'); // Control
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            sb.Append('⌥'); // Option
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            sb.Append('⇧'); // Shift
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Meta))
        {
            sb.Append('⌘'); // Command
        }

        sb.Append(FormatKey(gesture.Key));
        return sb.ToString();
    }

    protected override string FormatKey(Key key) => key switch
    {
        Key.Backspace => "⌫",
        Key.Delete => "⌦",
        Key.Enter => "↩",
        Key.Escape => "⎋",
        Key.Tab => "⇥",
        Key.Space => "␣",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Home => "↖",
        Key.End => "↘",
        Key.PageUp => "⇞",
        Key.PageDown => "⇟",
        _ => base.FormatKey(key),
    };
}
