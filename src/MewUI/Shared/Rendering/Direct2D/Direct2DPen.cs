namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// Direct2D pen resource.
/// <para>
/// Stores the stroke color (via a <see cref="Direct2DSolidColorBrush"/>), thickness, and
/// <see cref="StrokeStyle"/> attributes.  The <c>ID2D1StrokeStyle</c> COM object — which is
/// factory-level and <em>not</em> render-target-specific — is created on first use and cached
/// in <see cref="StrokeStyleHandle"/>.
/// </para>
/// <para>
/// When constructed from a <see cref="Color"/>, the pen creates and owns the inner brush.
/// When constructed from an existing <see cref="IBrush"/>, the brush lifetime is managed by
/// the caller.
/// </para>
/// </summary>
internal sealed class Direct2DPen : IPen
{
    private readonly bool _ownsBrush;
    private bool _disposed;

    /// <inheritdoc/>
    public IBrush Brush { get; }

    /// <inheritdoc/>
    public double Thickness { get; }

    /// <inheritdoc/>
    public StrokeStyle StrokeStyle { get; }

    /// <summary>
    /// The cached <c>ID2D1StrokeStyle*</c> handle, or zero if not yet created or not supported.
    /// Created lazily by <see cref="Direct2DGraphicsFactory.CreatePen(IBrush, double, StrokeStyle?)"/>.
    /// </summary>
    internal nint StrokeStyleHandle { get; }

    public Direct2DPen(Color color, double thickness, StrokeStyle strokeStyle, nint strokeStyleHandle)
    {
        Brush = new Direct2DSolidColorBrush(color);
        Thickness = thickness;
        StrokeStyle = strokeStyle;
        StrokeStyleHandle = strokeStyleHandle;
        _ownsBrush = true;
    }

    public Direct2DPen(IBrush brush, double thickness, StrokeStyle strokeStyle, nint strokeStyleHandle)
    {
        Brush = brush;
        Thickness = thickness;
        StrokeStyle = strokeStyle;
        StrokeStyleHandle = strokeStyleHandle;
        _ownsBrush = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsBrush)
                Brush.Dispose();
            // StrokeStyleHandle is released by the factory that created it, not here,
            // because multiple pens may share the same D2D stroke style object.
            _disposed = true;
        }
    }
}
