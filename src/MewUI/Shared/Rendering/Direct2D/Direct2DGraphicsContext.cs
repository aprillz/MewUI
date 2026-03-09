using System.Diagnostics;
using System.Numerics;

using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DGraphicsContext : GraphicsContextBase
{
    private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    private const int D2DERR_WRONG_RESOURCE_DOMAIN = unchecked((int)0x88990015);

    private readonly nint _hwnd;
    private readonly nint _d2dFactory;
    private readonly nint _dwriteFactory;
    private readonly nint _defaultStrokeStyle;
    private readonly Action? _onRecreateTarget;
    private readonly bool _ownsRenderTarget;
    private readonly bool _useClearTypeText;

    private nint _renderTarget; // ID2D1RenderTarget*
    private readonly int _renderTargetGeneration;
    private readonly Dictionary<uint, nint> _solidBrushes = new();
    private readonly Stack<(Matrix3x2 transform, float globalAlpha, int clipCount, Rect? clipBoundsWorld, bool textPixelSnap)> _states = new();
    private readonly Stack<ClipEntry> _clipStack = new();
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private float _globalAlpha = 1f;
    private bool _textPixelSnap = true;
    private Rect? _clipBoundsWorld;
    private bool _disposed;

    public override ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    public override double DpiScale { get; }

    public Direct2DGraphicsContext(
        nint hwnd,
        double dpiScale,
        nint renderTarget,
        int renderTargetGeneration,
        nint d2dFactory,
        nint dwriteFactory,
        nint defaultStrokeStyle,
        Action? onRecreateTarget,
        bool ownsRenderTarget)
    {
        _hwnd = hwnd;
        _d2dFactory = d2dFactory;
        _dwriteFactory = dwriteFactory;
        _defaultStrokeStyle = defaultStrokeStyle;
        _onRecreateTarget = onRecreateTarget;
        _ownsRenderTarget = ownsRenderTarget;
        DpiScale = dpiScale;

        _renderTarget = renderTarget;
        _renderTargetGeneration = renderTargetGeneration;
        D2D1VTable.BeginDraw((ID2D1RenderTarget*)_renderTarget);
        // ClearType is only appropriate for opaque surfaces. For layered/per-pixel-alpha presentation
        // (we render into an offscreen premultiplied bitmap), prefer grayscale to avoid D2D errors
        // and color fringes.
        var textAa = _hwnd == 0
            ? D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE
            : D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE;
        _useClearTypeText = textAa == D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE;
        D2D1VTable.SetTextAntialiasMode((ID2D1RenderTarget*)_renderTarget, textAa);
    }

    [Conditional("DEBUG")]
    private static void AssertHr(int hr, string op)
    {
        if (hr >= 0) return;
        string msg = $"Direct2D: {op} failed: 0x{hr:X8}";
        Debug.Fail(msg);
        DiagLog.Write(msg);
    }

    public override void Dispose()
    {
        if (_disposed) return;

        try
        {
            if (_renderTarget != 0)
            {
                while (_clipStack.Count > 0) PopClip();
                int hr = D2D1VTable.EndDraw((ID2D1RenderTarget*)_renderTarget);
                AssertHr(hr, "EndDraw");
                if (hr == D2DERR_RECREATE_TARGET || hr == D2DERR_WRONG_RESOURCE_DOMAIN)
                    _onRecreateTarget?.Invoke();
            }
        }
        finally
        {
            foreach (var (_, brush) in _solidBrushes)
                ComHelpers.Release(brush);
            _solidBrushes.Clear();

            if (_ownsRenderTarget && _renderTarget != 0)
                ComHelpers.Release(_renderTarget);

            _renderTarget = 0;
            _disposed = true;
        }
    }

    protected override void SaveCore()
        => _states.Push((_transform, _globalAlpha, _clipStack.Count, _clipBoundsWorld, _textPixelSnap));

    protected override void RestoreCore()
    {
        if (_states.Count == 0 || _renderTarget == 0) return;

        var state = _states.Pop();
        while (_clipStack.Count > state.clipCount) PopClip();

        _transform = state.transform;
        _globalAlpha = state.globalAlpha;
        bool snapChanged = _textPixelSnap != state.textPixelSnap;
        _textPixelSnap = state.textPixelSnap;
        _clipBoundsWorld = state.clipBoundsWorld;
        SyncNativeTransform();
        if (snapChanged && _useClearTypeText)
            D2D1VTable.SetTextAntialiasMode((ID2D1RenderTarget*)_renderTarget,
                _textPixelSnap ? D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE : D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE);
    }

    protected override void SetClipCore(Rect rect)
    {
        if (_renderTarget == 0) return;

        // Track bounding box in world space for text-culling heuristics.
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, TransformRect(rect));

        D2D1VTable.PushAxisAlignedClip((ID2D1RenderTarget*)_renderTarget, ToRectF(rect));
        _clipStack.Push(new ClipEntry(ClipKind.AxisAligned, 0, 0));
    }

    protected override void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY)
    {
        if (_renderTarget == 0) return;

        if (radiusX <= 0 && radiusY <= 0)
        {
            SetClip(rect);
            return;
        }

        if (_d2dFactory == 0)
        {
            SetClip(rect);
            return;
        }

        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, TransformRect(rect));

        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
        int hr = D2D1VTable.CreateRoundedRectangleGeometry((ID2D1Factory*)_d2dFactory, rr, out var geometry);
        if (hr < 0 || geometry == 0)
        {
            SetClip(rect);
            return;
        }

        hr = D2D1VTable.CreateLayer((ID2D1RenderTarget*)_renderTarget, out var layer);
        if (hr < 0 || layer == 0)
        {
            ComHelpers.Release(geometry);
            SetClip(rect);
            return;
        }

        var parameters = new D2D1_LAYER_PARAMETERS(
            contentBounds: ToRectF(rect),
            geometricMask: geometry,
            maskAntialiasMode: D2D1_ANTIALIAS_MODE.PER_PRIMITIVE,
            maskTransform: D2D1_MATRIX_3X2_F.Identity,
            opacity: 1.0f,
            opacityBrush: 0,
            layerOptions: _useClearTypeText ? D2D1_LAYER_OPTIONS.INITIALIZE_FOR_CLEARTYPE : D2D1_LAYER_OPTIONS.NONE);

        D2D1VTable.PushLayer((ID2D1RenderTarget*)_renderTarget, parameters, layer);
        _clipStack.Push(new ClipEntry(ClipKind.Layer, layer, geometry));
    }

    protected override void ResetClipCore()
    {
        // Pop clips back to the save-boundary, or all clips if no save was pushed.
        int targetCount = _states.Count > 0 ? _states.Peek().clipCount : 0;
        while (_clipStack.Count > targetCount) PopClip();
        _clipBoundsWorld = null;
    }

    protected override void TranslateCore(double dx, double dy)
    {
        _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;
        SyncNativeTransform();
    }

    protected override void RotateCore(double angleRadians)
    {
        _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;
        SyncNativeTransform();
    }

    protected override void ScaleCore(double sx, double sy)
    {
        _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;
        SyncNativeTransform();
    }

    protected override void SetTransformCore(Matrix3x2 matrix)
    {
        _transform = matrix;
        SyncNativeTransform();
    }

    protected override Matrix3x2 GetTransformCore() => _transform;

    protected override void ResetTransformCore()
    {
        _transform = Matrix3x2.Identity;
        SyncNativeTransform();
    }

    public override float GlobalAlpha
    {
        get => _globalAlpha;
        set => _globalAlpha = Math.Clamp(value, 0f, 1f);
    }

    public override bool TextPixelSnap
    {
        get => _textPixelSnap;
        set
        {
            if (_textPixelSnap == value) return;
            _textPixelSnap = value;
            if (_renderTarget == 0) return;
            // ClearType forces pixel snapping for subpixel RGB alignment.
            // Switch to grayscale so NO_SNAP actually takes effect.
            if (_useClearTypeText)
                D2D1VTable.SetTextAntialiasMode((ID2D1RenderTarget*)_renderTarget,
                    value ? D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE : D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE);
        }
    }

    private void SyncNativeTransform()
    {
        if (_renderTarget == 0) return;
        var m = new D2D1_MATRIX_3X2_F(
            _transform.M11, _transform.M12,
            _transform.M21, _transform.M22,
            _transform.M31, _transform.M32);
        D2D1VTable.SetTransform((ID2D1RenderTarget*)_renderTarget, m);
    }

    public override void Clear(Color color)
    {
        if (_renderTarget == 0) return;
        D2D1VTable.Clear((ID2D1RenderTarget*)_renderTarget, ToColorF(color));
    }

    protected override void DrawLineCore(Point start, Point end, Color color, double thickness = 1)
    {
        if (_renderTarget == 0) return;

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        var p0 = ToPoint2F(start);
        var p1 = ToPoint2F(end);

        D2D1VTable.DrawLine((ID2D1RenderTarget*)_renderTarget, p0, p1, brush, stroke, _defaultStrokeStyle);
    }

    protected override void DrawRectangleCore(Rect rect, Color color, double thickness = 1)
    {
        if (_renderTarget == 0) return;
        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        D2D1VTable.DrawRectangle((ID2D1RenderTarget*)_renderTarget, ToRectF(rect), brush, stroke, _defaultStrokeStyle);
    }

    protected override void FillRectangleCore(Rect rect, Color color)
    {
        if (_renderTarget == 0) return;
        nint brush = GetSolidBrush(color);
        D2D1VTable.FillRectangle((ID2D1RenderTarget*)_renderTarget, ToRectF(rect), brush);
    }

    protected override void DrawRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (_renderTarget == 0) return;
        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
        D2D1VTable.DrawRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, brush, stroke, _defaultStrokeStyle);
    }

    protected override void FillRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (_renderTarget == 0) return;
        nint brush = GetSolidBrush(color);
        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
        D2D1VTable.FillRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, brush);
    }

    protected override void DrawEllipseCore(Rect bounds, Color color, double thickness = 1)
    {
        if (_renderTarget == 0) return;
        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);

        var center = new D2D1_POINT_2F(
            (float)(bounds.X + bounds.Width / 2),
            (float)(bounds.Y + bounds.Height / 2));
        var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));
        D2D1VTable.DrawEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, brush, stroke, _defaultStrokeStyle);
    }

    protected override void FillEllipseCore(Rect bounds, Color color)
    {
        if (_renderTarget == 0) return;
        nint brush = GetSolidBrush(color);
        var center = new D2D1_POINT_2F(
            (float)(bounds.X + bounds.Width / 2),
            (float)(bounds.Y + bounds.Height / 2));
        var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));
        D2D1VTable.FillEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, brush);
    }

    public override void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null || color.A == 0 || thickness <= 0) return;

        nint geometry = BuildD2DPathGeometry(path);
        if (geometry == 0) return;

        try
        {
            nint brush = GetSolidBrush(color);
            float stroke = QuantizeStrokeDip((float)thickness);
            D2D1VTable.DrawGeometry((ID2D1RenderTarget*)_renderTarget, geometry, brush, stroke, _defaultStrokeStyle);
        }
        finally
        {
            ComHelpers.Release(geometry);
        }
    }

    public override void FillPath(PathGeometry path, Color color)
    {
        FillPath(path, color, FillRule.NonZero);
    }

    public override void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null || color.A == 0) return;

        nint geometry = BuildD2DPathGeometry(path, fillRule);
        if (geometry == 0) return;

        try
        {
            nint brush = GetSolidBrush(color);
            D2D1VTable.FillGeometry((ID2D1RenderTarget*)_renderTarget, geometry, brush);
        }
        finally
        {
            ComHelpers.Release(geometry);
        }
    }

    public override void FillPath(PathGeometry path, IBrush brush)
        => FillPath(path, brush, path?.FillRule ?? FillRule.NonZero);

    public override void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
    {
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null) return;
        if (brush is ISolidColorBrush solid)
        {
            FillPath(path, solid.Color, fillRule);
            return;
        }
        if (brush is IGradientBrush gradient && gradient.GradientUnits == GradientUnits.UserSpaceOnUse)
        {
            nint geometry = BuildD2DPathGeometry(path, fillRule);
            if (geometry == 0) return;
            try
            {
                FillWithGradient(gradient, default, b =>
                    D2D1VTable.FillGeometry((ID2D1RenderTarget*)_renderTarget, geometry, b));
            }
            finally { ComHelpers.Release(geometry); }
            return;
        }
        if (brush is IGradientBrush g)
            FillPath(path, g.GetRepresentativeColor(), fillRule);
    }

    public override void FillRectangle(Rect rect, IBrush brush)
    {
        if (_renderTarget == 0) return;
        if (brush is ISolidColorBrush solid) { FillRectangle(rect, solid.Color); return; }
        if (brush is IGradientBrush gradient)
        {
            var rf = ToRectF(rect);
            FillWithGradient(gradient, rect, b =>
                D2D1VTable.FillRectangle((ID2D1RenderTarget*)_renderTarget, rf, b));
        }
    }

    public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (_renderTarget == 0) return;
        if (brush is ISolidColorBrush solid) { FillRoundedRectangle(rect, radiusX, radiusY, solid.Color); return; }
        if (brush is IGradientBrush gradient)
        {
            var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
            FillWithGradient(gradient, rect, b =>
                D2D1VTable.FillRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, b));
        }
    }

    public override void FillEllipse(Rect bounds, IBrush brush)
    {
        if (_renderTarget == 0) return;
        if (brush is ISolidColorBrush solid) { FillEllipse(bounds, solid.Color); return; }
        if (brush is IGradientBrush gradient)
        {
            var center = new D2D1_POINT_2F(
                (float)(bounds.X + bounds.Width / 2),
                (float)(bounds.Y + bounds.Height / 2));
            var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));
            FillWithGradient(gradient, bounds, b =>
                D2D1VTable.FillEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, b));
        }
    }

    public override void DrawPath(PathGeometry path, IPen pen)
    {
        if (_renderTarget == 0 || _d2dFactory == 0 || path == null || pen.Thickness <= 0) return;
        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;

        nint geometry = BuildD2DPathGeometry(path, FillRule.NonZero);
        if (geometry == 0) return;

        try
        {
            if (pen.Brush is IGradientBrush gradient)
            {
                FillWithGradient(gradient, default, b =>
                    D2D1VTable.DrawGeometry((ID2D1RenderTarget*)_renderTarget, geometry, b, stroke, ssHandle));
            }
            else
            {
                Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
                if (color.A == 0) return;
                nint brush = GetSolidBrush(color);
                D2D1VTable.DrawGeometry((ID2D1RenderTarget*)_renderTarget, geometry, brush, stroke, ssHandle);
            }
        }
        finally
        {
            ComHelpers.Release(geometry);
        }
    }

    public override void DrawLine(Point start, Point end, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0) return;
        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;
        var p0 = ToPoint2F(start);
        var p1 = ToPoint2F(end);

        if (pen.Brush is IGradientBrush gradient)
        {
            FillWithGradient(gradient, default, b =>
                D2D1VTable.DrawLine((ID2D1RenderTarget*)_renderTarget, p0, p1, b, stroke, ssHandle));
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0) return;
            D2D1VTable.DrawLine((ID2D1RenderTarget*)_renderTarget, p0, p1, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    public override void DrawRectangle(Rect rect, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0) return;
        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;
        var rf = ToRectF(rect);

        if (pen.Brush is IGradientBrush gradient)
        {
            FillWithGradient(gradient, rect, b =>
                D2D1VTable.DrawRectangle((ID2D1RenderTarget*)_renderTarget, rf, b, stroke, ssHandle));
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0) return;
            D2D1VTable.DrawRectangle((ID2D1RenderTarget*)_renderTarget, rf, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0) return;
        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;
        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);

        if (pen.Brush is IGradientBrush gradient)
        {
            FillWithGradient(gradient, rect, b =>
                D2D1VTable.DrawRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, b, stroke, ssHandle));
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0) return;
            D2D1VTable.DrawRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    public override void DrawEllipse(Rect bounds, IPen pen)
    {
        if (_renderTarget == 0 || pen.Thickness <= 0) return;
        float stroke = QuantizeStrokeDip((float)pen.Thickness);
        nint ssHandle = pen is Direct2DPen d2dPen ? d2dPen.StrokeStyleHandle : 0;

        var center = new D2D1_POINT_2F(
            (float)(bounds.X + bounds.Width / 2),
            (float)(bounds.Y + bounds.Height / 2));
        var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));

        if (pen.Brush is IGradientBrush gradient)
        {
            FillWithGradient(gradient, bounds, b =>
                D2D1VTable.DrawEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, b, stroke, ssHandle));
        }
        else
        {
            Color color = pen.Brush is ISolidColorBrush solid ? solid.Color : Color.Black;
            if (color.A == 0) return;
            D2D1VTable.DrawEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, GetSolidBrush(color), stroke, ssHandle);
        }
    }

    private nint BuildD2DPathGeometry(PathGeometry path, FillRule fillRule = FillRule.NonZero)
    {
        int hr = D2D1VTable.CreatePathGeometry((ID2D1Factory*)_d2dFactory, out nint geometry);
        if (hr < 0 || geometry == 0) return 0;

        hr = D2D1VTable.OpenPathGeometry((ID2D1Geometry*)geometry, out nint sink);
        if (hr < 0 || sink == 0)
        {
            ComHelpers.Release(geometry);
            return 0;
        }

        bool figureOpen = false;
        try
        {
            var d2dFillMode = fillRule == FillRule.EvenOdd ? D2D1_FILL_MODE.ALTERNATE : D2D1_FILL_MODE.WINDING;
            D2D1VTable.SetFillMode((ID2D1GeometrySink*)sink, d2dFillMode);

            foreach (var cmd in path.Commands)
            {
                switch (cmd.Type)
                {
                    case PathCommandType.MoveTo:
                        if (figureOpen)
                        {
                            D2D1VTable.EndFigure((ID2D1GeometrySink*)sink, D2D1_FIGURE_END.OPEN);
                            figureOpen = false;
                        }
                        D2D1VTable.BeginFigure((ID2D1GeometrySink*)sink,
                            new D2D1_POINT_2F((float)cmd.X0, (float)cmd.Y0),
                            D2D1_FIGURE_BEGIN.FILLED);
                        figureOpen = true;
                        break;

                    case PathCommandType.LineTo:
                        if (figureOpen)
                            D2D1VTable.AddLine((ID2D1GeometrySink*)sink,
                                new D2D1_POINT_2F((float)cmd.X0, (float)cmd.Y0));
                        break;

                    case PathCommandType.BezierTo:
                        if (figureOpen)
                        {
                            var bezier = new D2D1_BEZIER_SEGMENT(
                                new D2D1_POINT_2F((float)cmd.X0, (float)cmd.Y0),
                                new D2D1_POINT_2F((float)cmd.X1, (float)cmd.Y1),
                                new D2D1_POINT_2F((float)cmd.X2, (float)cmd.Y2));
                            D2D1VTable.AddBezier((ID2D1GeometrySink*)sink, bezier);
                        }
                        break;

                    case PathCommandType.Close:
                        if (figureOpen)
                        {
                            D2D1VTable.EndFigure((ID2D1GeometrySink*)sink, D2D1_FIGURE_END.CLOSED);
                            figureOpen = false;
                        }
                        break;
                }
            }

            if (figureOpen)
                D2D1VTable.EndFigure((ID2D1GeometrySink*)sink, D2D1_FIGURE_END.OPEN);

            hr = D2D1VTable.CloseGeometrySink((ID2D1GeometrySink*)sink);
        }
        finally
        {
            ComHelpers.Release(sink);
        }

        if (hr < 0)
        {
            ComHelpers.Release(geometry);
            return 0;
        }

        return geometry;
    }

    protected override void DrawTextCore(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        if (_renderTarget == 0 || text.IsEmpty) return;

        if (_clipBoundsWorld.HasValue && bounds.Width < 100_000)
        {
            var clip = _clipBoundsWorld.Value;
            var wv = Vector2.Transform(new Vector2((float)bounds.X, (float)bounds.Y), _transform);
            if (wv.X + bounds.Width <= clip.X || wv.X >= clip.Right ||
                wv.Y + bounds.Height <= clip.Y || wv.Y >= clip.Bottom)
                return;
        }

        if (font is not DirectWriteFont dwFont)
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));

        nint textFormat = CreateTextFormat(dwFont, horizontalAlignment, verticalAlignment, wrapping);
        if (textFormat == 0) return;

        nint textLayout = 0;
        nint trimmingSign = 0;
        try
        {
            nint brush = GetSolidBrush(color);
            var options = _textPixelSnap
                ? D2D1_DRAW_TEXT_OPTIONS.CLIP
                : D2D1_DRAW_TEXT_OPTIONS.NO_SNAP | D2D1_DRAW_TEXT_OPTIONS.CLIP;

            if (trimming == TextTrimming.CharacterEllipsis)
            {
                var rectF = ToRectF(bounds);
                int hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat,
                    rectF.right - rectF.left, rectF.bottom - rectF.top, out textLayout);
                if (hr >= 0 && textLayout != 0)
                {
                    DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)_dwriteFactory, textFormat, out trimmingSign);
                    var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
                    DWriteVTable.SetTrimming(textLayout, dwriteTrimming, trimmingSign);
                    D2D1VTable.DrawTextLayout((ID2D1RenderTarget*)_renderTarget,
                        new D2D1_POINT_2F(rectF.left, rectF.top), textLayout, brush, options);
                    return;
                }
            }

            D2D1VTable.DrawText((ID2D1RenderTarget*)_renderTarget, text, textFormat, ToRectF(bounds), brush, options);
        }
        finally
        {
            ComHelpers.Release(trimmingSign);
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font) => MeasureText(text, font, float.MaxValue);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty) return Size.Empty;

        if (font is not DirectWriteFont dwFont)
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));

        nint textFormat = 0;
        nint textLayout = 0;
        try
        {
            textFormat = CreateTextFormat(dwFont, TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap);
            if (textFormat == 0) return Size.Empty;

            float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)Math.Max(0, maxWidth);
            int hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, out textLayout);
            if (hr < 0 || textLayout == 0) return Size.Empty;

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0) return Size.Empty;

            var height = metrics.height;
            if (metrics.top < 0) height += -metrics.top;

            return new Size(TextMeasurePolicy.ApplyWidthPadding(metrics.widthIncludingTrailingWhitespace), height);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    private nint CreateTextFormat(DirectWriteFont font, TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment, TextWrapping wrapping)
    {
        var weight = (DWRITE_FONT_WEIGHT)(int)font.Weight;
        var style = font.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
        int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, font.Family,
            weight, style, (float)font.Size, out nint textFormat);
        if (hr < 0 || textFormat == 0) return 0;

        DWriteVTable.SetTextAlignment(textFormat, horizontalAlignment switch
        {
            TextAlignment.Left => DWRITE_TEXT_ALIGNMENT.LEADING,
            TextAlignment.Center => DWRITE_TEXT_ALIGNMENT.CENTER,
            TextAlignment.Right => DWRITE_TEXT_ALIGNMENT.TRAILING,
            _ => DWRITE_TEXT_ALIGNMENT.LEADING
        });

        DWriteVTable.SetParagraphAlignment(textFormat, verticalAlignment switch
        {
            TextAlignment.Top => DWRITE_PARAGRAPH_ALIGNMENT.NEAR,
            TextAlignment.Center => DWRITE_PARAGRAPH_ALIGNMENT.CENTER,
            TextAlignment.Bottom => DWRITE_PARAGRAPH_ALIGNMENT.FAR,
            _ => DWRITE_PARAGRAPH_ALIGNMENT.NEAR
        });

        DWriteVTable.SetWordWrapping(textFormat,
            wrapping == TextWrapping.NoWrap ? DWRITE_WORD_WRAPPING.NO_WRAP : DWRITE_WORD_WRAPPING.WRAP);
        return textFormat;
    }

    public override void DrawImage(IImage image, Point location) =>
        DrawImageCore(image, new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight));

    protected override void DrawImageCore(IImage image, Rect destRect) =>
        DrawImageCore(image, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect) =>
        DrawImageCore(
            image as Direct2DImage ?? throw new ArgumentException("Image must be a Direct2DImage", nameof(image)),
            destRect, sourceRect);

    private void DrawImageCore(Direct2DImage image, Rect destRect, Rect sourceRect)
    {
        if (_renderTarget == 0) return;

        int mipLevel = 0;
        if (ImageScaleQuality == ImageScaleQuality.HighQuality)
            mipLevel = SelectHighQualityMipLevel(sourceRect, destRect, DpiScale);

        nint bmp = mipLevel == 0
            ? image.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration)
            : image.GetOrCreateBitmapForMip(_renderTarget, _renderTargetGeneration, mipLevel);
        if (bmp == 0) return;

        // Pixel-snap destination rect in world space to avoid shimmer.
        // Use the translation components of _transform (M31/M32); this gives the correct result
        // for the common pure-translation case and degrades gracefully for rotated transforms.
        double tx = _transform.M31;
        double ty = _transform.M32;
        var worldDest = new Rect(destRect.X + tx, destRect.Y + ty, destRect.Width, destRect.Height);
        var snappedWorldDest = LayoutRounding.SnapRectEdgesToPixels(worldDest, DpiScale);
        var snappedLocalDest = new Rect(
            snappedWorldDest.X - tx,
            snappedWorldDest.Y - ty,
            snappedWorldDest.Width,
            snappedWorldDest.Height);

        var dst = ToRectF(snappedLocalDest);
        var src = mipLevel == 0
            ? new D2D1_RECT_F(
                left: (float)sourceRect.X,
                top: (float)sourceRect.Y,
                right: (float)sourceRect.Right,
                bottom: (float)sourceRect.Bottom)
            : CreateMipSourceRect(sourceRect, mipLevel);

        var interpolation = ImageScaleQuality switch
        {
            ImageScaleQuality.Fast => D2D1_BITMAP_INTERPOLATION_MODE.NEAREST_NEIGHBOR,
            _ => D2D1_BITMAP_INTERPOLATION_MODE.LINEAR,
        };

        D2D1VTable.DrawBitmap((ID2D1RenderTarget*)_renderTarget, bmp, dst, opacity: 1.0f, interpolation, src);
    }

    private static int SelectHighQualityMipLevel(Rect sourceRect, Rect destRect, double dpiScale)
    {
        double destW = Math.Max(1e-6, destRect.Width * dpiScale);
        double destH = Math.Max(1e-6, destRect.Height * dpiScale);
        double srcW = Math.Max(1.0, sourceRect.Width);
        double srcH = Math.Max(1.0, sourceRect.Height);
        double scale = Math.Max(srcW / destW, srcH / destH);
        if (scale <= 2.0) return 0;
        int level = 0;
        while (scale > 2.0 && level < 12) { scale *= 0.5; level++; }
        return level;
    }

    private static D2D1_RECT_F CreateMipSourceRect(Rect sourceRect, int mipLevel)
    {
        double factor = 1 << Math.Min(mipLevel, 30);
        return new D2D1_RECT_F(
            left: (float)(sourceRect.X / factor),
            top: (float)(sourceRect.Y / factor),
            right: (float)(sourceRect.Right / factor),
            bottom: (float)(sourceRect.Bottom / factor));
    }

    /// <summary>
    /// Creates a D2D gradient stop collection + gradient brush, invokes <paramref name="fillAction"/>
    /// with the raw brush handle, then releases both resources.
    /// </summary>
    private void FillWithGradient(IGradientBrush brush, Rect objectBounds, Action<nint> fillAction)
    {
        var stops = brush.Stops;
        if (stops == null || stops.Count == 0) return;

        var d2dStops = new D2D1_GRADIENT_STOP[stops.Count];
        for (int i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            d2dStops[i] = new D2D1_GRADIENT_STOP((float)Math.Clamp(s.Offset, 0.0, 1.0), ToColorF(s.Color));
        }

        var extendMode = brush.SpreadMethod switch
        {
            SpreadMethod.Reflect => D2D1_EXTEND_MODE.MIRROR,
            SpreadMethod.Repeat => D2D1_EXTEND_MODE.WRAP,
            _ => D2D1_EXTEND_MODE.CLAMP
        };

        int hr = D2D1VTable.CreateGradientStopCollection(
            (ID2D1RenderTarget*)_renderTarget,
            d2dStops, D2D1_GAMMA.GAMMA_2_2, extendMode, out nint stopCollection);
        if (hr < 0 || stopCollection == 0) return;

        try
        {
            var gt = brush.GradientTransform;
            var bProps = new D2D1_BRUSH_PROPERTIES(
                _globalAlpha,
                gt.HasValue ? ToMatrix3x2F(gt.Value) : D2D1_MATRIX_3X2_F.Identity);

            nint gradBrush = 0;

            if (brush is ILinearGradientBrush linear)
            {
                var start = ResolveGradientPoint(linear.StartPoint, brush.GradientUnits, objectBounds);
                var end = ResolveGradientPoint(linear.EndPoint, brush.GradientUnits, objectBounds);
                var linProps = new D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES(
                    new D2D1_POINT_2F((float)start.X, (float)start.Y),
                    new D2D1_POINT_2F((float)end.X, (float)end.Y));
                D2D1VTable.CreateLinearGradientBrush(
                    (ID2D1RenderTarget*)_renderTarget, linProps, bProps, stopCollection, out gradBrush);
            }
            else if (brush is IRadialGradientBrush radial)
            {
                var center = ResolveGradientPoint(radial.Center, brush.GradientUnits, objectBounds);
                var origin = ResolveGradientPoint(radial.GradientOrigin, brush.GradientUnits, objectBounds);
                double rx = brush.GradientUnits == GradientUnits.ObjectBoundingBox
                    ? radial.RadiusX * objectBounds.Width : radial.RadiusX;
                double ry = brush.GradientUnits == GradientUnits.ObjectBoundingBox
                    ? radial.RadiusY * objectBounds.Height : radial.RadiusY;
                var radProps = new D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES(
                    new D2D1_POINT_2F((float)center.X, (float)center.Y),
                    new D2D1_POINT_2F((float)(origin.X - center.X), (float)(origin.Y - center.Y)),
                    (float)rx, (float)ry);
                D2D1VTable.CreateRadialGradientBrush(
                    (ID2D1RenderTarget*)_renderTarget, radProps, bProps, stopCollection, out gradBrush);
            }

            if (gradBrush != 0)
            {
                try { fillAction(gradBrush); }
                finally { ComHelpers.Release(gradBrush); }
            }
        }
        finally
        {
            ComHelpers.Release(stopCollection);
        }
    }

    private static Point ResolveGradientPoint(Point p, GradientUnits units, Rect objectBounds)
        => units == GradientUnits.ObjectBoundingBox
            ? new Point(objectBounds.X + p.X * objectBounds.Width, objectBounds.Y + p.Y * objectBounds.Height)
            : p;

    private static D2D1_MATRIX_3X2_F ToMatrix3x2F(Matrix3x2 m)
        => new(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);

    private nint GetSolidBrush(Color color)
    {
        // Apply global alpha multiplier before looking up or creating the brush.
        if (_globalAlpha < 1f)
            color = Color.FromArgb((byte)(int)(color.A * _globalAlpha), color.R, color.G, color.B);

        uint key = color.ToArgb();
        if (_solidBrushes.TryGetValue(key, out var brush) && brush != 0)
            return brush;

        int hr = D2D1VTable.CreateSolidColorBrush((ID2D1RenderTarget*)_renderTarget, ToColorF(color), out brush);
        if (hr < 0 || brush == 0) return 0;

        _solidBrushes[key] = brush;
        return brush;
    }

    private static D2D1_COLOR_F ToColorF(Color color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static D2D1_POINT_2F ToPoint2F(Point point) =>
        new((float)point.X, (float)point.Y);

    private static D2D1_RECT_F ToRectF(Rect rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    private float QuantizeStrokeDip(float thickness)
    {
        if (thickness <= 0) return 0;
        float strokePx = thickness * (float)DpiScale;
        float snappedPx = Math.Max(1, (float)Math.Round(strokePx, MidpointRounding.AwayFromZero));
        return snappedPx / (float)DpiScale;
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of <paramref name="rect"/> after applying
    /// <see cref="_transform"/>. Used for conservative world-space culling tracking.
    /// </summary>
    private Rect TransformRect(Rect rect)
    {
        var tl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Y), _transform);
        var tr = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Y), _transform);
        var bl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Bottom), _transform);
        var br = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Bottom), _transform);
        float minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
        float minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
        float maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
        float maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Rect IntersectClipBounds(Rect? current, Rect next)
    {
        if (!current.HasValue) return next;
        double left = Math.Max(current.Value.X, next.X);
        double top = Math.Max(current.Value.Y, next.Y);
        double right = Math.Min(current.Value.Right, next.Right);
        double bottom = Math.Min(current.Value.Bottom, next.Bottom);
        return right > left && bottom > top
            ? new Rect(left, top, right - left, bottom - top)
            : new Rect(left, top, 0, 0);
    }

    private void PopClip()
    {
        if (_clipStack.Count == 0 || _renderTarget == 0) return;

        var entry = _clipStack.Pop();
        if (entry.Kind == ClipKind.AxisAligned)
        {
            D2D1VTable.PopAxisAlignedClip((ID2D1RenderTarget*)_renderTarget);
            return;
        }

        D2D1VTable.PopLayer((ID2D1RenderTarget*)_renderTarget);
        ComHelpers.Release(entry.Geometry);
        ComHelpers.Release(entry.Layer);
    }

    private enum ClipKind { AxisAligned, Layer }

    private readonly struct ClipEntry(ClipKind kind, nint layer, nint geometry)
    {
        public ClipKind Kind { get; } = kind;
        public nint Layer { get; } = layer;
        public nint Geometry { get; } = geometry;
    }
}
