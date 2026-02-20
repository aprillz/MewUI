namespace Aprillz.MewUI.Input;

/// <summary>
/// Small helper for platforms where key events and text input arrive on different messages/callbacks.
/// Used to prevent double delivery of Enter/Tab as both KeyDown-handled action and committed text input.
/// </summary>
internal sealed class TextInputSuppression
{
    [Flags]
    private enum SuppressNext
    {
        None = 0,
        Tab = 1 << 0,
        Enter = 1 << 1,
    }

    private SuppressNext _suppressNext;

    internal void ResetPerKeyDown()
        => _suppressNext = SuppressNext.None;

    internal void SuppressNextFromHandledKeyDown(Key key)
    {
        if (key == Key.Tab)
        {
            _suppressNext |= SuppressNext.Tab;
        }
        else if (key == Key.Enter)
        {
            _suppressNext |= SuppressNext.Enter;
        }
    }

    internal bool TryConsumeChar(char c)
    {
        if (c == '\t' && (_suppressNext & SuppressNext.Tab) != 0)
        {
            _suppressNext &= ~SuppressNext.Tab;
            return true;
        }

        if ((c == '\r' || c == '\n') && (_suppressNext & SuppressNext.Enter) != 0)
        {
            _suppressNext &= ~SuppressNext.Enter;
            return true;
        }

        return false;
    }

    internal static bool ShouldSuppressCommittedText(KeyEventArgs keyDownArgs, string text)
    {
        if (!keyDownArgs.Handled)
        {
            return false;
        }

        var normalized = TextInputEventArgs.NormalizeText(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        return keyDownArgs.Key switch
        {
            Key.Tab => normalized.IndexOf('\t') >= 0,
            Key.Enter => normalized.IndexOf('\n') >= 0,
            _ => false
        };
    }
}

