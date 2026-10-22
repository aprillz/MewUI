using System.Numerics;

using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Motion;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Drawing;

/// <summary>The LiveCharts <see cref="DrawingContext"/> that renders chart elements onto a MewUI <see cref="IGraphicsContext"/>.</summary>
public sealed class MewDrawingContext : DrawingContext
{
    private float _savedAlpha = 1f;

    /// <summary>Initializes a drawing context for one frame of <paramref name="motionCanvas"/> on <paramref name="graphics"/>, with <paramref name="background"/> as the frame background and <paramref name="factory"/> for resources.</summary>
    public MewDrawingContext(CoreMotionCanvas motionCanvas, IGraphicsContext graphics, Color background, IGraphicsFactory factory)
    {
        MotionCanvas = motionCanvas;
        G = graphics;
        Background = background;
        Factory = factory;
    }

    /// <summary>Gets the motion canvas being drawn.</summary>
    public CoreMotionCanvas MotionCanvas { get; }

    /// <summary>Gets the graphics context of the current frame.</summary>
    public IGraphicsContext G { get; }

    /// <summary>Gets the graphics factory of the rendering backend.</summary>
    public IGraphicsFactory Factory { get; }

    /// <summary>Gets the frame background color.</summary>
    public Color Background { get; }

    // Resolved paint state, set by the active MewPaint in OnPaintStarted and read by geometry Draw helpers.
    internal Color ActiveColor { get; set; }
    internal PaintStyle ActiveStyle { get; set; }
    internal float ActiveStrokeThickness { get; set; } = 1f;
    internal bool ActivePixelSnap { get; set; }

    // Optional gradient/image fill brush and dashed/styled stroke pen; when set they take
    // precedence over ActiveColor. Set by gradient/dashed paints in OnPaintStarted.
    internal Brush? ActiveBrush { get; set; }
    internal Pen? ActivePen { get; set; }

    /// <summary>
    /// Gets or sets the draw area, in local coordinates, that gradient paints map their relative (0..1)
    /// coordinates to; the host sets it to the element bounds and it narrows to the active clip zone while drawing.
    /// </summary>
    public Rect DrawArea { get; set; }
    private Rect _previousDrawArea;

    /// <summary>Ignores the log text; the MewUI backend has no on-canvas diagnostics overlay.</summary>
    public override void LogOnCanvas(string log) { /* diagnostics overlay, not implemented yet */ }

    internal override void OnBeginDraw()
    {
        // Background is painted by the host within the element bounds (ChartViewBase.OnRender);
        // clearing the whole shared surface here would erase other controls.
    }

    internal override void OnEndDraw() { }

    internal override void OnBeginZone(CanvasZone zone)
    {
        if (zone.Clip == LvcRectangle.Empty) return;
        G.Save();
        G.IntersectClip(new Rect(zone.Clip.X, zone.Clip.Y, zone.Clip.Width, zone.Clip.Height));
        _previousDrawArea = DrawArea;
        DrawArea = new Rect(zone.Clip.X, zone.Clip.Y, zone.Clip.Width, zone.Clip.Height);
    }

    internal override void OnEndZone(CanvasZone zone)
    {
        if (zone.Clip == LvcRectangle.Empty) return;
        G.Restore();
        DrawArea = _previousDrawArea;
    }

    internal override void SelectPaint(Paint paint)
    {
        ActiveLvcPaint = paint;
        PaintMotionProperty.s_activePaint = paint;
        paint.OnPaintStarted(this, null);
    }

    internal override void ClearPaintSelection(Paint paint)
    {
        paint.OnPaintFinished(this, null);
        ActiveLvcPaint = null!;
        PaintMotionProperty.s_activePaint = null!;
    }

    internal override void Draw(IDrawnElement drawable)
    {
        var element = (IDrawnElement<MewDrawingContext>)drawable;

        var transformed = element.HasTransform;
        if (transformed)
        {
            G.Save();
            G.SetTransform(BuildTransform(element) * G.GetTransform());
        }

        var opacity = ActiveOpacity;
        var previousAlpha = G.GlobalAlpha;
        if (opacity < 1f)
            G.GlobalAlpha = previousAlpha * opacity;

        if (ActiveLvcPaint is null)
        {
            // For label geometries IDrawnElement.Fill aliases Paint ("quick hack" in BaseLabelGeometry),
            // so guard against drawing the same paint object twice (which doubles text and muddies its AA).
            var fill = element.Fill;
            var stroke = element.Stroke;
            var paint = element.Paint;
            if (fill is not null) DrawByPaint(fill, element);
            if (stroke is not null && !ReferenceEquals(stroke, fill)) DrawByPaint(stroke, element);
            if (paint is not null && !ReferenceEquals(paint, fill) && !ReferenceEquals(paint, stroke)) DrawByPaint(paint, element);
        }
        else
        {
            if (ActiveLvcPaint.PaintStyle.HasFlag(PaintStyle.Fill))
            {
                if (element.Fill is null) element.Draw(this);
                else DrawByPaint(element.Fill, element);
            }

            if (ActiveLvcPaint.PaintStyle.HasFlag(PaintStyle.Stroke))
            {
                if (element.Stroke is null) element.Draw(this);
                else DrawByPaint(element.Stroke, element);
            }
        }

        if (opacity < 1f)
            G.GlobalAlpha = previousAlpha;

        if (transformed)
            G.Restore();
    }

    private void DrawByPaint(Paint paint, IDrawnElement<MewDrawingContext> element)
    {
        var savedColor = ActiveColor;
        var savedStyle = ActiveStyle;
        var savedThickness = ActiveStrokeThickness;
        var savedPixelSnap = ActivePixelSnap;
        var savedBrush = ActiveBrush;
        var savedPen = ActivePen;
        var savedLvcPaint = ActiveLvcPaint;

        if (paint != MeasureTask.Instance)
        {
            ActiveLvcPaint = paint;
            paint.OnPaintStarted(this, element);
        }

        element.Draw(this);
        paint.OnPaintFinished(this, element);

        ActiveColor = savedColor;
        ActiveStyle = savedStyle;
        ActiveStrokeThickness = savedThickness;
        ActivePixelSnap = savedPixelSnap;
        ActiveBrush = savedBrush;
        ActivePen = savedPen;
        ActiveLvcPaint = savedLvcPaint;
    }

    // Opacity handling for the legend/tooltip path (DrawByPaint with a geometry opacity mask).
    internal void PushOpacity(float opacity)
    {
        _savedAlpha = G.GlobalAlpha;
        G.GlobalAlpha = _savedAlpha * opacity;
    }

    internal void PopOpacity() => G.GlobalAlpha = _savedAlpha;

    // ----- geometry draw helpers (used by MewCharts geometries) -----

    private bool IsStroke => ActiveStyle.HasFlag(PaintStyle.Stroke);

    /// <summary>Fills or strokes the rectangle with the active paint.</summary>
    public void DrawRectangle(Rect rect)
    {
        if (IsStroke)
        {
            if (ActivePen is not null) G.DrawRectangle(rect, ActivePen);
            else G.DrawRectangle(rect, ActiveColor, ActiveStrokeThickness, strokeInset: ActivePixelSnap);
        }
        else if (ActiveBrush is not null) G.FillRectangle(rect, ActiveBrush);
        else G.FillRectangle(rect, ActiveColor);
    }

    /// <summary>Fills or strokes the rounded rectangle with the active paint.</summary>
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY)
    {
        if (IsStroke)
        {
            if (ActivePen is not null) G.DrawRoundedRectangle(rect, radiusX, radiusY, ActivePen);
            else G.DrawRoundedRectangle(rect, radiusX, radiusY, ActiveColor, ActiveStrokeThickness);
        }
        else if (ActiveBrush is not null) G.FillRoundedRectangle(rect, radiusX, radiusY, ActiveBrush);
        else G.FillRoundedRectangle(rect, radiusX, radiusY, ActiveColor);
    }

    /// <summary>Fills or strokes the ellipse inscribed in the bounds with the active paint.</summary>
    public void DrawEllipse(Rect bounds)
    {
        if (IsStroke)
        {
            if (ActivePen is not null) G.DrawEllipse(bounds, ActivePen);
            else G.DrawEllipse(bounds, ActiveColor, ActiveStrokeThickness);
        }
        else if (ActiveBrush is not null) G.FillEllipse(bounds, ActiveBrush);
        else G.FillEllipse(bounds, ActiveColor);
    }

    /// <summary>Strokes a line between the two points with the active paint.</summary>
    public void DrawLine(Point start, Point end)
    {
        if (ActivePen is not null) G.DrawLine(start, end, ActivePen);
        else G.DrawLine(start, end, ActiveColor, ActiveStrokeThickness, pixelSnap: ActivePixelSnap);
    }

    /// <summary>Fills or strokes the path with the active paint.</summary>
    public void DrawPath(PathGeometry path)
    {
        if (IsStroke)
        {
            if (ActivePen is not null) G.DrawPath(path, ActivePen);
            else G.DrawPath(path, ActiveColor, ActiveStrokeThickness);
        }
        else if (ActiveBrush is not null) G.FillPath(path, ActiveBrush);
        else G.FillPath(path, ActiveColor);
    }

    private static Matrix3x2 BuildTransform(IDrawnElement<MewDrawingContext> element)
    {
        var size = element.Measure();
        var origin = element.TransformOrigin;
        var originX = element.X + size.Width * origin.X;
        var originY = element.Y + size.Height * origin.Y;
        var pivot = new Vector2(originX, originY);

        var matrix = Matrix3x2.Identity;

        if (element.HasTranslate)
        {
            var translate = element.TranslateTransform;
            matrix *= Matrix3x2.CreateTranslation(translate.X, translate.Y);
        }

        if (element.HasRotation)
            matrix *= Matrix3x2.CreateRotation(element.RotateTransform * MathF.PI / 180f, pivot);

        if (element.HasScale)
        {
            var scale = element.ScaleTransform;
            matrix *= Matrix3x2.CreateScale(scale.X, scale.Y, pivot);
        }

        if (element.HasSkew)
        {
            var skew = element.SkewTransform;
            var skewMatrix = new Matrix3x2(
                1, MathF.Tan(skew.Y * MathF.PI / 180f),
                MathF.Tan(skew.X * MathF.PI / 180f), 1,
                0, 0);
            matrix *= Matrix3x2.CreateTranslation(-originX, -originY) * skewMatrix * Matrix3x2.CreateTranslation(originX, originY);
        }

        return matrix;
    }
}
