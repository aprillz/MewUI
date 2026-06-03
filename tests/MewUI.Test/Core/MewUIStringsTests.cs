using System.Globalization;
using System.Reflection;
using System.Resources;
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
        Assert.AreEqual("Open", MewUIStrings.OpenFileDialogTitle.Value);
        Assert.AreEqual("Window", MewUIStrings.WindowTitle.Value);
        Assert.AreEqual("Hex", MewUIStrings.ColorPickerHexLabel.Value);
        Assert.AreEqual("Refresh", GetStringValue("DevToolsRefresh"));
        Assert.AreEqual("MewUI Profiler", GetStringValue("ProfilerTitle"));
    }

    [TestMethod]
    public void SetCulture_UsesChineseSimplifiedResources()
    {
        MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

        Assert.AreEqual("复制", MewUIStrings.Copy.Value);
        Assert.AreEqual("全选", MewUIStrings.SelectAll.Value);
        Assert.AreEqual("确定", MewUIStrings.OK.Value);
        Assert.AreEqual("打开", MewUIStrings.OpenFileDialogTitle.Value);
        Assert.AreEqual("窗口", MewUIStrings.WindowTitle.Value);
        Assert.AreEqual("十六进制", MewUIStrings.ColorPickerHexLabel.Value);
        Assert.AreEqual("刷新", GetStringValue("DevToolsRefresh"));
        Assert.AreEqual("MewUI 分析器", GetStringValue("ProfilerTitle"));
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
    public void SetCulture_UpdatesLoadedValues()
    {
        MewUIStrings.SetCulture(CultureInfo.InvariantCulture);

        Assert.AreEqual("Cut", MewUIStrings.Cut.Value);

        MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

        Assert.AreEqual("剪切", MewUIStrings.Cut.Value);
    }

    [TestMethod]
    public void Resources_ContainEveryRegisteredString()
    {
        var resourceManager = new ResourceManager(
            "Aprillz.MewUI.Resources.MewUIStrings",
            typeof(MewUIStrings).Assembly);
        var neutralCulture = CultureInfo.InvariantCulture;
        var chineseCulture = CultureInfo.GetCultureInfo("zh-Hans");

        var properties = GetRegisteredStringProperties();

        foreach (var property in properties)
        {
            Assert.IsNotNull(resourceManager.GetString(property.Name, neutralCulture), property.Name);
            Assert.IsNotNull(resourceManager.GetString(property.Name, chineseCulture), property.Name);
        }
    }

    private static string GetStringValue(string propertyName)
    {
        var property = typeof(MewUIStrings).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(property, propertyName);
        Assert.AreEqual(typeof(ObservableValue<string>), property.PropertyType, propertyName);

        var value = (ObservableValue<string>?)property.GetValue(null);
        Assert.IsNotNull(value, propertyName);
        return value.Value;
    }

    private static IEnumerable<PropertyInfo> GetRegisteredStringProperties()
        => typeof(MewUIStrings)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(ObservableValue<string>));
}
