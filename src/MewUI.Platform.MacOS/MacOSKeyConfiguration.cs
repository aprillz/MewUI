using System.Text;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// macOS keyboard configuration: Cmd as primary modifier, symbol-based gesture formatting (⌘⇧⌥⌃).
/// </summary>
internal sealed class MacOSKeyConfiguration : PlatformKeyConfiguration
{
    public override ModifierKeys PrimaryModifier => ModifierKeys.Meta;

    public override bool SupportsAccessKeys => false;

    public override string FormatGesture(KeyGesture gesture)
    {
        var sb = new StringBuilder();
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control))
        {
            sb.Append('\u2303'); // ⌃
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            sb.Append('\u2325');     // ⌥
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            sb.Append('\u21e7');   // ⇧
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Meta))
        {
            sb.Append('\u2318');    // ⌘
        }

        sb.Append(FormatKey(gesture.Key));
        return sb.ToString();
    }

    protected override string FormatKey(Key key) => key switch
    {
        Key.Backspace => "\u232b",  // ⌫
        Key.Delete => "\u2326",     // ⌦
        Key.Enter => "\u21a9",      // ↩
        Key.Escape => "\u238b",     // ⎋
        Key.Tab => "\u21e5",        // ⇥
        Key.Space => "\u2423",      // ␣
        Key.Left => "\u2190",       // ←
        Key.Right => "\u2192",      // →
        Key.Up => "\u2191",         // ↑
        Key.Down => "\u2193",       // ↓
        Key.Home => "\u2196",       // ↖
        Key.End => "\u2198",        // ↘
        Key.PageUp => "\u21de",     // ⇞
        Key.PageDown => "\u21df",   // ⇟
        _ => base.FormatKey(key),
    };
}
