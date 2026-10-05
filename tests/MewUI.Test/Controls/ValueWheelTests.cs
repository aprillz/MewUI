using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A slider, a numeric up-down and a closed combo box take the wheel while the pointer is over them, unless
/// ChangeOnWheel is off. A slider moves by its SmallChange per notch and its LargeChange per page key.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ValueWheelTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    /// <summary>The control at the top of a page taller than the window.</summary>
    private static (Window window, ScrollViewer page) OnAPage(FrameworkElement control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        var content = new StackPanel();
        content.Add(control);
        content.Add(new Border { Height = 2000 });
        var page = new ScrollViewer { Content = content };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = page;
        window.PerformLayout();
        return (window, page);
    }

    private static void Wheel(Window window, FrameworkElement control, double notches)
    {
        window.SendMouseWheel(control.CenterOf(), notches);
        window.PerformLayout();
    }

    [TestMethod]
    public void Slider_MovesBySmallChangePerNotch()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Width = 200, Minimum = 0, Maximum = 1, SmallChange = 0.1 };
        var (window, _) = OnAPage(slider);
        window.FocusManager.SetFocus(slider);

        Wheel(window, slider, 1);

        Assert.AreEqual(0.1, slider.Value, 1e-9, "one notch did not move the slider by its SmallChange");
    }

    [TestMethod]
    public void Slider_GathersPartialNotches()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Width = 200, Value = 50 };
        var (window, _) = OnAPage(slider);
        window.FocusManager.SetFocus(slider);

        Wheel(window, slider, 0.5);
        Assert.AreEqual(50, slider.Value, 1e-9, "half a notch moved the slider");

        Wheel(window, slider, 0.5);
        Assert.AreEqual(51, slider.Value, 1e-9, "two halves did not add up to one step");
    }

    [TestMethod]
    public void PageUp_MovesASliderByItsLargeChange()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Width = 200, LargeChange = 25 };
        var (window, _) = OnAPage(slider);
        window.FocusManager.SetFocus(slider);

        window.SendKeyPress(Key.PageUp);

        Assert.AreEqual(25, slider.Value, 1e-9, "PageUp ignored the LargeChange it was given");
    }

    [TestMethod]
    public void PageUp_MovesADefaultSliderByTen()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Width = 200 };
        var (window, _) = OnAPage(slider);
        window.FocusManager.SetFocus(slider);

        window.SendKeyPress(Key.PageUp);

        Assert.AreEqual(10, slider.Value, 1e-9);
    }

    [TestMethod]
    public void NumericUpDown_StepsPerNotch()
    {
        if (SkipOnNonWindows()) return;

        var box = new NumericUpDown { Width = 120, Maximum = 100, Value = 5 };
        var (window, _) = OnAPage(box);
        window.FocusManager.SetFocus(box);

        Wheel(window, box, 1);

        Assert.AreEqual(6, box.Value, 1e-9);
    }

    [TestMethod]
    public void ComboBox_SelectsTheNextItemOnAWheelDown()
    {
        if (SkipOnNonWindows()) return;

        var combo = new ComboBox { Width = 120 }.Items(["One", "Two", "Three"]);
        combo.SelectedIndex = 0;
        var (window, _) = OnAPage(combo);
        window.FocusManager.SetFocus(combo);

        Wheel(window, combo, -1);

        Assert.AreEqual(1, combo.SelectedIndex);
    }

    [TestMethod]
    public void HoveredSlider_TakesTheWheelFromThePage()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Width = 200, Value = 50 };
        var (window, page) = OnAPage(slider);

        Wheel(window, slider, -1);

        Assert.AreEqual(49, slider.Value, "a hovered slider did not take the wheel");
        Assert.AreEqual(0, page.VerticalOffset, "the page scrolled as well");
    }

    [TestMethod]
    public void ChangeOnWheelOff_KeepsTheValue()
    {
        if (SkipOnNonWindows()) return;

        var slider = new Slider { Width = 200, Value = 50, ChangeOnWheel = false };
        var box = new NumericUpDown { Width = 120, Maximum = 100, Value = 5, ChangeOnWheel = false };
        var combo = new ComboBox { Width = 120, ChangeOnWheel = false }.Items(["One", "Two", "Three"]);
        combo.SelectedIndex = 0;
        var content = new StackPanel();
        content.Add(slider);
        content.Add(box);
        content.Add(combo);
        var window = HeadlessWindow.Create(400, 300);
        window.Content = content;
        window.PerformLayout();

        window.FocusManager.SetFocus(slider);
        Wheel(window, slider, 1);
        window.FocusManager.SetFocus(box);
        Wheel(window, box, 1);
        window.FocusManager.SetFocus(combo);
        Wheel(window, combo, -1);

        Assert.AreEqual(50, slider.Value);
        Assert.AreEqual(5, box.Value);
        Assert.AreEqual(0, combo.SelectedIndex);
    }
}
