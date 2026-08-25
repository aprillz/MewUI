using Aprillz.MewUI.Preview;

namespace MewUI.Test.Core;

[TestClass]
public sealed class WindowlessPreviewTests
{
    [TestMethod]
    public void TargetScan_DisablesMainWindowWhenApplicationHasNone()
    {
        var main = PreviewTargetScanner.Scan(mainWindowAvailable: false)
            .Single(target => target.Id == PreviewTargetScanner.MAIN_WINDOW_ID);

        Assert.IsFalse(main.Available);
        StringAssert.Contains(main.UnavailableReason, "without a main window");
    }

    [TestMethod]
    public void TargetScan_KeepsMainWindowAvailableForWindowedRun()
    {
        var main = PreviewTargetScanner.Scan(mainWindowAvailable: true)
            .Single(target => target.Id == PreviewTargetScanner.MAIN_WINDOW_ID);

        Assert.IsTrue(main.Available);
        Assert.IsNull(main.UnavailableReason);
    }
}
