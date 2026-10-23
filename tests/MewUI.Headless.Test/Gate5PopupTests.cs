using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>Gate 5: a click opens the ComboBox drop-down inside the window surface and a click on an item selects it.</summary>
[TestClass]
public sealed class Gate5PopupTests
{
    [TestMethod]
    public void Click_OpensDropDownAndSelectsClickedItem()
    {
        var combo = new ComboBox().Items("User", "Admin", "Guest");
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        combo.VerticalAlignment = VerticalAlignment.Top;
        combo.Width = 160;
        var window = HeadlessTestHost.ShowWindow(combo);
        try
        {
            window.SendClick(combo.CenterInWindow());
            Assert.IsTrue(HeadlessTestHost.Shared.Run(() => combo.IsDropDownOpen), "the click did not open the drop-down");

            var item = FindDropDownItem(window, combo, index: 1);
            window.SendClick(item.CenterInWindow());

            Assert.AreEqual(1, HeadlessTestHost.Shared.Run(() => combo.SelectedIndex));
            Assert.IsFalse(HeadlessTestHost.Shared.Run(() => combo.IsDropDownOpen), "selecting an item did not close the drop-down");
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    [TestMethod]
    public void Click_OutsideOpenDropDown_ClosesIt()
    {
        var combo = new ComboBox().Items("User", "Admin", "Guest");
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        combo.VerticalAlignment = VerticalAlignment.Top;
        combo.Width = 160;
        var window = HeadlessTestHost.ShowWindow(combo);
        try
        {
            window.SendClick(combo.CenterInWindow());
            Assert.IsTrue(HeadlessTestHost.Shared.Run(() => combo.IsDropDownOpen));

            window.SendClick(new Point(380, 280));

            Assert.IsFalse(HeadlessTestHost.Shared.Run(() => combo.IsDropDownOpen));
            Assert.AreEqual(-1, HeadlessTestHost.Shared.Run(() => combo.SelectedIndex));
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    /// <summary>
    /// The drop-down is hosted in the window's popup layer, not under the combo box, so its rows
    /// are found the way a user finds them: by what sits under the pointer below the box.
    /// </summary>
    private static ItemContainer FindDropDownItem(Window window, ComboBox combo, int index)
        => HeadlessTestHost.Shared.Run(() =>
        {
            var bounds = combo.Bounds;
            double x = bounds.X + (bounds.Width / 2);
            for (double y = bounds.Bottom + 1; y < window.ClientSize.Height; y += 2)
            {
                for (Element? current = window.HitTest(new Point(x, y)); current != null; current = current.Parent)
                {
                    if (current is ItemContainer container && container.Index == index)
                    {
                        return container;
                    }
                }
            }

            throw new AssertFailedException($"no drop-down row with index {index} is under the combo box");
        });
}
