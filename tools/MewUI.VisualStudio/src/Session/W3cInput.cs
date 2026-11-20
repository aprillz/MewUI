using System.Windows.Input;

namespace Aprillz.MewUI.VisualStudio.Session
{
    /// <summary>
    /// Maps WPF input identifiers to the wire convention (W3C KeyboardEvent.code strings, W3C
    /// button numbers, ctrl/shift/alt/meta modifier bits) consumed by the preview session.
    /// </summary>
    internal static class W3cInput
    {
        /// <summary>Wire modifier bits: 1 ctrl, 2 shift, 4 alt, 8 meta (WPF bit order differs).</summary>
        internal static int Modifiers(ModifierKeys modifiers) =>
            ((modifiers & ModifierKeys.Control) != 0 ? 1 : 0)
            | ((modifiers & ModifierKeys.Shift) != 0 ? 2 : 0)
            | ((modifiers & ModifierKeys.Alt) != 0 ? 4 : 0)
            | ((modifiers & ModifierKeys.Windows) != 0 ? 8 : 0);

        /// <summary>W3C button number: 0 left, 1 middle, 2 right.</summary>
        internal static int Button(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Middle: return 1;
                case MouseButton.Right: return 2;
                default: return 0;
            }
        }

        /// <summary>W3C buttons bitmask after the event: 1 left, 2 right, 4 middle.</summary>
        internal static int Buttons(MouseEventArgs args) =>
            (args.LeftButton == MouseButtonState.Pressed ? 1 : 0)
            | (args.RightButton == MouseButtonState.Pressed ? 2 : 0)
            | (args.MiddleButton == MouseButtonState.Pressed ? 4 : 0);

        /// <summary>W3C KeyboardEvent.code value for keys without a mapping.</summary>
        internal const string UNIDENTIFIED_KEY_CODE = "Unidentified";

        /// <summary>
        /// W3C KeyboardEvent.code for a WPF key; null when unmapped. WPF reports virtual keys,
        /// which for the mapped set coincide with physical codes on standard layouts.
        /// </summary>
        internal static string KeyCode(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                return "Key" + (char)('A' + (key - Key.A));
            }
            if (key >= Key.D0 && key <= Key.D9)
            {
                return "Digit" + (char)('0' + (key - Key.D0));
            }
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return "Numpad" + (char)('0' + (key - Key.NumPad0));
            }
            if (key >= Key.F1 && key <= Key.F24)
            {
                return "F" + (1 + (key - Key.F1));
            }

            switch (key)
            {
                case Key.Back: return "Backspace";
                case Key.Tab: return "Tab";
                case Key.Enter: return "Enter";
                case Key.Escape: return "Escape";
                case Key.Space: return "Space";
                case Key.Left: return "ArrowLeft";
                case Key.Up: return "ArrowUp";
                case Key.Right: return "ArrowRight";
                case Key.Down: return "ArrowDown";
                case Key.Insert: return "Insert";
                case Key.Delete: return "Delete";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PageUp";
                case Key.PageDown: return "PageDown";
                case Key.Add: return "NumpadAdd";
                case Key.Subtract: return "NumpadSubtract";
                case Key.Multiply: return "NumpadMultiply";
                case Key.Divide: return "NumpadDivide";
                case Key.Decimal: return "NumpadDecimal";
                case Key.OemMinus: return "Minus";
                case Key.OemPlus: return "Equal";
                case Key.OemComma: return "Comma";
                case Key.OemPeriod: return "Period";
                case Key.OemQuestion: return "Slash";
                case Key.OemSemicolon: return "Semicolon";
                case Key.OemQuotes: return "Quote";
                case Key.OemOpenBrackets: return "BracketLeft";
                case Key.OemCloseBrackets: return "BracketRight";
                case Key.OemPipe: return "Backslash";
                case Key.OemTilde: return "Backquote";
                case Key.OemBackslash: return "IntlBackslash";
                case Key.Apps: return "ContextMenu";
                default: return null;
            }
        }
    }
}
