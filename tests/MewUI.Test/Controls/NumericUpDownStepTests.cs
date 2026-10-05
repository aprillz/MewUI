using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A numeric up-down steps by its SmallChange (arrow keys, spinner, wheel) and its LargeChange
/// (PageUp/PageDown), as WinUI's NumberBox does; Step is the obsolete name for SmallChange.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NumericUpDownStepTests
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

    private static Window Focused(NumericUpDown box)
    {
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.Width = 120;
        var window = HeadlessWindow.Create(400, 300);
        window.Content = box;
        window.PerformLayout();
        window.FocusManager.SetFocus(box);
        window.PerformLayout();
        return window;
    }

    [TestMethod]
    public void SmallChange_DrivesTheArrowKeyTheSpinnerAndTheWheel()
    {
        if (SkipOnNonWindows()) return;

        var box = new NumericUpDown { SmallChange = 5, Value = 10 };
        var window = Focused(box);

        window.SendKeyPress(Key.Up);
        Assert.AreEqual(15, box.Value, 1e-9, "the arrow key did not step by SmallChange");

        box.StepUp();
        Assert.AreEqual(20, box.Value, 1e-9, "the spinner did not step by SmallChange");

        window.SendMouseWheel(box.CenterOf(), 1);
        Assert.AreEqual(25, box.Value, 1e-9, "the wheel did not step by SmallChange");
    }

    [TestMethod]
    public void PageUp_StepsByLargeChange()
    {
        if (SkipOnNonWindows()) return;

        var box = new NumericUpDown { LargeChange = 20, Value = 10 };
        var window = Focused(box);

        window.SendKeyPress(Key.PageUp);
        Assert.AreEqual(30, box.Value, 1e-9, "PageUp did not step by LargeChange");

        window.SendKeyPress(Key.PageDown);
        Assert.AreEqual(10, box.Value, 1e-9, "PageDown did not step back by LargeChange");
    }

    [TestMethod]
    public void PageUp_StepsADefaultBoxByTen()
    {
        if (SkipOnNonWindows()) return;

        var box = new NumericUpDown();
        var window = Focused(box);

        window.SendKeyPress(Key.PageUp);

        Assert.AreEqual(10, box.Value, 1e-9);
    }

    [TestMethod]
    public void Step_IsSmallChange()
    {
#pragma warning disable CS0618 // The obsolete name has to keep working until it is removed.
        var box = new NumericUpDown { Step = 3 };
#pragma warning restore CS0618

        Assert.AreEqual(3, box.SmallChange, "setting Step did not set SmallChange");
    }

    [TestMethod]
    public void AnIntegerBox_RoundsItsStepsToWholeNumbers()
    {
        var box = new NumericUpDown { IsInteger = true, SmallChange = 2.6, Value = 1 };

        box.StepUp();

        Assert.AreEqual(4, box.Value, "a SmallChange of 2.6 did not step an integer box by 3");
    }

    [TestMethod]
    public void TheDefaultRangeIsZeroToOneHundred()
    {
        var box = new NumericUpDown();

        Assert.AreEqual(0, box.Minimum);
        Assert.AreEqual(100, box.Maximum);
        Assert.AreEqual(1, box.SmallChange);
        Assert.AreEqual(10, box.LargeChange);
    }
}
