namespace Aprillz.MewUI.Input;

/// <summary>
/// Drops the text a keystroke produces once its KeyDown was handled, for platforms that deliver the key and
/// its text on separate messages. State lives per keystroke: the next KeyDown clears it.
/// </summary>
internal sealed class TextInputSuppression
{
    private bool _suppressed;

    /// <summary>Whether the current keystroke's text must be dropped.</summary>
    internal bool IsSuppressed => _suppressed;

    /// <summary>Starts a new keystroke; any stale suppression from a keystroke that produced no text ends here.</summary>
    internal void BeginKeyDown()
        => _suppressed = false;

    /// <summary>Marks the current keystroke's text for dropping; called when its PreviewKeyDown or KeyDown was handled.</summary>
    internal void SuppressKeystrokeText()
        => _suppressed = true;
}
