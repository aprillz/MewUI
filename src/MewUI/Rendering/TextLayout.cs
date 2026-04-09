namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Text layout measurement result produced by <see cref="IGraphicsContext.CreateTextLayout"/>.
/// Pure managed result. Backend may attach a native handle internally for rendering.
/// </summary>
public sealed class TextLayout
{
    public required Size MeasuredSize { get; init; }

    public required Rect EffectiveBounds { get; set; }

    public required double EffectiveMaxWidth { get; init; }

    public required double ContentHeight { get; init; }

    /// <summary>Backend-private native handle for rendering.</summary>
    internal nint BackendHandle { get; set; }
}
