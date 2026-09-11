namespace Aprillz.MewUI.Text;

/// <summary>Configures the presentation of inline text markup.</summary>
public sealed class TextMarkupOptions
{
    /// <summary>Gets the default markup options.</summary>
    public static TextMarkupOptions Default { get; } = new();

    /// <summary>Initializes markup options with the font used by code spans.</summary>
    public TextMarkupOptions(string monospaceFontFamily = "Consolas")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monospaceFontFamily);
        MonospaceFontFamily = monospaceFontFamily;
    }

    /// <summary>Gets the font family used by <c>code</c> and <c>tt</c> spans.</summary>
    public string MonospaceFontFamily { get; }
}
