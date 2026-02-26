using System.Reflection;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

using GradientStop = Aprillz.MewUI.Rendering.GradientStop;

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
            Content = _canvas,
            Padding = new Thickness(0),
        };
        AttachChild(_scroll);
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


    private void BuildTests()
    {
        _tests.Clear();

        _tests.Add(new TestCase("Lines (1px / 2px)", (g, r) =>
        {
            var c1 = Theme.Palette.Accent;
            var c2 = Theme.Palette.ControlBorder;
            g.DrawLine(new Point(r.X + 8, r.Y + 16), new Point(r.Right - 8, r.Y + 16), c1, 1);
            g.DrawLine(new Point(r.X + 8, r.Y + 32), new Point(r.Right - 8, r.Y + 32), c1, 2);
            g.DrawLine(new Point(r.X + 8, r.Y + 48), new Point(r.Right - 8, r.Y + 48), c2.WithAlpha(0xAA), 1);
            g.DrawLine(new Point(r.X + 8, r.Y + 64), new Point(r.Right - 8, r.Y + 88), c2, 1);
        }));

        _tests.Add(new TestCase("Rects", (g, r) =>
        {

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), Theme.Palette.Accent, 1);
            g.FillRectangle(new Rect(r.X + 95, r.Y + 10, 70, 50), Theme.Palette.Accent.WithAlpha(0x66));
            g.DrawRectangle(new Rect(r.X + 95, r.Y + 10, 70, 50), Theme.Palette.Accent, 1);

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), Theme.Palette.Accent, 2);
        }));

        _tests.Add(new TestCase("RoundedRect", (g, r) =>
        {

            var bg = Theme.Palette.Accent.WithAlpha(0x44);
            g.FillRoundedRectangle(new Rect(r.X + 10, r.Y + 10, 155, 50), 8, 8, bg);
            g.DrawRoundedRectangle(new Rect(r.X + 10, r.Y + 10, 155, 50), 8, 8, Theme.Palette.Accent, 1);

            g.FillRoundedRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), 18, 18, bg);
            g.DrawRoundedRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), 18, 18, Theme.Palette.Accent, 2);
        }));

        _tests.Add(new TestCase("Ellipse / Stroke", (g, r) =>
        {

            var fill = Theme.Palette.Accent.WithAlpha(0x44);
            var outer = new Rect(r.X + 12, r.Y + 12, 60, 60);
            g.FillEllipse(outer, fill);
            g.DrawEllipse(outer, Theme.Palette.Accent, 1);

            var outer2 = new Rect(r.X + 96, r.Y + 12, 60, 60);
            g.FillEllipse(outer2, fill);
            g.DrawEllipse(outer2, Theme.Palette.Accent, 2);

            var thin = new Rect(r.X + 12, r.Y + 90, 144, 40);
            g.FillEllipse(thin, fill);
            g.DrawEllipse(thin, Theme.Palette.Accent, 1);
        }));

        _tests.Add(new TestCase("Clip", (g, r) =>
        {

            var clip = new Rect(r.X + 10, r.Y + 12, 80, 80);
            g.DrawRectangle(clip, Theme.Palette.Accent, 1);

            g.Save();
            g.SetClip(clip);
            g.FillRectangle(new Rect(r.X + 10, r.Y + 12, 160, 160), Theme.Palette.Accent.WithAlpha(0x55));
            g.DrawLine(new Point(r.X + 10, r.Y + 12), new Point(r.Right - 10, r.Bottom - 10), Theme.Palette.WindowText, 2);
            g.Restore();

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 100, 160, 30), Theme.Palette.Accent, 1);
        }));

        _tests.Add(new TestCase("Save/Restore + Translate", (g, r) =>
        {

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), Theme.Palette.Accent, 1);

            g.Save();
            g.Translate(90, 0);
            g.FillRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), Theme.Palette.Accent.WithAlpha(0x55));
            g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 70, 50), Theme.Palette.Accent, 1);
            g.Restore();

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 75, 155, 50), Theme.Palette.Accent, 1);
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
            g.DrawRectangle(box, Theme.Palette.Accent, 1);
            g.DrawText("Left", box, font, Theme.Palette.WindowText, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

            var box2 = new Rect(r.X + 10, r.Y + 55, 155, 40);
            g.DrawRectangle(box2, Theme.Palette.Accent, 1);
            g.DrawText("Center", box2, font, Theme.Palette.WindowText, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

            var box3 = new Rect(r.X + 10, r.Y + 100, 155, 40);
            g.DrawRectangle(box3, Theme.Palette.Accent, 1);
            g.DrawText("Right", box3, font, Theme.Palette.WindowText, TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);
        }));

        _tests.Add(new TestCase("Text Wrap/Measure", (g, r) =>
        {
            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            var box = new Rect(r.X + 10, r.Y + 10, 155, 75);
            g.DrawRectangle(box, Theme.Palette.Accent, 1);
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
            g.DrawRectangle(dest, Theme.Palette.Accent, 1);

            var dest2 = new Rect(r.X + 95, r.Y + 10, 70, 70);
            var src = new Rect(30, 30, 120, 120);
            g.DrawImage(_image, dest2, src);
            g.DrawRectangle(dest2, Theme.Palette.Accent, 1);

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
                    g.DrawRectangle(destAligned, Theme.Palette.Accent.WithAlpha(0xCC), 1);

                    g.DrawImage(_logo, destFractional, srcFractional);
                    g.DrawRectangle(destFractional, Theme.Palette.Accent.WithAlpha(0xCC), 1);
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
                    g.DrawRectangle(destAligned, Theme.Palette.Accent.WithAlpha(0xCC), 1);

                    g.DrawImage(_logo, destFractional, srcFractional);
                    g.DrawRectangle(destFractional, Theme.Palette.Accent.WithAlpha(0xCC), 1);
                }
                finally
                {
                    g.ImageScaleQuality = prev;
                }
            }
        }));

        // ── PathGeometry: Basic shapes ──────────────────────────────────────
        _tests.Add(new TestCase("Path: Triangle + Star", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var fill = accent.WithAlpha(0x44);

            // Triangle
            var tri = new PathGeometry();
            tri.MoveTo(r.X + 10, r.Y + 80);
            tri.LineTo(r.X + 50, r.Y + 10);
            tri.LineTo(r.X + 90, r.Y + 80);
            tri.Close();
            g.FillPath(tri, fill);
            g.DrawPath(tri, accent, 2);

            // 5-pointed star
            var star = new PathGeometry();
            double cx = r.X + 150, cy = r.Y + 50, outerR = 40, innerR = 16;
            for (int i = 0; i < 10; i++)
            {
                double angle = Math.PI / 2 + i * Math.PI / 5;
                double rad = i % 2 == 0 ? outerR : innerR;
                double px = cx + rad * Math.Cos(angle);
                double py = cy - rad * Math.Sin(angle);
                if (i == 0) star.MoveTo(px, py);
                else star.LineTo(px, py);
            }
            star.Close();
            g.FillPath(star, fill);
            g.DrawPath(star, accent, 1);
        }));

        // ── PathGeometry: Bezier curves ─────────────────────────────────────
        _tests.Add(new TestCase("Path: Bezier + QuadTo", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var c2 = Theme.Palette.ControlBorder;

            // Cubic bezier
            var cubic = new PathGeometry();
            cubic.MoveTo(r.X + 10, r.Y + 70);
            cubic.BezierTo(r.X + 40, r.Y + 5, r.X + 80, r.Y + 5, r.X + 110, r.Y + 70);
            g.DrawPath(cubic, accent, 2);

            // Quad bezier
            var quad = new PathGeometry();
            quad.MoveTo(r.X + 10, r.Y + 130);
            quad.QuadTo(r.X + 60, r.Y + 70, r.X + 110, r.Y + 130);
            g.DrawPath(quad, c2, 2);

            // Multiple connected quads (wavy line)
            var wavy = new PathGeometry();
            wavy.MoveTo(r.X + 120, r.Y + 30);
            wavy.QuadTo(r.X + 145, r.Y + 10, r.X + 170, r.Y + 30);
            wavy.QuadTo(r.X + 195, r.Y + 50, r.X + 220, r.Y + 30);
            g.DrawPath(wavy, accent, 1.5);

            // Filled closed bezier shape
            var blob = new PathGeometry();
            blob.MoveTo(r.X + 130, r.Y + 100);
            blob.BezierTo(r.X + 130, r.Y + 60, r.X + 210, r.Y + 60, r.X + 210, r.Y + 100);
            blob.BezierTo(r.X + 210, r.Y + 140, r.X + 130, r.Y + 140, r.X + 130, r.Y + 100);
            blob.Close();
            g.FillPath(blob, accent.WithAlpha(0x44));
            g.DrawPath(blob, accent, 1);
        }));

        // ── PathGeometry: Arc ───────────────────────────────────────────────
        _tests.Add(new TestCase("Path: Arc / ArcTo", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var c2 = Theme.Palette.ControlBorder;

            // Arc (center parameterization) - semicircle
            var arc1 = new PathGeometry();
            arc1.Arc(r.X + 55, r.Y + 50, 40, 40, 0, Math.PI);
            g.DrawPath(arc1, accent, 2);

            // Arc - 3/4 circle
            var arc2 = new PathGeometry();
            arc2.Arc(r.X + 55, r.Y + 50, 25, 25, 0, Math.PI * 1.5);
            g.DrawPath(arc2, c2, 1.5);

            // ArcTo (tangential, canvas-style)
            var arcTo = new PathGeometry();
            arcTo.MoveTo(r.X + 120, r.Y + 20);
            arcTo.ArcTo(r.X + 200, r.Y + 20, r.X + 200, r.Y + 90, 20);
            arcTo.LineTo(r.X + 200, r.Y + 90);
            g.DrawPath(arcTo, accent, 2);

            // Elliptical arc
            var ellArc = new PathGeometry();
            ellArc.Arc(r.X + 160, r.Y + 120, 50, 25, 0, Math.PI * 1.5);
            g.DrawPath(ellArc, c2, 1.5);
        }));

        // ── PathGeometry: SvgArcTo ──────────────────────────────────────────
        _tests.Add(new TestCase("Path: SvgArcTo", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var c2 = Theme.Palette.ControlBorder;
            var fill = accent.WithAlpha(0x33);

            // Large-arc sweep variations
            // largeArc=false, sweep=true
            var p1 = new PathGeometry();
            p1.MoveTo(r.X + 30, r.Y + 50);
            p1.SvgArcTo(30, 30, 0, false, true, r.X + 90, r.Y + 50);
            g.DrawPath(p1, accent, 2);

            // largeArc=true, sweep=true
            var p2 = new PathGeometry();
            p2.MoveTo(r.X + 30, r.Y + 50);
            p2.SvgArcTo(30, 30, 0, true, true, r.X + 90, r.Y + 50);
            g.DrawPath(p2, c2, 1.5);

            // Rotated elliptical SVG arc
            var p3 = new PathGeometry();
            p3.MoveTo(r.X + 120, r.Y + 30);
            p3.SvgArcTo(50, 25, 30, false, true, r.X + 210, r.Y + 80);
            g.DrawPath(p3, accent, 2);

            // Closed pill shape using two SvgArcTo
            var pill = new PathGeometry();
            pill.MoveTo(r.X + 130, r.Y + 110);
            pill.SvgArcTo(15, 15, 0, false, true, r.X + 130, r.Y + 140);
            pill.LineTo(r.X + 200, r.Y + 140);
            pill.SvgArcTo(15, 15, 0, false, true, r.X + 200, r.Y + 110);
            pill.Close();
            g.FillPath(pill, fill);
            g.DrawPath(pill, accent, 1);
        }));

        // ── PathGeometry: Static factories ──────────────────────────────────
        _tests.Add(new TestCase("Path: FromRect/Ellipse/Circle", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var fill = accent.WithAlpha(0x33);

            var pRect = PathGeometry.FromRect(r.X + 10, r.Y + 10, 70, 40);
            g.FillPath(pRect, fill);
            g.DrawPath(pRect, accent, 1);

            var pRound = PathGeometry.FromRoundedRect(new Rect(r.X + 90, r.Y + 10, 70, 40), 10);
            g.FillPath(pRound, fill);
            g.DrawPath(pRound, accent, 1);

            var pEllipse = PathGeometry.FromEllipse(new Rect(r.X + 10, r.Y + 65, 60, 40));
            g.FillPath(pEllipse, fill);
            g.DrawPath(pEllipse, accent, 1);

            var pCircle = PathGeometry.FromCircle(r.X + 130, r.Y + 85, 20);
            g.FillPath(pCircle, fill);
            g.DrawPath(pCircle, accent, 1);

            // Rounded rect with different corner radii
            var pCorners = PathGeometry.FromRoundedRect(new Rect(r.X + 10, r.Y + 115, 150, 35), 0, 12, 0, 12);
            g.FillPath(pCorners, fill);
            g.DrawPath(pCorners, accent, 1);
        }));

        // ── StrokeStyle: LineCap ────────────────────────────────────────────
        _tests.Add(new TestCase("StrokeStyle: LineCap", (g, r) =>
        {
            var factory = Application.Current.GraphicsFactory;
            var caps = new[] { StrokeLineCap.Flat, StrokeLineCap.Round, StrokeLineCap.Square };
            var labels = new[] { "Flat", "Round", "Square" };

            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            for (int i = 0; i < 3; i++)
            {
                double y = r.Y + 15 + i * 45;
                using var pen = factory.CreatePen(Theme.Palette.Accent, 8, new StrokeStyle { LineCap = caps[i] });
                g.DrawLine(new Point(r.X + 60, y), new Point(r.X + r.Width - 20, y), pen);
                // Reference line showing exact endpoints
                g.DrawLine(new Point(r.X + 60, y), new Point(r.X + 60, y), Theme.Palette.ControlBorder, 1);
                g.DrawText(labels[i], new Rect(r.X + 4, y - 8, 54, 16), font, Theme.Palette.WindowText,
                    TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }));

        // ── StrokeStyle: LineJoin ───────────────────────────────────────────
        _tests.Add(new TestCase("StrokeStyle: LineJoin", (g, r) =>
        {
            var factory = Application.Current.GraphicsFactory;
            var joins = new[] { StrokeLineJoin.Miter, StrokeLineJoin.Round, StrokeLineJoin.Bevel };
            var labels = new[] { "Miter", "Round", "Bevel" };

            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            double colW = (r.Width - 10) / 3;
            for (int i = 0; i < 3; i++)
            {
                double cx = r.X + 5 + i * colW + colW / 2;
                using var pen = factory.CreatePen(Theme.Palette.Accent, 6, new StrokeStyle { LineJoin = joins[i], MiterLimit = 10 });
                var zigzag = new PathGeometry();
                zigzag.MoveTo(cx - 25, r.Y + 100);
                zigzag.LineTo(cx, r.Y + 20);
                zigzag.LineTo(cx + 25, r.Y + 100);
                g.DrawPath(zigzag, pen);

                g.DrawText(labels[i], new Rect(cx - colW / 2, r.Y + 110, colW, 16), font, Theme.Palette.WindowText,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }));

        // ── StrokeStyle: DashArray ──────────────────────────────────────────
        _tests.Add(new TestCase("StrokeStyle: DashArray", (g, r) =>
        {
            var factory = Application.Current.GraphicsFactory;

            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            var patterns = new (string Label, double[] Dashes)[]
            {
                ("Dash [4,2]", [4, 2]),
                ("Dot [1,2]", [1, 2]),
                ("DashDot [4,2,1,2]", [4, 2, 1, 2]),
                ("Long [8,3,2,3]", [8, 3, 2, 3]),
            };

            for (int i = 0; i < patterns.Length; i++)
            {
                double y = r.Y + 15 + i * 35;
                using var pen = factory.CreatePen(Theme.Palette.Accent, 2, new StrokeStyle
                {
                    DashArray = patterns[i].Dashes,
                    LineCap = StrokeLineCap.Flat,
                });
                g.DrawLine(new Point(r.X + 10, y + 14), new Point(r.X + r.Width - 10, y + 14), pen);
                g.DrawText(patterns[i].Label, new Rect(r.X + 10, y, r.Width - 20, 14), font, Theme.Palette.WindowText,
                    TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }));

        // ── StrokeStyle: IPen on shapes ─────────────────────────────────────
        _tests.Add(new TestCase("IPen: Rect/RoundRect/Ellipse", (g, r) =>
        {
            var factory = Application.Current.GraphicsFactory;
            using var dashPen = factory.CreatePen(Theme.Palette.Accent, 2, new StrokeStyle
            {
                DashArray = new double[] { 6, 3 },
                LineCap = StrokeLineCap.Round,
                LineJoin = StrokeLineJoin.Round,
            });
            using var thickPen = factory.CreatePen(Theme.Palette.ControlBorder, 3, new StrokeStyle
            {
                LineCap = StrokeLineCap.Square,
                LineJoin = StrokeLineJoin.Bevel,
            });

            g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 80, 50), dashPen);
            g.DrawRoundedRectangle(new Rect(r.X + 100, r.Y + 10, 80, 50), 10, 10, dashPen);
            g.DrawEllipse(new Rect(r.X + 10, r.Y + 75, 70, 60), thickPen);

            // IPen path
            var path = new PathGeometry();
            path.MoveTo(r.X + 110, r.Y + 80);
            path.LineTo(r.X + 150, r.Y + 75);
            path.LineTo(r.X + 180, r.Y + 110);
            path.LineTo(r.X + 140, r.Y + 140);
            path.Close();
            g.DrawPath(path, dashPen);
        }));

        // ── Linear Gradient Brush ───────────────────────────────────────────
        _tests.Add(new TestCase("LinearGradientBrush", (g, r) =>
        {
            var factory = Application.Current.GraphicsFactory;

            // Horizontal gradient fill rect
            var rect1 = new Rect(r.X + 10, r.Y + 10, r.Width - 20, 35);
            using var hBrush = factory.CreateLinearGradientBrush(
                new Point(rect1.X, rect1.Y), new Point(rect1.Right, rect1.Y),
                new GradientStop[] { new(0, Color.FromArgb(0xFF, 0x00, 0x80, 0xFF)), new(1, Color.FromArgb(0xFF, 0xFF, 0x40, 0x80)) });
            g.FillRectangle(rect1, hBrush);

            // Vertical gradient rounded rect
            var rect2 = new Rect(r.X + 10, r.Y + 55, r.Width - 20, 35);
            using var vBrush = factory.CreateLinearGradientBrush(
                new Point(rect2.X, rect2.Y), new Point(rect2.X, rect2.Bottom),
                new GradientStop[] { new(0, Color.FromArgb(0xFF, 0x40, 0xC0, 0x40)), new(1, Color.FromArgb(0xFF, 0x00, 0x40, 0x80)) });
            g.FillRoundedRectangle(rect2, 8, 8, vBrush);

            // Diagonal gradient ellipse
            var rect3 = new Rect(r.X + 10, r.Y + 100, r.Width - 20, 45);
            using var dBrush = factory.CreateLinearGradientBrush(
                new Point(rect3.X, rect3.Y), new Point(rect3.Right, rect3.Bottom),
                new GradientStop[] { new(0, Color.FromArgb(0xFF, 0xFF, 0xA0, 0x00)), new(0.5, Color.FromArgb(0xFF, 0xFF, 0x20, 0x20)), new(1, Color.FromArgb(0xFF, 0x80, 0x00, 0xFF)) });
            g.FillEllipse(rect3, dBrush);
        }));

        // ── Radial Gradient Brush ───────────────────────────────────────────
        _tests.Add(new TestCase("RadialGradientBrush", (g, r) =>
        {
            var factory = Application.Current.GraphicsFactory;

            // Centered radial gradient circle
            double cx1 = r.X + 55, cy1 = r.Y + 55, rad1 = 45;
            using var rBrush1 = factory.CreateRadialGradientBrush(
                new Point(cx1, cy1), new Point(cx1, cy1), rad1, rad1,
                new GradientStop[] { new(0, Color.FromArgb(0xFF, 0xFF, 0xFF, 0x80)), new(1, Color.FromArgb(0xFF, 0xFF, 0x40, 0x00)) });
            g.FillEllipse(new Rect(cx1 - rad1, cy1 - rad1, rad1 * 2, rad1 * 2), rBrush1);

            // Off-center radial gradient
            double cx2 = r.X + 160, cy2 = r.Y + 55, rad2 = 40;
            using var rBrush2 = factory.CreateRadialGradientBrush(
                new Point(cx2, cy2), new Point(cx2 - 15, cy2 - 15), rad2, rad2,
                new GradientStop[] { new(0, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), new(1, Color.FromArgb(0xFF, 0x00, 0x40, 0xC0)) });
            g.FillEllipse(new Rect(cx2 - rad2, cy2 - rad2, rad2 * 2, rad2 * 2), rBrush2);

            // Gradient filled path
            var path = PathGeometry.FromRoundedRect(new Rect(r.X + 10, r.Y + 108, r.Width - 20, 38), 8);
            double pcx = r.X + r.Width / 2, pcy = r.Y + 127;
            using var rBrush3 = factory.CreateRadialGradientBrush(
                new Point(pcx, pcy), new Point(pcx, pcy), r.Width / 2, 25,
                new GradientStop[] { new(0, Color.FromArgb(0xFF, 0x80, 0xFF, 0x80)), new(1, Color.FromArgb(0xFF, 0x00, 0x60, 0x00)) });
            g.FillPath(path, rBrush3);
        }));

        // ── Rotate / Scale ──────────────────────────────────────────────────
        _tests.Add(new TestCase("Rotate / Scale", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var fill = accent.WithAlpha(0x33);

            // Draw reference rect
            var box = new Rect(r.X + 30, r.Y + 20, 60, 40);
            g.DrawRectangle(box, Theme.Palette.ControlBorder, 1);

            // Rotated rect (15 degrees)
            g.Save();
            g.Translate(box.X + box.Width / 2, box.Y + box.Height / 2);
            g.Rotate(15 * Math.PI / 180);
            g.Translate(-(box.X + box.Width / 2), -(box.Y + box.Height / 2));
            g.FillRectangle(box, fill);
            g.DrawRectangle(box, accent, 1);
            g.Restore();

            // Scaled shape
            g.Save();
            double scx = r.X + 160, scy = r.Y + 40;
            g.Translate(scx, scy);
            g.Scale(1.5, 0.8);
            g.Translate(-scx, -scy);
            g.FillEllipse(new Rect(scx - 25, scy - 25, 50, 50), fill);
            g.DrawEllipse(new Rect(scx - 25, scy - 25, 50, 50), accent, 1);
            g.Restore();

            // Multiple rotations fan
            for (int i = 0; i < 6; i++)
            {
                g.Save();
                double fx = r.X + 80, fy = r.Y + 115;
                g.Translate(fx, fy);
                g.Rotate(i * 30 * Math.PI / 180);
                g.Translate(-fx, -fy);
                g.DrawLine(new Point(fx, fy), new Point(fx + 40, fy), accent.WithAlpha((byte)(0x44 + i * 0x22)), 2);
                g.Restore();
            }
        }));

        // ── Translate ───────────────────────────────────────────────────────
        _tests.Add(new TestCase("Translate", (g, r) =>
        {
            var accent = Theme.Palette.Accent;

            // 4 squares translated progressively to the right and down
            for (int i = 0; i < 4; i++)
            {
                g.Save();
                g.Translate(i * 40, i * 25);
                g.FillRectangle(new Rect(r.X + 10, r.Y + 10, 30, 30), accent.WithAlpha((byte)(0x44 + i * 0x33)));
                g.DrawRectangle(new Rect(r.X + 10, r.Y + 10, 30, 30), accent, 1);
                g.Restore();
            }
        }));

        // ── Nested Transforms ──────────────────────────────────────────────
        _tests.Add(new TestCase("Nested Transforms", (g, r) =>
        {
            var accent = Theme.Palette.Accent;

            // Translate → Rotate → Scale (nested Save/Restore)
            // Draw cross-hair at origin for reference
            double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            g.DrawLine(new Point(cx - 50, cy), new Point(cx + 50, cy), Theme.Palette.ControlBorder, 1);
            g.DrawLine(new Point(cx, cy - 50), new Point(cx, cy + 50), Theme.Palette.ControlBorder, 1);

            // Layer 1: Translate to center
            g.Save();
            g.Translate(cx, cy);

            // Layer 2: Rotate 30 degrees
            g.Save();
            g.Rotate(30 * Math.PI / 180);

            // Layer 3: Scale 1.5x horizontally
            g.Save();
            g.Scale(1.5, 1.0);
            g.FillRectangle(new Rect(-30, -15, 60, 30), accent.WithAlpha(0x55));
            g.DrawRectangle(new Rect(-30, -15, 60, 30), accent, 2);
            g.Restore(); // undo scale

            // Still rotated, draw a circle
            g.FillEllipse(new Rect(-12, -12, 24, 24), accent.WithAlpha(0x88));
            g.Restore(); // undo rotate

            // Still translated, draw un-rotated reference
            g.DrawEllipse(new Rect(-20, -20, 40, 40), Theme.Palette.ControlBorder, 1);
            g.Restore(); // undo translate

            // Label corners to show nothing leaked
            g.FillRectangle(new Rect(r.X + 4, r.Y + 4, 6, 6), accent);
            g.FillRectangle(new Rect(r.X + r.Width - 10, r.Y + r.Height - 10, 6, 6), accent);
        }));

        // ── GlobalAlpha ─────────────────────────────────────────────────────
        _tests.Add(new TestCase("GlobalAlpha", (g, r) =>
        {
            var accent = Theme.Palette.Accent;

            // Draw 4 overlapping rects with decreasing global alpha
            var alphas = new float[] { 1.0f, 0.7f, 0.4f, 0.15f };
            for (int i = 0; i < 4; i++)
            {
                g.Save();
                g.GlobalAlpha = alphas[i];
                g.FillRoundedRectangle(new Rect(r.X + 10 + i * 35, r.Y + 15 + i * 15, 80, 50), 6, 6, accent);
                g.Restore();
            }

            // GlobalAlpha on lines
            for (int i = 0; i < 5; i++)
            {
                g.Save();
                g.GlobalAlpha = 1.0f - i * 0.2f;
                double y = r.Y + 100 + i * 12;
                g.DrawLine(new Point(r.X + 10, y), new Point(r.X + r.Width - 10, y), accent, 3);
                g.Restore();
            }
        }));

        // ── SetClipRoundedRect ──────────────────────────────────────────────
        _tests.Add(new TestCase("SetClipRoundedRect", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var fill = accent.WithAlpha(0x44);

            // Draw the clip outline (reference)
            var clipRect = new Rect(r.X + 20, r.Y + 15, 120, 80);
            g.DrawRoundedRectangle(clipRect, 16, 16, Theme.Palette.ControlBorder, 1);

            // Clip and fill a larger rect - only the rounded intersection shows
            g.Save();
            g.SetClipRoundedRect(clipRect, 16, 16);
            g.FillRectangle(new Rect(r.X, r.Y, r.Width, r.Height), fill);
            // Diagonal lines to show clipping
            for (double lx = r.X - 100; lx < r.Right + 100; lx += 12)
            {
                g.DrawLine(new Point(lx, r.Y), new Point(lx + 100, r.Bottom), accent, 1);
            }
            g.Restore();

            // Unclipped rect below for contrast
            g.FillRectangle(new Rect(r.X + 20, r.Y + 110, 120, 30), fill);
            g.DrawRectangle(new Rect(r.X + 20, r.Y + 110, 120, 30), accent, 1);
        }));

        // ── FillRule ────────────────────────────────────────────────────────
        _tests.Add(new TestCase("FillRule: NonZero vs EvenOdd", (g, r) =>
        {
            var accent = Theme.Palette.Accent;
            var fill = accent.WithAlpha(0x66);

            using var measure = BeginTextMeasurement();
            var font = measure.Font;

            // Two concentric rects - NonZero fills everything
            var p1 = new PathGeometry();
            p1.FillRule = FillRule.NonZero;
            // Outer (CW)
            p1.MoveTo(r.X + 10, r.Y + 25);
            p1.LineTo(r.X + 90, r.Y + 25);
            p1.LineTo(r.X + 90, r.Y + 100);
            p1.LineTo(r.X + 10, r.Y + 100);
            p1.Close();
            // Inner (CW)
            p1.MoveTo(r.X + 30, r.Y + 45);
            p1.LineTo(r.X + 70, r.Y + 45);
            p1.LineTo(r.X + 70, r.Y + 80);
            p1.LineTo(r.X + 30, r.Y + 80);
            p1.Close();
            g.FillPath(p1, fill, FillRule.NonZero);
            g.DrawPath(p1, accent, 1);
            g.DrawText("NonZero", new Rect(r.X + 10, r.Y + 105, 80, 16), font, Theme.Palette.WindowText,
                TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

            // Same paths - EvenOdd cuts a hole
            var p2 = new PathGeometry();
            p2.FillRule = FillRule.EvenOdd;
            // Outer (CW)
            p2.MoveTo(r.X + 110, r.Y + 25);
            p2.LineTo(r.X + 190, r.Y + 25);
            p2.LineTo(r.X + 190, r.Y + 100);
            p2.LineTo(r.X + 110, r.Y + 100);
            p2.Close();
            // Inner (CW)
            p2.MoveTo(r.X + 130, r.Y + 45);
            p2.LineTo(r.X + 170, r.Y + 45);
            p2.LineTo(r.X + 170, r.Y + 80);
            p2.LineTo(r.X + 130, r.Y + 80);
            p2.Close();
            g.FillPath(p2, fill, FillRule.EvenOdd);
            g.DrawPath(p2, accent, 1);
            g.DrawText("EvenOdd", new Rect(r.X + 110, r.Y + 105, 80, 16), font, Theme.Palette.WindowText,
                TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }));

        // ── Gradient + StrokeStyle combined ─────────────────────────────────
        _tests.Add(new TestCase("Gradient Pen + DashPath", (g, r) =>
        {
            var factory = Application.Current.GraphicsFactory;

            // Linear gradient brush on a pen with dashes
            using var gradBrush = factory.CreateLinearGradientBrush(
                new Point(r.X, r.Y + 20), new Point(r.X + r.Width, r.Y + 20),
                new GradientStop[] { new(0, Color.FromArgb(0xFF, 0xFF, 0x00, 0x00)), new(1, Color.FromArgb(0xFF, 0x00, 0x00, 0xFF)) });
            using var gradPen = factory.CreatePen(gradBrush, 3, new StrokeStyle
            {
                DashArray = new double[] { 6, 3 },
                LineCap = StrokeLineCap.Round,
            });

            // Wavy path with gradient dashed stroke
            var wave = new PathGeometry();
            wave.MoveTo(r.X + 10, r.Y + 40);
            for (double wx = 0; wx < r.Width - 30; wx += 40)
            {
                wave.QuadTo(r.X + 10 + wx + 10, r.Y + 15, r.X + 10 + wx + 20, r.Y + 40);
                wave.QuadTo(r.X + 10 + wx + 30, r.Y + 65, r.X + 10 + wx + 40, r.Y + 40);
            }
            g.DrawPath(wave, gradPen);
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
            context.FillRoundedRectangle(card, 8, 8, Theme.Palette.WindowBackground);
            context.DrawRoundedRectangle(card, 8, 8, Theme.Palette.ControlBorder, 1);

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
