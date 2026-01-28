using System.Reflection;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.GraphicsBackendTest;

internal sealed class GraphicsBackendTestView : ContentControl
{
    private readonly ScrollViewer _scroll;
    private readonly GraphicsBackendTestCanvas _canvas;

    public GraphicsBackendTestView()
    {
        Padding = new Thickness(12);

        _canvas = new GraphicsBackendTestCanvas();

        _scroll = new ScrollViewer
        {
            Parent = this,
            Content = _canvas,
            Padding = new Thickness(0),
        };

        Content = _scroll;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        _scroll.Measure(availableSize);
        return _scroll.DesiredSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        _scroll.Arrange(bounds);
    }
}

internal sealed class GraphicsBackendTestCanvas : Control
{
    private sealed record TestCase(string Name, Action<IGraphicsContext, Rect> Render);

    private readonly List<TestCase> _tests = new();
    private IImage? _image;
    private IImage? _logo;

    private const double CardMinWidth = 240;
    private const double CardHeight = 220;
    private const double CardGap = 12;
    private const double HeaderHeight = 24;

    public GraphicsBackendTestCanvas()
    {
        BorderThickness = 0;
        Padding = new Thickness(0);

        BuildTests();
    }

    Color border = new Color(30, 30, 30);
    Color background = new Color(230, 230, 230);
    private void BuildTests()
    {
        _tests.Clear();

        _tests.Add(new TestCase("Lines (1px / 2px)", (g, r) =>
        {
            var c1 = Theme.Palette.Accent;
            var c2 = border;
            g.DrawLine(new Point(r.X + 8, r.Y + 16), new Point(r.Right - 8, r.Y + 16), c1, 1);
            g.DrawLine(new Point(r.X + 8, r.Y + 32), new Point(r.Right - 8, r.Y + 32), c1, 2);
            g.DrawLine(new Point(r.X + 8, r.Y + 48), new Point(r.Right - 8, r.Y + 48), c2.WithAlpha(0xAA), 1);
            g.DrawLine(new Point(r.X + 8, r.Y + 64), new Point(r.Right - 8, r.Y + 88), c2, 1);
        }));

        _tests.Add(new TestCase("Rects", (g, r) =>
        {

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), border, 1);
            g.FillRectangle(new Rect(r.X + 95, r.Y + 10, 70, 50), Theme.Palette.Accent.WithAlpha(0x66));
            g.DrawRectangle(new Rect(r.X + 95, r.Y + 10, 70, 50), border, 1);

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), border, 2);
        }));

        _tests.Add(new TestCase("RoundedRect", (g, r) =>
        {

            var bg = Theme.Palette.Accent.WithAlpha(0x44);
            g.FillRoundedRectangle(new Rect(r.X + 10, r.Y + 10, 155, 50), 8, 8, bg);
            g.DrawRoundedRectangle(new Rect(r.X + 10, r.Y + 10, 155, 50), 8, 8, border, 1);

            g.FillRoundedRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), 18, 18, bg);
            g.DrawRoundedRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), 18, 18, border, 2);
        }));

        _tests.Add(new TestCase("Ellipse / Stroke", (g, r) =>
        {

            var fill = Theme.Palette.Accent.WithAlpha(0x44);
            var outer = new Rect(r.X + 12, r.Y + 12, 60, 60);
            g.FillEllipse(outer, fill);
            g.DrawEllipse(outer, border, 1);

            var outer2 = new Rect(r.X + 96, r.Y + 12, 60, 60);
            g.FillEllipse(outer2, fill);
            g.DrawEllipse(outer2, border, 2);

            var thin = new Rect(r.X + 12, r.Y + 90, 144, 40);
            g.FillEllipse(thin, fill);
            g.DrawEllipse(thin, Theme.Palette.Accent, 1);
        }));

        _tests.Add(new TestCase("Clip", (g, r) =>
        {

            var clip = new Rect(r.X + 10, r.Y + 12, 80, 80);
            g.DrawRectangle(clip, border, 1);

            g.Save();
            g.SetClip(clip);
            g.FillRectangle(new Rect(r.X + 10, r.Y + 12, 160, 160), Theme.Palette.Accent.WithAlpha(0x55));
            g.DrawLine(new Point(r.X + 10, r.Y + 12), new Point(r.Right - 10, r.Bottom - 10), Theme.Palette.WindowText, 2);
            g.Restore();

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 100, 160, 30), border, 1);
        }));

        _tests.Add(new TestCase("Save/Restore + Translate", (g, r) =>
        {

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), border, 1);

            g.Save();
            g.Translate(90, 0);
            g.FillRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), Theme.Palette.Accent.WithAlpha(0x55));
            g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), border, 1);
            g.Restore();

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), border, 1);
        }));

        _tests.Add(new TestCase("Alpha Primitives (A<255)", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var fill = accent.WithAlpha(0x66);
            var stroke = accent.WithAlpha(0x99);

            // Semi-transparent fill + stroke should render consistently across backends.
            var rr1 = new Rect(r.X + 10, r.Y + 10, 70, 50);
            g.FillRoundedRectangle(rr1, 8, 8, fill);
            g.DrawRoundedRectangle(rr1, 8, 8, stroke, 2);

            var rr2 = new Rect(r.X + 95, r.Y + 10, 70, 50);
            g.FillRectangle(rr2, fill);
            g.DrawRectangle(rr2, stroke, 2);

            var e1 = new Rect(r.X + 10, r.Y + 75, 60, 60);
            g.FillEllipse(e1, fill);
            g.DrawEllipse(e1, stroke, 2);

            var e2 = new Rect(r.X + 95, r.Y + 75, 70, 50);
            g.FillEllipse(e2, fill);
            g.DrawEllipse(e2, stroke, 2);

            // Non-axis-aligned line with alpha to validate AA + compositing.
            g.DrawLine(new Point(r.X + 10, r.Bottom - 18), new Point(r.Right - 10, r.Bottom - 48), stroke, 2);
        }));

        _tests.Add(new TestCase("Text Align", (g, r) =>
        {
            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            var box = new Rect(r.X + 10, r.Y + 10, 155, 40);
            g.DrawRectangle(box, border, 1);
            g.DrawText("Left", box, font, Theme.Palette.WindowText, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

            var box2 = new Rect(r.X + 10, r.Y + 55, 155, 40);
            g.DrawRectangle(box2, border, 1);
            g.DrawText("Center", box2, font, Theme.Palette.WindowText, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

            var box3 = new Rect(r.X + 10, r.Y + 100, 155, 40);
            g.DrawRectangle(box3, border, 1);
            g.DrawText("Right", box3, font, Theme.Palette.WindowText, TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);
        }));

        _tests.Add(new TestCase("Text Wrap/Measure", (g, r) =>
        {
            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            var box = new Rect(r.X + 10, r.Y + 10, 155, 75);
            g.DrawRectangle(box, border, 1);
            g.DrawText("Wrap test: The quick brown fox jumps over the lazy dog.", box, font, Theme.Palette.WindowText,
                TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap);

            var m = g.MeasureText("MeasureText()", font);
            g.DrawText($"Measured: {m.Width:0.0}x{m.Height:0.0}", new Rect(r.X + 10, r.Y + 95, 155, 40), font,
                Theme.Palette.WindowText, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }));

        _tests.Add(new TestCase("Image (dest/source)", (g, r) =>
        {
            EnsureImage(g);
            if (_image == null)
            {
                return;
            }

            var dest = new Rect(r.X + 10, r.Y + 10, 80, 80);
            g.DrawImage(_image, dest);
            g.DrawRectangle(dest, border, 1);

            var dest2 = new Rect(r.X + 95, r.Y + 10, 70, 70);
            var src = new Rect(30, 30, 120, 120);
            g.DrawImage(_image, dest2, src);
            g.DrawRectangle(dest2, border, 1);

            g.DrawText("april.jpg", new Rect(r.X + 10, r.Y + 95, 155, 30), GetFont(), Theme.Palette.WindowText,
                TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }));

        _tests.Add(new TestCase("Image ScaleQuality Down (aligned/fractional)", (g, r) =>
        {
            EnsureLogo(g);
            if (_logo == null)
            {
                return;
            }

            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            var modes = new[]
            {
                ImageScaleQuality.Fast,
                ImageScaleQuality.Normal,
                ImageScaleQuality.HighQuality,
            };

            double colW = r.Width / 3;
            double rowH = r.Height / 2;
            double pad = 6;
            double thumb = Math.Max(10, Math.Min(44, Math.Min(colW, rowH) - pad * 2 - 14));

            var srcAligned = new Rect(30, 30, 160, 160);
            var srcFractional = new Rect(30.25, 30.75, 160.5, 159.5);

            for (int col = 0; col < 3; col++)
            {
                var colRect = new Rect(r.X + col * colW, r.Y, colW, r.Height);

                g.DrawText(modes[col].ToString(), new Rect(colRect.X + 2, colRect.Y, colRect.Width - 4, 14),
                    font, Theme.Palette.WindowText, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);


                var cell = new Rect(colRect.X, colRect.Y + 16, colRect.Width, rowH - 16);

                var destAligned = new Rect(cell.X + pad, cell.Y + pad, thumb, thumb);

                var destFractional = new Rect(cell.X + pad, cell.Y + pad + rowH, thumb, thumb);

                var prev = g.ImageScaleQuality;
                g.ImageScaleQuality = modes[col];
                try
                {
                    g.DrawImage(_logo, destAligned, srcAligned);
                    g.DrawRectangle(destAligned, border.WithAlpha(0xCC), 1);

                    g.DrawImage(_logo, destFractional, srcFractional);
                    g.DrawRectangle(destFractional, border.WithAlpha(0xCC), 1);
                }
                finally
                {
                    g.ImageScaleQuality = prev;
                }
            }
        }));
        _tests.Add(new TestCase("Image ScaleQuality Up (aligned/fractional)", (g, r) =>
        {
            EnsureLogo(g);
            if (_logo == null)
            {
                return;
            }

            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            var modes = new[]
            {
                ImageScaleQuality.Fast,
                ImageScaleQuality.Normal,
                ImageScaleQuality.HighQuality,
            };

            double colW = r.Width / 3;
            double rowH = r.Height / 2;
            double pad = 6;
            double thumb = Math.Max(10, Math.Min(44, Math.Min(colW, rowH) - pad * 2 - 14));

            var srcAligned = new Rect(30, 30, 160, 160);
            var srcFractional = new Rect(30.25, 30.75, 160.5, 159.5);

            for (int col = 0; col < 3; col++)
            {
                var colRect = new Rect(r.X + col * colW, r.Y, colW, r.Height);

                g.DrawText(modes[col].ToString(), new Rect(colRect.X + 2, colRect.Y, colRect.Width - 4, 14),
                    font, Theme.Palette.WindowText, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                var cell = new Rect(colRect.X, colRect.Y + 16, colRect.Width, rowH - 16);

                var destAligned = new Rect(cell.X + pad, cell.Y + pad, thumb * 1.5, thumb * 1.5); // upscale (src < dest)

                var destFractional = new Rect(cell.X + pad, cell.Y + pad + rowH, thumb * 1.5, thumb * 1.5);

                var prev = g.ImageScaleQuality;
                g.ImageScaleQuality = modes[col];
                try
                {
                    g.DrawImage(_logo, destAligned, srcAligned);
                    g.DrawRectangle(destAligned, border.WithAlpha(0xCC), 1);

                    g.DrawImage(_logo, destFractional, srcFractional);
                    g.DrawRectangle(destFractional, border.WithAlpha(0xCC), 1);
                }
                finally
                {
                    g.ImageScaleQuality = prev;
                }
            }
        }));
    }

    private void EnsureImage(IGraphicsContext g)
    {
        if (_image != null)
        {
            return;
        }

        // Use an embedded resource so the test app is self-contained.
        var source = ImageSource.FromResource(Assembly.GetExecutingAssembly(), "Aprillz.MewUI.GraphicsBackendTest.april.jpg");
        _image = source.CreateImage(Application.Current.GraphicsFactory);
    }

    private void EnsureLogo(IGraphicsContext g)
    {
        if (_logo != null)
        {
            return;
        }

        // Use an embedded resource so the test app is self-contained.
        var source = ImageSource.FromResource(Assembly.GetExecutingAssembly(), "Aprillz.MewUI.GraphicsBackendTest.logo_c-256.png");
        _logo = source.CreateImage(Application.Current.GraphicsFactory);
    }
    protected override Size MeasureContent(Size availableSize)
    {
        double width = double.IsPositiveInfinity(availableSize.Width) ? 900 : Math.Max(0, availableSize.Width);
        int columns = Math.Max(1, (int)Math.Floor((width + CardGap) / (CardMinWidth + CardGap)));
        double cardWidth = Math.Max(CardMinWidth, (width - (columns - 1) * CardGap) / columns);

        int rows = (int)Math.Ceiling(_tests.Count / (double)columns);
        double height = rows * CardHeight + Math.Max(0, rows - 1) * CardGap;
        return new Size(cardWidth * columns + (columns - 1) * CardGap, height);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _image?.Dispose();
        _image = null;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = Theme;
        var bounds = Bounds;
        var dpiScale = GetDpi() / 96.0;

        // Background
        context.FillRectangle(bounds, new Color(255, 255, 255));

        double width = Math.Max(0, bounds.Width);
        int columns = Math.Max(1, (int)Math.Floor((width + CardGap) / (CardMinWidth + CardGap)));
        double cardWidth = Math.Round(Math.Max(CardMinWidth, (width - (columns - 1) * CardGap) / columns));

        using var measure = BeginTextMeasurement();
        var font = measure.Font;

        var header = $"Backend: {Application.Current.GraphicsFactory.Backend}  DPI: {GetDpi()}  DpiScale: {dpiScale:0.00}";
        context.DrawText(header, new Rect(bounds.X, bounds.Y, bounds.Width, HeaderHeight), font, theme.Palette.WindowText,
            TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

        double x0 = bounds.X;
        double y0 = bounds.Y + HeaderHeight + 8;

        for (int i = 0; i < _tests.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;

            double x = x0 + col * (cardWidth + CardGap);
            double y = y0 + row * (CardHeight + CardGap);
            var card = new Rect(x, y, cardWidth, CardHeight);

            // Card chrome
            context.FillRoundedRectangle(card, 8, 8, background);
            context.DrawRoundedRectangle(card, 8, 8, border, 1);

            var nameRect = new Rect(card.X + 10, card.Y + 8, card.Width - 20, 22);
            context.DrawText(_tests[i].Name, nameRect, font, theme.Palette.WindowText,
                TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

            var content = new Rect(card.X + 10, card.Y + 34, card.Width - 20, card.Height - 44);
            context.Save();
            context.SetClip(content);
            _tests[i].Render(context, content);
            context.Restore();
        }
    }
}
