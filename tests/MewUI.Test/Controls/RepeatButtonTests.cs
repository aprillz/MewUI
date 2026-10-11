using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class RepeatButtonTests
{
    [TestMethod]
    public void Defaults_MatchSpecifiedValues()
    {
        var repeatButton = new RepeatButton();

        Assert.AreEqual(400.0, repeatButton.Delay);
        Assert.AreEqual(80.0, repeatButton.Interval);
    }

    [TestMethod]
    public void MouseDown_FiresClickImmediatelyOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var repeatButton = new RepeatButton();
        window.Content = repeatButton;
        window.PerformLayout();

        int clicks = 0;
        repeatButton.Click += () => clicks++;

        var position = new Point(5, 5);
        repeatButton.RaiseMouseDown(new MouseEventArgs(position, position, MouseButton.Left, leftButton: true));

        Assert.AreEqual(1, clicks, "pressing raises Click once immediately, before any repeat delay");
    }

    [TestMethod]
    public void MouseDownThenUp_DoesNotDoubleFireClick()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var repeatButton = new RepeatButton();
        window.Content = repeatButton;
        window.PerformLayout();

        int clicks = 0;
        repeatButton.Click += () => clicks++;

        var position = new Point(5, 5);
        repeatButton.RaiseMouseDown(new MouseEventArgs(position, position, MouseButton.Left, leftButton: true));
        Assert.AreEqual(1, clicks);

        repeatButton.RaiseMouseUp(new MouseEventArgs(position, position, MouseButton.Left, leftButton: false));

        Assert.AreEqual(1, clicks, "mouse-up must not raise a second Click; RepeatButton suppresses Button's own mouse-up firing");
    }

    [TestMethod]
    public void MouseUp_ReleasesMouseCapture()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var repeatButton = new RepeatButton();
        window.Content = repeatButton;
        window.PerformLayout();

        var position = new Point(5, 5);
        repeatButton.RaiseMouseDown(new MouseEventArgs(position, position, MouseButton.Left, leftButton: true));
        Assert.IsTrue(repeatButton.IsMouseCaptured);

        repeatButton.RaiseMouseUp(new MouseEventArgs(position, position, MouseButton.Left, leftButton: false));

        Assert.IsFalse(repeatButton.IsMouseCaptured);
    }
}
