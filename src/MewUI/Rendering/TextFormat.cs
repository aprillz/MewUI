namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Text format descriptor — style, alignment, wrapping, and trimming.
/// <para>
/// Pure managed value object. Created directly by user code or controls.
/// Backend caches handle native resource lifecycle separately.
/// </para>
/// </summary>
public sealed class TextFormat
{
    public required IFont Font { get; init; }

    public required TextAlignment HorizontalAlignment { get; init; }

    public required TextAlignment VerticalAlignment { get; init; }

    public required TextWrapping Wrapping { get; init; }

    public required TextTrimming Trimming { get; init; }
}
