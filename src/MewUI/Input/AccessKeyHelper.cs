namespace Aprillz.MewUI;

/// <summary>
/// Parses text with "_" access key markers (WPF convention).
/// "_File" → accessKey='F', displayText="File".
/// "__" escapes to a literal "_".
/// </summary>
internal static class AccessKeyHelper
{
    /// <summary>
    /// Parses text for an access key marker.
    /// </summary>
    /// <param name="text">Raw text with optional "_" prefix.</param>
    /// <param name="accessKey">The access key character if found.</param>
    /// <param name="displayText">Text with markers removed (__ → _).</param>
    /// <returns>True if an access key was found.</returns>
    public static bool TryParse(string text, out char accessKey, out string displayText)
    {
        accessKey = default;

        if (string.IsNullOrEmpty(text) || text.IndexOf('_') < 0)
        {
            displayText = text ?? string.Empty;
            return false;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        bool found = false;
        int underlineIndex = -1;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '_')
            {
                if (i + 1 < text.Length && text[i + 1] == '_')
                {
                    // __ → literal _
                    sb.Append('_');
                    i++; // skip second _
                }
                else if (i + 1 < text.Length && !found)
                {
                    // _ + char → access key
                    accessKey = text[i + 1];
                    underlineIndex = sb.Length;
                    found = true;
                    // don't append the _, the next char will be appended normally
                }
                else
                {
                    // trailing _ or already found → literal
                    sb.Append('_');
                }
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        displayText = sb.ToString();
        return found;
    }

    /// <summary>
    /// Returns the index in the display text where the access key underline should be drawn.
    /// Returns -1 if no access key is present.
    /// </summary>
    public static int GetUnderlineIndex(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('_') < 0)
            return -1;

        int displayIndex = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '_')
            {
                if (i + 1 < text.Length && text[i + 1] == '_')
                {
                    displayIndex++;
                    i++;
                }
                else if (i + 1 < text.Length)
                {
                    return displayIndex;
                }
                else
                {
                    displayIndex++;
                }
            }
            else
            {
                displayIndex++;
            }
        }

        return -1;
    }
}
