using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ScrollBarTemplateTests
{
    private const double HEIGHT = 200;

    private static DelegateControlTemplate<ScrollBar> TrackTemplate(Track track, Thumb thumb, Border decrease, Border increase)
        => new((owner, ctx) =>
        {
            track.Thumb = thumb;
            track.DecreaseElement = decrease;
            track.IncreaseElement = increase;
            ctx.Register(ScrollBar.PART_TRACK, track);
            return track;
        });

    private static (Window Window, ScrollBar Bar, Track Track, Thumb Thumb, Border Decrease, Border Increase) CreateTemplated()
    {
        var track = new Track();
        var thumb = new Thumb();
        var decrease = new Border();
        var increase = new Border();
        var window = HeadlessWindow.Create(300, 300);
        window.Padding = new Thickness(0);
        var bar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 50,
            Value = 25,
            LargeChange = 10,
            Width = 16,
            Height = HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Template = TrackTemplate(track, thumb, decrease, increase),
        };
        window.Content = bar;
        window.PerformLayout();
        return (window, bar, track, thumb, decrease, increase);
    }

    [TestMethod]
    public void TrackPart_ReceivesRangeValueViewportAndOrientation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, bar, track, thumb, _, _) = CreateTemplated();

        Assert.AreEqual(25, track.Value, 0.001);
        Assert.AreEqual(50, track.ViewportSize, 0.001);
        Assert.AreEqual(100, track.Maximum, 0.001);
        Assert.AreEqual(Orientation.Vertical, track.Orientation);

        // The viewport is a third of range plus viewport, so the thumb takes a third of the track.
        Assert.AreEqual(HEIGHT / 3, thumb.Bounds.Height, 0.5, "the thumb length does not follow the viewport share");
        double usable = HEIGHT - thumb.Bounds.Height;
        Assert.AreEqual(track.Bounds.Y + usable * 0.25, thumb.Bounds.Y, 0.5, "the thumb is not placed for the value");

        bar.Value = 75;
        window.PerformLayout();
        Assert.AreEqual(track.Bounds.Y + usable * 0.75, thumb.Bounds.Y, 0.5, "a value change does not move the thumb part");
    }

    [TestMethod]
    public void ThumbDrag_CommitsTheValueThroughTheTrack()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, bar, thumbTrack, thumb, _, _) = CreateTemplated();
        double usable = HEIGHT - thumb.Bounds.Height;
        var start = thumb.CenterOf();

        window.SendMouseDown(start);
        Assert.IsTrue(thumb.IsDragging, "the press on the thumb did not start a drag");
        window.SendMouseDrag(new Point(start.X, start.Y + usable * 0.3));
        Assert.AreEqual(55, bar.Value, 0.5, "a drag of 30% of the usable length is 30% of the range");
        window.SendMouseUp(new Point(start.X, start.Y + usable * 0.3));
        Assert.IsFalse(thumb.IsDragging, "the release did not end the drag");
        Assert.IsNotNull(thumbTrack);
    }

    [TestMethod]
    public void PressOnAPagingRegion_PagesByLargeChange()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, bar, _, thumb, _, increase) = CreateTemplated();

        window.SendClick(increase.CenterOf());
        Assert.AreEqual(35, bar.Value, 0.001, "a press past the thumb does not page toward the maximum");

        window.PerformLayout();
        window.SendClick(new Point(thumb.CenterOf().X, thumb.Bounds.Y - 2));
        Assert.AreEqual(25, bar.Value, 0.001, "a press before the thumb does not page toward the minimum");
    }

    [TestMethod]
    public void MissingTrackPart_LeavesWheelScrolling()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(300, 300);
        window.Padding = new Thickness(0);
        var bar = new ScrollBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            Width = 16,
            Height = HEIGHT,
            Template = new DelegateControlTemplate<ScrollBar>((_, _) => new Border()),
        };
        window.Content = bar;
        window.PerformLayout();

        window.SendClick(bar.CenterOf());
        Assert.AreEqual(50, bar.Value, 0.001, "without a track part a press has nothing to page against");

        window.SendMouseWheel(bar.CenterOf(), -120);
        Assert.AreNotEqual(50, bar.Value, "the wheel stopped working without a track part");
    }

    [TestMethod]
    public void DrawnThumb_RoundsByCornerRadiusUpToAHalfCircle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(300, 300);
        window.Padding = new Thickness(0);
        var bar = new ScrollBar { Minimum = 0, Maximum = 100, ViewportSize = 50, Value = 25, Height = HEIGHT, HorizontalAlignment = HorizontalAlignment.Left };
        window.Content = bar;
        window.PerformLayout();

        double thickness = bar.ThemeInternal.Metrics.ScrollBarThickness;
        Assert.AreEqual(Math.Min(bar.CornerRadius, thickness / 2), RenderedThumbRadius(bar), 0.001, "the default style's corner radius does not reach the thumb");

        bar.CornerRadius = 0;
        Assert.AreEqual(0, RenderedThumbRadius(bar), 0.001, "a zero corner radius still rounds the thumb");

        bar.CornerRadius = 20;
        Assert.AreEqual(thickness / 2, RenderedThumbRadius(bar), 0.001, "the radius is not capped at a half circle");
    }

    private static double RenderedThumbRadius(ScrollBar bar)
    {
        var recorder = new RadiusRecordingContext();
        bar.Render(recorder);
        Assert.IsNotNull(recorder.Radius, "the thumb was not drawn");
        return recorder.Radius.Value;
    }

    private sealed class RadiusRecordingContext : NoOpGraphicsContext
    {
        public double? Radius { get; private set; }

        public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) => Radius = radiusX;
    }
}
