using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class SliderTemplateTests
{
    private const double WIDTH = 200;
    private const double THUMB_SIZE = 14;
    private const double USABLE = WIDTH - THUMB_SIZE;

    private static DelegateControlTemplate<Slider> TrackTemplate(Track track, Thumb thumb, Border decrease, Border increase)
        => new((owner, ctx) =>
        {
            track.Thumb = thumb;
            track.DecreaseElement = decrease;
            track.IncreaseElement = increase;
            ctx.Register(Slider.PART_TRACK, track);
            ctx.Bind(decrease, Control.BackgroundProperty, Slider.FillProperty);
            return track;
        });

    private static (Window Window, Slider Slider, Track Track, Thumb Thumb, Border Decrease) CreateTemplated()
    {
        var track = new Track();
        var thumb = new Thumb { Width = THUMB_SIZE, Height = THUMB_SIZE };
        var decrease = new Border { Height = 4 };
        var increase = new Border { Height = 4 };
        var window = HeadlessWindow.Create(300, 300);
        window.Padding = new Thickness(0);
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 25,
            Width = WIDTH,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Template = TrackTemplate(track, thumb, decrease, increase),
        };
        window.Content = slider;
        window.PerformLayout();
        return (window, slider, track, thumb, decrease);
    }

    [TestMethod]
    public void TrackPart_ReceivesRangeValueAndDirection()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, slider, track, thumb, decrease) = CreateTemplated();

        Assert.AreEqual(25, track.Value, 0.001);
        Assert.AreEqual(100, track.Maximum, 0.001);
        Assert.IsFalse(track.IsDirectionReversed, "a horizontal slider has its minimum at the start");
        Assert.AreEqual(track.Bounds.X + USABLE * 0.25, thumb.Bounds.X, 0.5, "the thumb is not placed for the value");
        Assert.AreEqual(slider.Fill, decrease.Background, "the fill reaches the decrease region through the template binding");

        slider.Value = 75;
        window.PerformLayout();
        Assert.AreEqual(track.Bounds.X + USABLE * 0.75, thumb.Bounds.X, 0.5, "a value change does not move the thumb part");

        slider.Orientation = Orientation.Vertical;
        window.PerformLayout();
        Assert.AreEqual(Orientation.Vertical, track.Orientation);
        Assert.IsTrue(track.IsDirectionReversed, "a vertical slider has its minimum at the bottom");
    }

    [TestMethod]
    public void ThumbDrag_CommitsTheValueThroughTheTrack()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, slider, _, thumb, _) = CreateTemplated();
        var start = thumb.CenterOf();

        window.SendMouseDown(start);
        Assert.IsTrue(thumb.IsDragging, "the press on the thumb did not start a drag");
        Assert.AreEqual(25, slider.Value, 0.001, "the press on the thumb moved the value");
        Assert.IsTrue(slider.IsFocused, "the press on the thumb did not focus the slider");

        window.SendMouseDrag(new Point(start.X + USABLE * 0.3, start.Y));
        Assert.AreEqual(55, slider.Value, 0.5, "a drag of 30% of the usable length is 30% of the range");

        window.SendMouseUp(new Point(start.X + USABLE * 0.3, start.Y));
        Assert.IsFalse(thumb.IsDragging, "the release did not end the drag");
    }

    [TestMethod]
    public void PressOnTheTrack_MovesTheValueToThePointAndFollowsTheDrag()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, slider, track, _, _) = CreateTemplated();
        double y = track.CenterOf().Y;
        double origin = track.Bounds.X + THUMB_SIZE / 2;

        window.SendMouseDown(new Point(origin + USABLE * 0.6, y));
        Assert.AreEqual(60, slider.Value, 0.5, "the press did not move the value to the point");

        window.SendMouseDrag(new Point(origin + USABLE * 0.9, y));
        Assert.AreEqual(90, slider.Value, 0.5, "the value did not follow the drag after the press");

        window.SendMouseUp(new Point(origin + USABLE * 0.9, y));
    }

    [TestMethod]
    public void MissingTrackPart_LeavesKeysAndWheel()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(300, 300);
        window.Padding = new Thickness(0);
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            Width = WIDTH,
            Template = new DelegateControlTemplate<Slider>((_, _) => new Border()),
        };
        window.Content = slider;
        window.PerformLayout();

        window.SendClick(new Point(slider.Bounds.X + WIDTH * 0.9, slider.CenterOf().Y));
        Assert.AreEqual(50, slider.Value, 0.001, "without a track part a press has nothing to map to a value");
        Assert.IsTrue(slider.IsFocused, "the press did not focus the slider");

        window.SendKeyPress(Key.Right);
        Assert.AreEqual(51, slider.Value, 0.001, "the keys stopped working without a track part");

        window.SendMouseWheel(slider.CenterOf(), 1);
        Assert.AreEqual(52, slider.Value, 0.001, "the wheel stopped working without a track part");
    }
}
