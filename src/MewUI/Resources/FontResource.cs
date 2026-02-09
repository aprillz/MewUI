namespace Aprillz.MewUI.Resources;

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
    /// A value suitable for assigning to <c>Control.FontFamily</c>.
    /// For stream-supplied fonts this is the cached font file path.
    /// </summary>
    public string FontFamily { get; }

    /// <summary>
    /// The cached font file path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The parsed font family name from the font file, when available.
    /// This does not imply the font is registered with any specific backend.
    /// </summary>
    public string ParsedFamilyName { get; }

    internal string Key { get; }

    /// <summary>
    /// Releases this handle and decrements the internal font cache reference count.
    /// </summary>
    public void Dispose() => FontResources.Release(Key);
}
