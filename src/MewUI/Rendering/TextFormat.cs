namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Text format descriptor — style, alignment, wrapping, and trimming.
/// <para>
/// Created by <see cref="IGraphicsContext.CreateTextFormat"/>.
/// Backend manages the native handle lifecycle.
/// Consumer sets <see cref="IsDetached"/> = true when no longer needed.
/// </para>
/// </summary>
public sealed class TextFormat
{
    public required IFont Font { get; init; }

    public required TextAlignment HorizontalAlignment { get; init; }

    public required TextAlignment VerticalAlignment { get; init; }

    public required TextWrapping Wrapping { get; init; }

    public required TextTrimming Trimming { get; init; }

    /// <summary>Backend-specific native handle (e.g. IDWriteTextFormat*).</summary>
    public nint NativeHandle { get; internal set; }

    /// <summary>
    /// Set to true when the consumer no longer needs this format.
    /// The backend will release native resources at an appropriate time.
    /// </summary>
    public bool IsDetached { get; private set; }

    public static void Deatch(ref TextFormat? textFormat)
    {
        if (textFormat is not null)
        {
            textFormat.IsDetached = true;
            textFormat = null;
        }
    }
}
