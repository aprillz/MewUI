namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// Direct2D solid-color brush resource.
/// <para>
/// Because <c>ID2D1SolidColorBrush</c> objects are render-target-specific, this class stores
/// only the logical <see cref="Color"/>; the actual D2D brush handle is created lazily and
/// cached by <see cref="Direct2DGraphicsContext"/> via its internal solid brush cache
/// (<c>_solidBrushes</c>).  Disposing this object therefore requires no D2D cleanup.
/// </para>
/// </summary>
internal sealed class Direct2DSolidColorBrush : ISolidColorBrush
{
    /// <inheritdoc/>
    public Color Color { get; }

    public Direct2DSolidColorBrush(Color color) => Color = color;

    /// <inheritdoc/>
    public void Dispose() { } // No D2D handle owned here; lifetime is managed by the context cache.
}
