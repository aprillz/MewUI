using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>
/// Cells laid edge to edge draw without a gap of background between them at any display scale, and an element measured
/// outside a layout pass is laid out again without waiting for another change.
/// </summary>
[TestClass]
public sealed class LayoutPixelTests
{
    private const int WIDTH = 420;
    private const int HEIGHT = 200;
    private const int SWEEP_STEPS = 24;
    private const double SWEEP_STEP = 0.37;

    private static readonly Color BACKGROUND = Color.FromArgb(255, 255, 255, 255);

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(168u)]
    [DataRow(192u)]
    public void GridCells_DrawWithoutGaps(uint dpi) => DrawsWithoutGaps(dpi, () =>
    {
        var grid = new Grid().Columns("*,*,*,*,*,*,*").Rows("*,*,*");
        grid.Height = 100.3;
        for (int index = 0; index < 21; index++)
        {
            var cell = Cell(index);
            Grid.SetRow(cell, index / 7);
            Grid.SetColumn(cell, index % 7);
            grid.Add(cell);
        }

        return grid;
    });

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(168u)]
    [DataRow(192u)]
    public void UniformGridCells_DrawWithoutGaps(uint dpi) => DrawsWithoutGaps(dpi, () =>
    {
        var grid = new UniformGrid { Columns = 6, Rows = 3, Height = 90.7 };
        for (int index = 0; index < 18; index++)
        {
            grid.Add(Cell(index));
        }

        return grid;
    });

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(168u)]
    [DataRow(192u)]
    public void StackedCells_DrawWithoutGaps(uint dpi) => DrawsWithoutGaps(dpi, () =>
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Height = 60.3 };
        for (int index = 0; index < 9; index++)
        {
            var cell = Cell(index);
            cell.Width = 26 + index * 0.45;
            stack.Add(cell);
        }

        return stack;
    }, sweepWidth: false);

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(168u)]
    [DataRow(192u)]
    public void WrappedTextFrames_MatchADrawingFromTheVisuals(uint dpi)
    {
        HeadlessTestHost.WithSession(new HeadlessOptions { Rendering = HeadlessRendering.Offscreen, Dpi = dpi }, session =>
        {
            StackPanel panel = null!;
            var window = session.Run(() =>
            {
                panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                var alignments = new[] { HorizontalAlignment.Stretch, HorizontalAlignment.Center, HorizontalAlignment.Right, HorizontalAlignment.Left };
                for (int index = 0; index < 8; index++)
                {
                    panel.Add(new TextBlock
                    {
                        Text = Words(index),
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = alignments[index % alignments.Length],
                        TextAlignment = index % 3 == 0 ? TextAlignment.Center : TextAlignment.Left,
                    });
                }

                var shown = new Window
                {
                    WindowSize = WindowSize.Resizable(WIDTH, 400),
                    Background = BACKGROUND,
                    Content = panel,
                };
                shown.Show();
                return shown;
            });

            var failures = new List<string>();
            for (int step = 0; step < SWEEP_STEPS * 2 && failures.Count < 4; step++)
            {
                double width = 260 + step * SWEEP_STEP * 3;
                session.Run(() => panel.Width = width);
                session.RunJobs();
                var frame = window.CaptureFrame();
                var reference = session.Run(() => window.CaptureReferenceFrame());
                int differing = 0;
                for (int pixelY = 0; pixelY < frame.Height; pixelY++)
                {
                    for (int pixelX = 0; pixelX < frame.Width; pixelX++)
                    {
                        if (frame.GetPixel(pixelX, pixelY) != reference.GetPixel(pixelX, pixelY))
                        {
                            differing++;
                        }
                    }
                }

                if (differing > 0)
                {
                    failures.Add($"width {width:0.##}: {differing} pixels differ from a drawing made fresh from the visuals");
                }
            }

            Assert.IsEmpty(failures, $"stale text at {dpi} DPI:\n" + string.Join("\n", failures));
        });
    }

    [TestMethod]
    public void MeasureOutsideALayoutPass_IsArrangedWithoutAnotherChange()
    {
        HeadlessTestHost.WithSession(new HeadlessOptions(), session =>
        {
            Probe probe = null!;
            var window = session.Run(() =>
            {
                probe = new Probe();
                var shown = new Window
                {
                    WindowSize = WindowSize.Resizable(WIDTH, HEIGHT),
                    Content = new StackPanel().Children(probe),
                };
                shown.Show();
                return shown;
            });
            session.RunJobs();
            int arranges = session.Run(() => probe.Arranges);

            session.Run(() => probe.Measure(new Size(77, 77)));
            session.RunJobs();

            Assert.AreEqual(arranges + 1, session.Run(() => probe.Arranges), "the element measured outside a pass was not arranged");
            Assert.IsFalse(session.Run(() => probe.IsArrangeDirty), "the element was left arrange dirty");
        });
    }

    [TestMethod]
    public void MeasureOutsideALayoutPass_DoesNotLeaveTheElementSizedForThatConstraint()
    {
        HeadlessTestHost.WithSession(new HeadlessOptions(), session =>
        {
            TextBlock text = null!;
            var window = session.Run(() =>
            {
                text = new TextBlock
                {
                    Text = "A wrapped line of text long enough to need several lines at a narrow width.",
                    TextWrapping = TextWrapping.Wrap,
                };
                var shown = new Window
                {
                    WindowSize = WindowSize.Resizable(WIDTH, HEIGHT),
                    Content = new StackPanel().Children(text),
                };
                shown.Show();
                return shown;
            });
            session.RunJobs();
            var laidOut = session.Run(() => text.DesiredSize);

            session.Run(() => text.Measure(new Size(60, double.PositiveInfinity)));
            session.RunJobs();

            Assert.AreEqual(laidOut, session.Run(() => text.DesiredSize), "the parent did not measure the text again after a stray measure");
            Assert.IsFalse(session.Run(() => text.IsMeasureDirty || text.IsArrangeDirty), "the text was left dirty");
        });
    }

    private static void DrawsWithoutGaps(uint dpi, Func<FrameworkElement> build, bool sweepWidth = true)
    {
        HeadlessTestHost.WithSession(new HeadlessOptions { Rendering = HeadlessRendering.Offscreen, Dpi = dpi }, session =>
        {
            FrameworkElement panel = null!;
            var window = session.Run(() =>
            {
                panel = build();
                panel.HorizontalAlignment = HorizontalAlignment.Left;
                panel.VerticalAlignment = VerticalAlignment.Top;
                panel.Margin = new Thickness(7.3, 5.6, 0, 0);
                var shown = new Window
                {
                    WindowSize = WindowSize.Resizable(WIDTH, HEIGHT),
                    Background = BACKGROUND,
                    Content = panel,
                };
                shown.Show();
                return shown;
            });

            var failures = new List<string>();
            for (int step = 0; step < SWEEP_STEPS; step++)
            {
                double width = 300 + step * SWEEP_STEP;
                if (sweepWidth)
                {
                    session.Run(() => panel.Width = width);
                }
                else
                {
                    double offset = 7.3 + step * SWEEP_STEP;
                    session.Run(() => panel.Margin = new Thickness(offset, 5.6, 0, 0));
                }

                session.RunJobs();
                var frame = window.CaptureFrame();
                var bounds = session.Run(() => panel.Bounds);
                int left = (int)Math.Round(bounds.X * frame.DpiScale);
                int top = (int)Math.Round(bounds.Y * frame.DpiScale);
                int right = (int)Math.Round(bounds.Right * frame.DpiScale);
                int bottom = (int)Math.Round(bounds.Bottom * frame.DpiScale);
                for (int pixelY = top; pixelY < bottom && failures.Count < 8; pixelY++)
                {
                    for (int pixelX = left; pixelX < right; pixelX++)
                    {
                        if (frame.GetPixel(pixelX, pixelY) == BACKGROUND)
                        {
                            failures.Add($"step {step}: background pixel at ({pixelX}, {pixelY}) inside the cells ({left}, {top})..({right}, {bottom})");
                            break;
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, $"gaps between cells at {dpi} DPI:\n" + string.Join("\n", failures));
        });
    }

    private static string Words(int seed)
        => string.Join(' ', Enumerable.Range(0, 8 + seed % 7)
            .Select(word => new string((char)('a' + (word + seed) % 26), 2 + (word * 7 + seed) % 9)));

    private static Border Cell(int seed)
        => new() { Background = Color.FromArgb(255, (byte)(40 + seed * 37 % 180), 120, 200) };

    private sealed class Probe : FrameworkElement
    {
        public int Arranges { get; private set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 20);

        protected override void ArrangeContent(Rect bounds) => Arranges++;
    }
}
