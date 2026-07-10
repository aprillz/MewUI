using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public enum PromptIconKind
{
    None,
    Question,
    Info,
    Warning,
    Error,
    Success,
    Shield,
    Crash,
}

public sealed class PromptIcon : FrameworkElement
{
    public static readonly MewProperty<PromptIconKind> KindProperty =
        MewProperty<PromptIconKind>.Register<PromptIcon>(nameof(Kind), PromptIconKind.None, MewPropertyOptions.AffectsRender);

    private const double StrokeThickness = 1.0;
    private static readonly StrokeStyle RoundedStroke = new() { LineJoin = StrokeLineJoin.Round, LineCap = StrokeLineCap.Round, MiterLimit = 10.0 };
    private static readonly StrokeStyle CrashHornStroke = new() { LineJoin = StrokeLineJoin.Round, MiterLimit = 10.0 };

    private static readonly PathGeometry QuestionGlyph = PathGeometry.Parse("M128 149c-3.313 0-6-2.687-6-6 0-12.01 6.202-18.654 15.53-24.897 7.381-5.549 10.47-9.04 10.47-16.103 0-8.505-6.28-14-16-14-8.972 0-15 5.626-15 14 0 3.313-2.687 6-6 6s-6-2.687-6-6c0-15.065 11.355-26 27-26 16.486 0 28 10.691 28 26 0 13.16-7.285 19.713-15.4 25.8-.089.067-.18.131-.272.192C136.316 133.333 134 136.7 134 143c0 3.313-2.687 6-6 6z M120 176c0 4.418 3.582 8 8 8s8-3.582 8-8-3.582-8-8-8-8 3.582-8 8z").Apply(x => x.Freeze());
    private static readonly PathGeometry InfoGlyph = PathGeometry.Parse("M128 75c4.971 0 9 4.029 9 9s-4.029 9-9 9-9-4.029-9-9 4.029-9 9-9z M128 104c4.418 0 8 3.582 8 8v60c0 4.418-3.582 8-8 8s-8-3.582-8-8v-60c0-4.418 3.582-8 8-8z").Apply(x => x.Freeze());
    private static readonly PathGeometry WarningBackground = PathGeometry.Parse("M128 34c7 0 14 4 18 11l83 148c8 14-2 31-18 31H45c-16 0-26-17-18-31l83-148c4-7 11-11 18-11z").Apply(x => x.Freeze());
    private static readonly PathGeometry WarningGlyph = PathGeometry.Parse("M128 86c4.418 0 8 3.582 8 8v52c0 4.418-3.582 8-8 8s-8-3.582-8-8V94c0-4.418 3.582-8 8-8z M128 172c4.418 0 8 3.582 8 8s-3.582 8-8 8-8-3.582-8-8 3.582-8 8-8z").Apply(x => x.Freeze());
    private static readonly PathGeometry ErrorGlyph = PathGeometry.Parse("M159 166c-1.791 0-3.583-.684-4.95-2.05L128 137.899l-26.05 26.05c-2.734 2.733-7.166 2.733-9.9 0-2.733-2.733-2.733-7.166 0-9.899L118.101 128 92.05 101.95c-2.733-2.733-2.733-7.166 0-9.899 2.734-2.733 7.166-2.733 9.9 0l26.05 26.05 26.05-26.05c2.734-2.733 7.166-2.733 9.9 0 2.733 2.733 2.733 7.166 0 9.899L137.899 128l26.051 26.05c2.733 2.733 2.733 7.166 0 9.899C162.583 165.316 160.791 166 159 166z").Apply(x => x.Freeze());
    private static readonly PathGeometry SuccessGlyph = PathGeometry.Parse("M116 166c-1.792 0-3.583-.684-4.95-2.05l-28-28c-2.734-2.734-2.734-7.166 0-9.9 2.733-2.732 7.166-2.732 9.899 0L116 149.101l50.05-50.051c2.733-2.732 7.166-2.732 9.899 0 2.734 2.734 2.734 7.166 0 9.9l-55 55C119.583 165.316 117.792 166 116 166z").Apply(x => x.Freeze());
    private static readonly PathGeometry ShieldLeft = PathGeometry.Parse("M128 29.4c-24.2 13.2-50.6 20.9-81.4 23.1v63.801c0 47.3 26.4 81.399 81.4 102.3V29.4z").Apply(x => x.Freeze());
    private static readonly PathGeometry ShieldRight = PathGeometry.Parse("M128 29.4c24.2 13.2 50.6 20.9 81.4 23.1v63.801c0 47.3-26.4 81.399-81.4 102.3V29.4z").Apply(x => x.Freeze());
    private static readonly PathGeometry ShieldOutline = PathGeometry.Parse("M128 29.4c-24.2 13.2-50.6 20.9-81.4 23.1v63.801c0 47.3 26.4 81.399 81.4 102.3 55-20.9 81.4-55 81.4-102.3V52.5C178.6 50.301 152.2 42.601 128 29.4z").Apply(x => x.Freeze());
    private static readonly PathGeometry CrashBody = PathGeometry.Parse("M128 50c47.496 0 86 38.504 86 86s-38.504 86-86 86-86-38.504-86-86 38.504-86 86-86z").Apply(x => x.Freeze());
    private static readonly PathGeometry CrashHorn = PathGeometry.Parse("M81.496 63.438 55.199 46.75c-9.095-5.504-19.696 3.474-15.816 13.959l12.16 34.411 M174.504 63.438l26.297-16.687c9.094-5.505 19.696 3.473 15.816 13.958l-12.16 34.411").Apply(x => x.Freeze());

    // Static shared resources (factory-lifetime, never disposed).
    private static bool _isInitialized;

    private static Pen? _penQuestionStroke;
    private static Pen? _penInfoStroke;
    private static Pen? _penWarningStroke;
    private static Pen? _penErrorStroke;
    private static Pen? _penSuccessStroke;
    private static Pen? _penShieldOutline;
    private static Pen? _penCrashHorn;
    private static Pen? _penCrashBody;
    private static Brush? _brushQuestionGlyph;
    private static Brush? _brushInfoGlyph;
    private static Brush? _brushWarningGlyph;
    private static Brush? _brushErrorGlyph;
    private static Brush? _brushSuccessGlyph;
    private static Brush? _brushCrashGlyph;
    private static Brush? _brushShieldLeft;
    private static Brush? _brushShieldRight;
    private static Brush? _brushCrashHorn;
    private static Brush? _brushCrashBody;

    private PromptIconKind _cachedKind;
    private Rect _cachedBounds;
    private Brush? _fill;

    public PromptIcon()
    { }

    public PromptIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = Bounds.Deflate(new Thickness(0.5));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        EnsureStaticResources();
        EnsureFillGradient(bounds);

        switch (Kind)
        {
            case PromptIconKind.Question:
                DrawCircleIcon(context, bounds, _fill!, _penQuestionStroke!, QuestionGlyph, _brushQuestionGlyph!, 0.628);
                break;

            case PromptIconKind.Info:
                DrawCircleIcon(context, bounds, _fill!, _penInfoStroke!, InfoGlyph, _brushInfoGlyph!, 0.610);
                break;

            case PromptIconKind.Warning:
                DrawGeometry(context, WarningBackground, bounds, _fill, _penWarningStroke, 1.0, Stretch.Uniform);
                DrawGeometry(context, WarningGlyph, bounds, _brushWarningGlyph, null, 0.593, Stretch.Uniform);
                break;

            case PromptIconKind.Error:
                DrawCircleIcon(context, bounds, _fill!, _penErrorStroke!, ErrorGlyph, _brushErrorGlyph!, 0.45);
                break;

            case PromptIconKind.Success:
                DrawCircleIcon(context, bounds, _fill!, _penSuccessStroke!, SuccessGlyph, _brushSuccessGlyph!, 0.572);
                break;

            case PromptIconKind.Shield:
                DrawShield(context, bounds);
                break;

            case PromptIconKind.Crash:
                DrawCrash(context, bounds);
                break;
        }
    }

    private static void EnsureStaticResources()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        _penQuestionStroke = new Pen(Color.FromRgb(138, 144, 155), StrokeThickness, RoundedStroke);
        _penInfoStroke = new Pen(Color.FromRgb(21, 101, 191), StrokeThickness, RoundedStroke);
        _penWarningStroke = new Pen(Color.FromRgb(200, 138, 10), StrokeThickness, RoundedStroke);
        _penErrorStroke = new Pen(Color.FromRgb(195, 46, 22), StrokeThickness, RoundedStroke);
        _penSuccessStroke = new Pen(Color.FromRgb(24, 149, 86), StrokeThickness, RoundedStroke);
        _penShieldOutline = new Pen(Color.FromRgb(43, 111, 182), StrokeThickness, RoundedStroke);
        _penCrashHorn = new Pen(Color.FromRgb(94, 50, 174), StrokeThickness, CrashHornStroke);
        _penCrashBody = new Pen(Color.FromRgb(94, 50, 174), StrokeThickness);

        _brushQuestionGlyph = new SolidColorBrush(Color.FromRgb(106, 111, 120));
        _brushInfoGlyph = new SolidColorBrush(Color.FromRgb(247, 251, 255));
        _brushWarningGlyph = new SolidColorBrush(Color.FromRgb(63, 67, 75));
        _brushErrorGlyph = new SolidColorBrush(Color.FromRgb(255, 248, 246));
        _brushSuccessGlyph = new SolidColorBrush(Color.FromRgb(243, 255, 249));
        _brushCrashGlyph = new SolidColorBrush(Color.FromRgb(251, 247, 255));

        _brushShieldLeft = new LinearGradientBrush(
            new Point(76.763, 38.334), new Point(120.762, 212.133),
            [new(0, Color.FromRgb(81, 175, 255)), new(1, Color.FromRgb(25, 118, 232))]);
        _brushShieldRight = new LinearGradientBrush(
            new Point(179.238, 38.335), new Point(135.238, 212.135),
            [new(0, Color.FromRgb(155, 227, 156)), new(1, Color.FromRgb(94, 199, 141))]);
        _brushCrashHorn = new LinearGradientBrush(
            new Point(128, 104.3564), new Point(128, -92.5697),
            [new(0, Color.FromRgb(178, 117, 240)), new(1, Color.FromRgb(122, 69, 212))]);
        _brushCrashBody = new LinearGradientBrush(
            new Point(128, 30), new Point(128, 226),
            [new(0, Color.FromRgb(178, 117, 240)), new(1, Color.FromRgb(122, 69, 212))]);
    }

    private void EnsureFillGradient(Rect bounds)
    {
        var kind = Kind;
        if (kind == _cachedKind && _cachedBounds == bounds && _fill != null)
        {
            return;
        }

        _cachedKind = kind;
        _cachedBounds = bounds;

        _fill = kind switch
        {
            PromptIconKind.Question => VertGrad(bounds, Color.FromRgb(245, 246, 249), Color.FromRgb(228, 231, 238)),
            PromptIconKind.Info => VertGrad(bounds, Color.FromRgb(65, 162, 244), Color.FromRgb(31, 127, 224)),
            PromptIconKind.Warning => VertGrad(bounds, Color.FromRgb(255, 216, 74), Color.FromRgb(243, 188, 30)),
            PromptIconKind.Error => VertGrad(bounds, Color.FromRgb(255, 114, 79), Color.FromRgb(235, 75, 46)),
            PromptIconKind.Success => VertGrad(bounds, Color.FromRgb(131, 216, 177), Color.FromRgb(50, 181, 111)),
            _ => null,
        };
    }

    private static void DrawCircleIcon(IGraphicsContext context, Rect bounds, Brush fill, Pen stroke, PathGeometry glyph, Brush glyphBrush, double glyphScale)
    {
        context.FillEllipse(bounds, fill);
        context.DrawEllipse(bounds, stroke);
        DrawGeometry(context, glyph, bounds, glyphBrush, null, glyphScale, Stretch.Uniform);
    }

    private static void DrawShield(IGraphicsContext context, Rect bounds)
    {
        // Shield gradient brushes (_brushShieldLeft/Right) have endpoints in the icon's
        // source 256×256 coordinate system. Push the source-rect transform around the fill
        // so the gradient is interpreted in source space, then draw the stroke from the
        // baked geometry in element-DIP space (so stroke thickness stays at StrokeThickness
        // DIP regardless of the source→bounds scale under Model D).
        var sourceBounds = ShieldOutline.GetBounds();
        var bake = ComputeSourceRectMatrix(sourceBounds, bounds, 1.0, Stretch.Uniform);

        context.Save();
        ApplySourceRectTransform(context, sourceBounds, bounds, 1.0, Stretch.Uniform);
        context.FillPath(ShieldLeft, _brushShieldLeft!);
        context.FillPath(ShieldRight, _brushShieldRight!);
        context.Restore();

        context.DrawPath(ShieldOutline.Transform(bake), _penShieldOutline!);
    }

    private static void DrawCrash(IGraphicsContext context, Rect bounds)
    {
        // Same split as Shield: source-space gradient brushes need the transform during fill,
        // strokes draw from baked geometry to keep thickness in element-DIP.
        var sourceBounds = new Rect(38, 38, 180, 186);
        var bake = ComputeSourceRectMatrix(sourceBounds, bounds, 1.0, Stretch.Uniform);

        context.Save();
        ApplySourceRectTransform(context, sourceBounds, bounds, 1.0, Stretch.Uniform);
        context.FillPath(CrashHorn, _brushCrashHorn!);
        context.FillPath(CrashBody, _brushCrashBody!);
        context.Restore();

        context.DrawPath(CrashHorn.Transform(bake), _penCrashHorn!);
        context.DrawPath(CrashBody.Transform(bake), _penCrashBody!);

        // Inner glyph uses a solid brush (no gradient), so the bake-only path is fine.
        DrawGeometry(context, QuestionGlyph, new Rect(42, 50, 172, 172), _brushCrashGlyph, null, 0.628, Stretch.Uniform, parentBake: bake);
    }

    /// <summary>
    /// Pushes the same source-rect transform that <see cref="ComputeSourceRectMatrix"/>
    /// computes onto the context. Used for fill calls where the brush expects source-space
    /// coordinates (e.g. linear gradients with hardcoded source-coordinate endpoints).
    /// </summary>
    private static void ApplySourceRectTransform(IGraphicsContext context, Rect sourceBounds, Rect bounds, double scale, Stretch stretch)
    {
        var targetRect = new Rect(
            bounds.X + (bounds.Width * (1.0 - scale)) * 0.5,
            bounds.Y + (bounds.Height * (1.0 - scale)) * 0.5,
            bounds.Width * scale,
            bounds.Height * scale);
        ComputeStretchTransform(sourceBounds, targetRect, stretch, out var sx, out var sy, out var tx, out var ty);
        context.Translate(tx, ty);
        context.Scale(sx, sy);
        context.Translate(-sourceBounds.X, -sourceBounds.Y);
    }

    private static void DrawGeometry(IGraphicsContext context, PathGeometry geometry, Rect bounds, Brush? brush, Pen? pen, double scale, Stretch stretch, System.Numerics.Matrix3x2? parentBake = null)
    {
        var geoBounds = geometry.GetBounds();
        if (geoBounds.Width <= 0 || geoBounds.Height <= 0)
        {
            return;
        }

        var bake = ComputeSourceRectMatrix(geoBounds, bounds, scale, stretch);
        // Caller may already be inside a baked source rect (e.g. Crash: glyph nested under
        // body's source rect). Compose so the inner geometry still ends up in window DIPs.
        if (parentBake.HasValue)
        {
            bake *= parentBake.Value;
        }
        var transformed = geometry.Transform(bake);
        if (brush != null)
        {
            context.FillPath(transformed, brush);
        }
        if (pen != null)
        {
            context.DrawPath(transformed, pen);
        }
    }

    /// <summary>
    /// Builds the matrix that maps coordinates in <paramref name="sourceBounds"/> into
    /// <paramref name="bounds"/> shrunken by <paramref name="scale"/> and aligned by
    /// <paramref name="stretch"/>. Used to bake the source-rect transform into a geometry
    /// so subsequent <c>DrawPath</c> draws don't pick up the transform on the context (and
    /// thus don't scale the stroke under Model D).
    /// </summary>
    private static System.Numerics.Matrix3x2 ComputeSourceRectMatrix(Rect sourceBounds, Rect bounds, double scale, Stretch stretch)
    {
        var targetRect = new Rect(
            bounds.X + (bounds.Width * (1.0 - scale)) * 0.5,
            bounds.Y + (bounds.Height * (1.0 - scale)) * 0.5,
            bounds.Width * scale,
            bounds.Height * scale);

        ComputeStretchTransform(sourceBounds, targetRect, stretch, out var sx, out var sy, out var tx, out var ty);

        // [Translate(-sourceOrigin)] × [Scale] × [Translate(target)]
        return
            System.Numerics.Matrix3x2.CreateTranslation((float)-sourceBounds.X, (float)-sourceBounds.Y) *
            System.Numerics.Matrix3x2.CreateScale((float)sx, (float)sy) *
            System.Numerics.Matrix3x2.CreateTranslation((float)tx, (float)ty);
    }

    private static LinearGradientBrush VertGrad(Rect bounds, Color start, Color end)
        => new(
            new Point(bounds.Left, bounds.Top),
            new Point(bounds.Left, bounds.Bottom),
            [new(0, start), new(1, end)]);

    private static void ComputeStretchTransform(Rect geoBounds, Rect destBounds, Stretch stretch, out double scaleX, out double scaleY, out double offsetX, out double offsetY)
    {
        double gw = geoBounds.Width;
        double gh = geoBounds.Height;
        double dw = destBounds.Width;
        double dh = destBounds.Height;

        switch (stretch)
        {
            case Stretch.Fill:
                scaleX = dw / gw;
                scaleY = dh / gh;
                break;

            case Stretch.UniformToFill:
                scaleX = scaleY = Math.Max(dw / gw, dh / gh);
                break;

            default:
                scaleX = scaleY = Math.Min(dw / gw, dh / gh);
                break;
        }

        double scaledW = gw * scaleX;
        double scaledH = gh * scaleY;
        offsetX = destBounds.X + (dw - scaledW) * 0.5;
        offsetY = destBounds.Y + (dh - scaledH) * 0.5;
    }
}
