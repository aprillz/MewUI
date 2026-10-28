using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A vertical slider runs its track bottom to top: the minimum is at the bottom, Up and Right raise the
/// value, and while Width is unset it is 24 DIPs wide, the way a horizontal one is 24 DIPs tall.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SliderOrientationTests
{
    // Half the thumb, by which the track is inset at both ends.
    private const double TRACK_INSET = 7;

    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static Window Host(FrameworkElement content)
    {
        var window = HeadlessWindow.Create(400, 300);
        window.Content = content;
        window.PerformLayout();
        return window;
    }

    [TestMethod]
    public void Horizontal_KeepsItsDefaultHeight()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider();
        Host(slider);

        Assert.AreEqual(24, slider.Bounds.Height, 1e-9);
        Assert.IsGreaterThan(24.0, slider.Bounds.Width);
    }

    [TestMethod]
    public void Horizontal_UnsetHeightStillSizesToContent()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Height = double.NaN, VerticalAlignment = VerticalAlignment.Top };
        Host(slider);

        Assert.AreEqual(14, slider.Bounds.Height, 1e-9, "an explicit NaN height was replaced by the default");
    }

    [TestMethod]
    public void Vertical_SwapsTheDefaultExtent()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Orientation = Orientation.Vertical };
        var window = Host(slider);
        double slotHeight = window.ClientSize.Height - window.Padding.VerticalThickness;

        Assert.AreEqual(24, slider.Bounds.Width, 1e-9, "a vertical slider is not 24 DIPs wide");
        Assert.AreEqual(slotHeight, slider.Bounds.Height, 1e-9, "a vertical slider did not stretch along its track");
    }

    [TestMethod]
    public void Vertical_MeasuresAlongTheTrack()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Orientation = Orientation.Vertical };
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Add(slider);
        Host(panel);

        Assert.AreEqual(24, slider.DesiredSize.Width, 1e-9);
        Assert.AreEqual(42, slider.DesiredSize.Height, 1e-9);
    }

    [TestMethod]
    public void Vertical_HonorsAnExplicitWidth()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Orientation = Orientation.Vertical, Width = 40 };
        Host(slider);

        Assert.AreEqual(40, slider.Bounds.Width, 1e-9);
    }

    [TestMethod]
    public void Vertical_PointMapsBottomToMinimum()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Orientation = Orientation.Vertical, Height = 200, VerticalAlignment = VerticalAlignment.Top };
        var window = Host(slider);
        var bounds = slider.Bounds;
        double trackLength = bounds.Height - TRACK_INSET * 2;
        double centerX = bounds.X + bounds.Width / 2;

        window.SendClick(new Point(centerX, bounds.Bottom - TRACK_INSET));
        Assert.AreEqual(0, slider.Value, 1e-9, "the bottom of the track is not the minimum");

        window.SendClick(new Point(centerX, bounds.Y + TRACK_INSET));
        Assert.AreEqual(100, slider.Value, 1e-9, "the top of the track is not the maximum");

        window.SendClick(new Point(centerX, bounds.Y + TRACK_INSET + trackLength * 0.25));
        Assert.AreEqual(75, slider.Value, 1e-9, "a quarter of the way down is not three quarters of the range");
    }

    [TestMethod]
    public void Vertical_DragFollowsThePointer()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Orientation = Orientation.Vertical, Height = 200, VerticalAlignment = VerticalAlignment.Top };
        var window = Host(slider);
        var bounds = slider.Bounds;
        double centerX = bounds.X + bounds.Width / 2;
        double middle = bounds.Y + bounds.Height / 2;

        window.SendMouseDown(new Point(centerX, bounds.Bottom - TRACK_INSET));
        window.SendMouseDrag(new Point(centerX, middle));
        window.SendMouseUp(new Point(centerX, middle));

        Assert.AreEqual(50, slider.Value, 1e-9);
    }

    [TestMethod]
    [DataRow(Key.Up, 51.0)]
    [DataRow(Key.Right, 51.0)]
    [DataRow(Key.Down, 49.0)]
    [DataRow(Key.Left, 49.0)]
    public void Vertical_ArrowKeysFollowTheTrack(Key key, double expected)
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Orientation = Orientation.Vertical, Value = 50 };
        var window = Host(slider);
        window.FocusManager.SetFocus(slider);

        window.SendKeyPress(key);

        Assert.AreEqual(expected, slider.Value, 1e-9);
    }

    [TestMethod]
    public void Vertical_WheelUpIncreases()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Orientation = Orientation.Vertical, Value = 50 };
        var window = Host(slider);

        window.SendMouseWheel(slider.CenterOf(), 1);

        Assert.AreEqual(51, slider.Value, 1e-9);
    }
}
