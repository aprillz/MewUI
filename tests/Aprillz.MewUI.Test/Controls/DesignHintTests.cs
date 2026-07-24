using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// DesignSize/DesignWidth/DesignHeight record preview sizing hints only while
/// Design.IsPreviewMode is set; production runs stay hint-free no-ops.
/// </summary>
[TestClass]
public sealed class DesignHintTests
{
    [TestCleanup]
    public void Cleanup() => Design.IsPreviewMode = false;

    [TestMethod]
    public void DesignSize_OutsidePreviewMode_RecordsNothing()
    {
        var element = new Border().DesignSize(400, 300);

        Assert.IsFalse(Design.TryGetDesignSize(element, out _, out _));
    }

    [TestMethod]
    public void DesignSize_InPreviewMode_RecordsBothDimensions()
    {
        Design.IsPreviewMode = true;
        var element = new Border().DesignSize(400, 300);

        Assert.IsTrue(Design.TryGetDesignSize(element, out double width, out double height));
        Assert.AreEqual(400, width);
        Assert.AreEqual(300, height);
    }

    [TestMethod]
    public void DesignWidth_InPreviewMode_LeavesHeightUnset()
    {
        Design.IsPreviewMode = true;
        var element = new Border().DesignWidth(250);

        Assert.IsTrue(Design.TryGetDesignSize(element, out double width, out double height));
        Assert.AreEqual(250, width);
        Assert.IsTrue(double.IsNaN(height));
    }

    [TestMethod]
    public void DesignWidthThenHeight_InPreviewMode_MergesPerAxis()
    {
        Design.IsPreviewMode = true;
        var element = new Border().DesignWidth(250).DesignHeight(120);

        Assert.IsTrue(Design.TryGetDesignSize(element, out double width, out double height));
        Assert.AreEqual(250, width);
        Assert.AreEqual(120, height);
    }
}
