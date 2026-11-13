using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Resolves a letter key pressed in a menu: the item with that access key where the platform has access
/// keys, otherwise the first item whose title starts with the letters typed so far.
/// </summary>
internal sealed class MenuLetterKeys
{
    // How long typed letters keep adding to the title search, as Qt keeps its menu search buffer.
    private const long TYPE_SELECT_RESET_MS = 2000;

    private string _typed = string.Empty;
    private long _lastTypedAt;

    /// <summary>
    /// Finds the item a letter key selects. <paramref name="activate"/> is true when the key names exactly
    /// one access key; a letter several items share moves to the next of them after
    /// <paramref name="hotIndex"/> without activating.
    /// </summary>
    internal bool TryFind<TEntry>(IList<TEntry> entries, Key key, int hotIndex, out int index, out bool activate)
        where TEntry : MenuEntry
    {
        index = -1;
        activate = false;

        char letter = LetterOf(key);
        if (letter == default)
        {
            return false;
        }

        if (PlatformConventions.Current.SupportsAccessKeys)
        {
            return TryFindAccessKey(entries, letter, hotIndex, out index, out activate);
        }
        else
        {
            return TryFindTitle(entries, letter, out index);
        }
    }

    private static bool TryFindAccessKey<TEntry>(IList<TEntry> entries, char letter, int hotIndex, out int index, out bool activate)
        where TEntry : MenuEntry
    {
        int first = -1;
        int afterHot = -1;
        int matches = 0;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            if (entries[entryIndex] is not MenuItem item || !item.IsEffectivelyEnabled)
            {
                continue;
            }

            char accessKey = item.GetParsedText().accessKey;
            if (accessKey == default || char.ToUpperInvariant(accessKey) != letter)
            {
                continue;
            }

            matches++;
            if (first < 0)
            {
                first = entryIndex;
            }

            if (afterHot < 0 && entryIndex > hotIndex)
            {
                afterHot = entryIndex;
            }
        }

        activate = matches == 1;
        index = matches == 1 || afterHot < 0 ? first : afterHot;
        return matches > 0;
    }

    private bool TryFindTitle<TEntry>(IList<TEntry> entries, char letter, out int index)
        where TEntry : MenuEntry
    {
        long now = Environment.TickCount64;
        if (now - _lastTypedAt > TYPE_SELECT_RESET_MS)
        {
            _typed = string.Empty;
        }

        _lastTypedAt = now;
        _typed += letter;

        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            if (entries[entryIndex] is MenuItem item && item.IsEffectivelyEnabled &&
                item.GetParsedText().displayText.StartsWith(_typed, StringComparison.OrdinalIgnoreCase))
            {
                index = entryIndex;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static char LetterOf(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        _ => default,
    };
}
