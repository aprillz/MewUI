using Aprillz.MewUI;

namespace MewUI.Test.Core;

[TestClass]
[DoNotParallelize]
public sealed class ScreenHelperTests
{
    [TestMethod]
    public void WorkArea_IsEmptyWithoutARunningApplication()
    {
        Assert.IsFalse(Application.IsRunning, "a previous test left an application running");

        var workArea = ScreenHelper.GetWorkAreaForPoint(new Point(10, 10));

        Assert.AreEqual(Rect.Empty, workArea);
        Assert.IsTrue(workArea.IsEmpty);
    }
}
