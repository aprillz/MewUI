namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Text format descriptor - style, alignment, wrapping, and trimming.
/// <para>
/// Pure managed value object. Created directly by user code or controls.
/// Backend caches handle native resource lifecycle separately.
/// </para>
/// </summary>
internal sealed class BackendTextFormat
{
    public required IFont Font { get; init; }

    public required TextAlignment HorizontalAlignment { get; init; }

    public required TextAlignment VerticalAlignment { get; init; }

    public required TextWrapping Wrapping { get; init; }

    public required TextTrimming Trimming { get; init; }

    /// <summary>
    /// Space added between the clusters the backend shapes the text into, in DIPs; negative tightens.
    /// Where a backend puts it around a cluster is its own choice, and so is whether the last cluster
    /// gets any: its measurement context reports the advances it draws with for the same value.
    /// </summary>
    public double LetterSpacing { get; init; }
}
