namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Text layout result produced by <see cref="IGraphicsContext.CreateTextLayout"/>.
/// <para>
/// Backend manages the native handle lifecycle.
/// Consumer sets <see cref="IsDetached"/> = true when no longer needed.
/// </para>
/// </summary>
public sealed class TextLayout
{
    public required Size MeasuredSize { get; init; }

    public required Rect EffectiveBounds { get; set; }

    public required double EffectiveMaxWidth { get; init; }

    public required double ContentHeight { get; init; }

    /// <summary>Backend-specific native handle (e.g. IDWriteTextLayout*).</summary>
    public nint NativeHandle { get; internal set; }

    /// <summary>
    /// Set to true when the consumer no longer needs this layout.
    /// The backend will release native resources at an appropriate time.
    /// </summary>
    public bool IsDetached { get; private set; }

    public static void Deatch(ref TextLayout? textLayout)
    {
        if (textLayout is not null)
        {
            textLayout.IsDetached = true;
            textLayout = null;
        }
    }
}
