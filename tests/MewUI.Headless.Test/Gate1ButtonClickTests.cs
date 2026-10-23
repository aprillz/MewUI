using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>Gate 1: a click at the button's center raises Click exactly once.</summary>
[TestClass]
public sealed class Gate1ButtonClickTests
{
    [TestMethod]
    public void Click_AtButtonCenter_RaisesClickOnce()
    {
        var button = new Button().Content("OK");
        int clicks = 0;
        button.Click += () => clicks++;
        var window = HeadlessTestHost.ShowWindow(button);
        try
        {
            window.SendClick(button.CenterInWindow());

            Assert.AreEqual(1, clicks);
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    [TestMethod]
    public void Click_OutsideButton_DoesNotRaiseClick()
    {
        var button = new Button().Content("OK");
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.VerticalAlignment = VerticalAlignment.Top;
        int clicks = 0;
        button.Click += () => clicks++;
        var window = HeadlessTestHost.ShowWindow(button);
        try
        {
            var bounds = HeadlessTestHost.Shared.Run(() => button.Bounds);
            window.SendClick(new Point(bounds.Right + 50, bounds.Bottom + 50));

            Assert.AreEqual(0, clicks);
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }
}
