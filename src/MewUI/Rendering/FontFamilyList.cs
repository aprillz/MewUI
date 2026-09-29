namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Splits a comma-separated font family list into candidates; backends draw with the first
/// installed one and take the characters it lacks from the installed ones after it.
/// </summary>
internal static class FontFamilyList
{
    public static bool IsList(string family) => family.Contains(',');

    public static string[] Split(string family)
    {
        string[] candidates = family.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < candidates.Length; index++)
        {
            candidates[index] = candidates[index].Trim('\'', '"');
        }
        return candidates;
    }
}
