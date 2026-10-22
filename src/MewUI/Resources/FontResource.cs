namespace Aprillz.MewUI;

/// <summary>
/// Represents a font registered for use by the current process.
/// </summary>
public sealed class FontResource : IDisposable
{
    internal FontResource(string fontFamily, string filePath, string parsedFamilyName, string key)
    {
        FontFamily = fontFamily;
        FilePath = filePath;
        ParsedFamilyName = parsedFamilyName;
        Key = key;
    }

    /// <summary>
    /// Gets the value to assign to <see cref="Controls.TextElement.FontFamily"/>: the family name parsed from the
    /// font file when available, otherwise the name hint given at registration, otherwise the cached file path.
    /// </summary>
    public string FontFamily { get; }

    /// <summary>
    /// Gets the cached font file path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the family name parsed from the font file, or an empty string when unavailable.
    /// </summary>
    /// <remarks>
    /// This does not imply the font is registered with any backend.
    /// </remarks>
    public string ParsedFamilyName { get; }

    internal string Key { get; }

    /// <summary>
    /// Releases this handle and decrements the internal font cache reference count.
    /// </summary>
    public void Dispose() => FontResources.Release(Key);
}
