using System.Globalization;
using Aprillz.MewUI;

namespace MewUI.Test.Core;

[TestClass]
[DoNotParallelize]
public sealed class MewUIStringsTests
{
    [TestCleanup]
    public void Cleanup()
    {
        MewUIStrings.SetCulture(CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public void SetCulture_UsesNeutralEnglishResources()
    {
        MewUIStrings.SetCulture(CultureInfo.InvariantCulture);

        Assert.AreEqual("Copy", MewUIStrings.Copy.Value);
        Assert.AreEqual("Select All", MewUIStrings.SelectAll.Value);
        Assert.AreEqual("_OK", MewUIStrings.OK.Value);
    }

    [TestMethod]
    public void SetCulture_UsesChineseSimplifiedResources()
    {
        MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

        Assert.AreEqual("复制", MewUIStrings.Copy.Value);
        Assert.AreEqual("全选", MewUIStrings.SelectAll.Value);
        Assert.AreEqual("确定", MewUIStrings.OK.Value);
    }

    [TestMethod]
    public void SetCulture_UsesChineseSimplifiedResourcesForRegionCulture()
    {
        MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));

        Assert.AreEqual("复制", MewUIStrings.Copy.Value);
        Assert.AreEqual("粘贴", MewUIStrings.Paste.Value);
    }

    [TestMethod]
    public void SetCulture_FallsBackToNeutralResources()
    {
        MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("fr-FR"));

        Assert.AreEqual("Paste", MewUIStrings.Paste.Value);
        Assert.AreEqual("Retry", MewUIStrings.Retry.Value.TrimStart('_'));
    }

    [TestMethod]
    public void SetCulture_RaisesChangedForUpdatedValues()
    {
        MewUIStrings.SetCulture(CultureInfo.InvariantCulture);
        int changedCount = 0;
        void OnChanged() => changedCount++;

        MewUIStrings.Cut.Changed += OnChanged;
        try
        {
            MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        }
        finally
        {
            MewUIStrings.Cut.Changed -= OnChanged;
        }

        Assert.AreEqual(1, changedCount);
    }
}
