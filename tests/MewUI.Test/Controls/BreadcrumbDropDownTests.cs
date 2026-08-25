using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class BreadcrumbDropDownTests
{
    [TestMethod]
    public void ReclickTrigger_ClosesPopupWithoutReopening()
    {
        var window = HeadlessWindow.Create();
        var dropDown = new BreadcrumbDropDown(
            "root",
            static _ => ["child"],
            static _ => { });
        window.Content = dropDown;
        window.PerformLayout();

        Assert.IsTrue(dropDown.Focusable, "the owner must retain focus while its popup is open");
        Assert.IsFalse(dropDown.IsTabStop, "the separator remains outside keyboard tab traversal");

        window.SendClick(dropDown.CenterOf());
        Assert.IsTrue(dropDown.IsDropDownOpen);

        window.SendClick(dropDown.CenterOf());
        Assert.IsFalse(dropDown.IsDropDownOpen, "reclicking the trigger closes instead of reopening");
    }
}
