using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.SkiaSharp.Sample;

internal sealed class SkiaSampleView : UserControl
{
    private readonly Window _window;
    private readonly ObservableValue<string> _backendStatus = new();
    private readonly ObservableValue<string> _canvasStatus = new("Canvas: -");
    private readonly ObservableValue<bool> _ignorePixelScaling = new(false);
    private readonly DispatcherTimer _animationTimer = new();

    private SKElement? _skiaElement;
    private float _phase;

    public SkiaSampleView(Window window)
    {
        _window = window;
        InitializeSample();
        Build();
    }

    protected override Element? OnBuild() =>
        new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(8)
            .Content(BuildContent());

    private void InitializeSample()
    {
        if (_window.GraphicsFactory.TryGetGraphicsService<ISkiaGpuControlFactory>() != null)
        {
            _backendStatus.Value = $"GPU backend: {_window.GraphicsFactory.Backend}";
            _animationTimer
                .IntervalMs(16)
                .OnTick(() =>
                {
                    _phase += 0.035f;
                    _skiaElement?.InvalidateSurface();
                })
                .Start();
        }
        else
        {
            _backendStatus.Value =
                $"Current backend: {_window.GraphicsFactory.Backend}. " +
                "This sample requires a MewVG GPU backend (Win32 GL, X11 GL, macOS Metal).";
        }
    }

    private FrameworkElement BuildContent()
    {
        if (_window.GraphicsFactory.TryGetGraphicsService<ISkiaGpuControlFactory>() == null)
        {
            return new StackPanel()
                .Vertical()
                .Spacing(12)
                .Children(
                    SectionHeader(
                        "SkiaSharp Sample",
                        "This sample hosts a retained GPU-backed SKElement and uses the familiar PaintSurface pattern from SkiaSharp."),

                    Card(
                        new StackPanel()
                            .Vertical()
                            .Spacing(8)
                            .Children(
                                new TextBlock().BindText(_backendStatus),
                                new TextBlock()
                                    .Text("Launch this sample on a supported MewVG backend.")
                                    .FontSize(11)
                            ),
                        minWidth: 640
                    )
                );
        }

        var element = new SKElement
        {
            IgnorePixelScaling = _ignorePixelScaling.Value
        }
        .Width(720)
        .Height(420)
        .BorderBrush(Color.FromRgb(210, 218, 232))
        .BorderThickness(1)
        .CornerRadius(18)
        .Background(Color.White);

        _skiaElement = element;
        element.PaintSurface += (_, e) => PaintSample(e);

        return new StackPanel()
            .Vertical()
            .Spacing(16)
            .Children(
                SectionHeader(
                    "SkiaSharp GPU Sample",
                    "Dedicated sample for Aprillz.MewUI.Controls.SKElement. The surface stays GPU-backed and animates through InvalidateSurface()."),

                Card(
                    new StackPanel()
                        .Vertical()
                        .Spacing(12)
                        .Children(
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Children(
                                    new CheckBox()
                                        .Content("Ignore pixel scaling")
                                        .BindIsChecked(_ignorePixelScaling)
                                        .OnCheckedChanged(_ => ApplyPixelScalingMode()),

                                    new Button()
                                        .Content("Invalidate")
                                        .OnClick(() => _skiaElement?.InvalidateSurface())
                                ),

                            element,

                            new TextBlock().BindText(_backendStatus),
                            new TextBlock().BindText(_canvasStatus),
                            new TextBlock()
                                .Text("API surface: PaintSurface, CanvasSize, IgnorePixelScaling, InvalidateSurface().")
                                .FontSize(11)
                        ),
                    minWidth: 760
                )
            );
    }

    private void ApplyPixelScalingMode()
    {
        if (_skiaElement is null)
        {
            return;
        }

        _skiaElement.IgnorePixelScaling = _ignorePixelScaling.Value;
        _skiaElement.InvalidateSurface();
    }

    private FrameworkElement SectionHeader(string title, string description) =>
        new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new TextBlock()
                    .Text(title)
                    .FontSize(22)
                    .Bold(),

                new TextBlock()
                    .Text(description)
                    .FontSize(12)
            );

    private FrameworkElement Card(FrameworkElement content, double minWidth = 320) =>
        new Border()
            .MinWidth(minWidth)
            .Padding(16)
            .CornerRadius(14)
            .BorderThickness(1)
            .Child(content);

    private void PaintSample(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float width = e.Info.Width;
        float height = e.Info.Height;
        float minSide = MathF.Min(width, height);
        float t = _phase;

        canvas.Clear(SKColors.Transparent);

        using var background = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [new SKColor(246, 249, 255), new SKColor(228, 237, 255)],
                null,
                SKShaderTileMode.Clamp)
        };

        using var cardStroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(1.5f, minSide * 0.012f),
            Color = new SKColor(113, 145, 230, 90)
        };

        var panelRect = new SKRect(0, 0, width, height);
        var innerRect = panelRect;
        innerRect.Inflate(-1.5f, -1.5f);
        canvas.DrawRoundRect(panelRect, 28, 28, background);
        canvas.DrawRoundRect(innerRect, 26, 26, cardStroke);

        using var wavePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(2f, minSide * 0.018f),
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(39, 92, 210, 220)
        };

        using var wave = new SKPath();
        float baseline = height * 0.70f;
        wave.MoveTo(width * 0.08f, baseline);
        for (int i = 0; i <= 56; i++)
        {
            float x = width * 0.08f + (width * 0.84f / 56f) * i;
            float amplitude = minSide * 0.10f;
            float y = baseline + MathF.Sin(t * 1.65f + i * 0.36f) * amplitude;
            wave.LineTo(x, y);
        }
        canvas.DrawPath(wave, wavePaint);

        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(88, 133, 255, 70)
        };

        using var dotPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(28, 73, 194, 255)
        };

        var center = new SKPoint(width * 0.5f, height * 0.42f);
        float orbit = minSide * 0.19f;
        float radius = minSide * 0.09f;

        for (int i = 0; i < 3; i++)
        {
            float angle = t + i * 2.0943952f;
            float x = center.X + MathF.Cos(angle) * orbit;
            float y = center.Y + MathF.Sin(angle * 1.17f) * orbit * 0.6f;

            canvas.DrawCircle(x, y, radius * 1.35f, glowPaint);
            canvas.DrawCircle(x, y, radius, dotPaint);
        }

        using var accentPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(248, 118, 74, 235)
        };

        using var accentText = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(40, 56, 104, 240)
        };
        using var accentFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), MathF.Max(20f, minSide * 0.11f));

        float accentWidth = width * 0.18f;
        float accentHeight = minSide * 0.07f;
        float accentX = width * 0.11f + (MathF.Sin(t * 1.4f) * width * 0.05f);
        float accentY = height * 0.18f;
        canvas.DrawRoundRect(new SKRect(accentX, accentY, accentX + accentWidth, accentY + accentHeight), 14, 14, accentPaint);
        canvas.DrawText("SKElement", width * 0.10f, height * 0.24f, SKTextAlign.Left, accentFont, accentText);

        _backendStatus.Value = $"GPU backend: {_window.GraphicsFactory.Backend} | IgnorePixelScaling: {_ignorePixelScaling.Value}";
        _canvasStatus.Value = $"Canvas {e.Info.Width}x{e.Info.Height} | Raw {e.RawInfo.Width}x{e.RawInfo.Height}";
    }

    protected override void OnDispose()
    {
        _animationTimer.Stop();
        base.OnDispose();
    }
}
