namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// GDI pen resource.
/// <para>
/// The pen stores its stroke attributes (color, thickness, style) as logical values.
/// The actual <c>HPEN</c> is created by <see cref="GdiGraphicsContext"/> per draw call using
/// <see cref="Native.Gdi32.ExtCreatePen"/> so that the pixel width can be quantised against the
/// current HDC's DPI scale.
/// </para>
/// When constructed from a <see cref="Color"/>, the pen creates and owns the inner
/// <see cref="ISolidColorBrush"/>.  When constructed from an existing <see cref="IBrush"/>
/// the brush lifetime is managed by the caller.
/// </summary>
internal sealed class GdiPen : IPen
{
    private readonly bool _ownsBrush;
    private bool _disposed;

    /// <inheritdoc/>
    public IBrush Brush { get; }

    /// <inheritdoc/>
    public double Thickness { get; }

    /// <inheritdoc/>
    public StrokeStyle StrokeStyle { get; }

    public GdiPen(Color color, double thickness, StrokeStyle strokeStyle)
    {
        Brush = new GdiSolidColorBrush(color);
        Thickness = thickness;
        StrokeStyle = strokeStyle;
        _ownsBrush = true;
    }

    public GdiPen(IBrush brush, double thickness, StrokeStyle strokeStyle)
    {
        Brush = brush;
        Thickness = thickness;
        StrokeStyle = strokeStyle;
        _ownsBrush = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsBrush)
                Brush.Dispose();
            _disposed = true;
        }
    }
}
