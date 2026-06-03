using System.Globalization;
using System.Reflection;
using Aprillz.MewUI;

namespace MewUI.Test.Core;

[TestClass]
[DoNotParallelize]
public sealed class MewUIStringsTests
{
    [TestCleanup]
    public void Cleanup()
    {
        MewUIStrings.SetLocalizer(null);
        MewUIStrings.SetCulture(CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public void SetCulture_UsesDefaultValuesWithoutLocalizer()
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
    public void SetLocalizer_UsesHostProvidedValues()
    {
        MewUIStrings.SetLocalizer(
            static (key, defaultValue, culture) => culture.Name switch
            {
                "zh-Hans" => ChineseStrings.TryGetValue(key, out var value) ? value : defaultValue,
                _ => defaultValue
            },
            CultureInfo.GetCultureInfo("zh-Hans"));

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
    public void SetLocalizer_LetsHostControlCultureFallback()
    {
        MewUIStrings.SetLocalizer(
            static (key, defaultValue, culture) =>
            {
                CultureInfo? current = culture;
                while (current != null && current != CultureInfo.InvariantCulture)
                {
                    if (ChineseStringsByCulture.TryGetValue(current.Name, out var strings)
                        && strings.TryGetValue(key, out var value))
                    {
                        return value;
                    }

                    current = current.Parent;
                }

                return defaultValue;
            },
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.AreEqual("复制", MewUIStrings.Copy.Value);
        Assert.AreEqual("粘贴", MewUIStrings.Paste.Value);
    }

    [TestMethod]
    public void SetCulture_FallsBackToDefaultValueWhenLocalizerReturnsNull()
    {
        MewUIStrings.SetLocalizer(static (_, _, _) => null);

        MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("fr-FR"));

        Assert.AreEqual("Paste", MewUIStrings.Paste.Value);
        Assert.AreEqual("Retry", MewUIStrings.Retry.Value.TrimStart('_'));
    }

    [TestMethod]
    public void SetCulture_UpdatesValuesFromCurrentLocalizer()
    {
        MewUIStrings.SetCulture(CultureInfo.InvariantCulture);

        Assert.AreEqual("Cut", MewUIStrings.Cut.Value);

        MewUIStrings.SetLocalizer(static (key, defaultValue, culture) =>
            culture.Name == "zh-Hans" && ChineseStrings.TryGetValue(key, out var value)
                ? value
                : defaultValue);
        MewUIStrings.SetCulture(CultureInfo.GetCultureInfo("zh-Hans"));

        Assert.AreEqual("剪切", MewUIStrings.Cut.Value);
    }

    [TestMethod]
    public void SetLocalizer_ResetRestoresDefaultValues()
    {
        MewUIStrings.SetLocalizer(static (key, defaultValue, _) =>
            ChineseStrings.TryGetValue(key, out var value) ? value : defaultValue);

        Assert.AreEqual("复制", MewUIStrings.Copy.Value);

        MewUIStrings.SetLocalizer(null, CultureInfo.InvariantCulture);

        Assert.AreEqual("Copy", MewUIStrings.Copy.Value);
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

    private static readonly Dictionary<string, string> ChineseStrings = new(StringComparer.Ordinal)
    {
        [nameof(MewUIStrings.Copy)] = "复制",
        [nameof(MewUIStrings.Cut)] = "剪切",
        [nameof(MewUIStrings.Paste)] = "粘贴",
        [nameof(MewUIStrings.SelectAll)] = "全选",
        [nameof(MewUIStrings.OK)] = "确定",
        [nameof(MewUIStrings.OpenFileDialogTitle)] = "打开",
        [nameof(MewUIStrings.WindowTitle)] = "窗口",
        [nameof(MewUIStrings.ColorPickerHexLabel)] = "十六进制",
        ["DevToolsRefresh"] = "刷新",
        ["ProfilerTitle"] = "MewUI 分析器"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> ChineseStringsByCulture = new(StringComparer.Ordinal)
    {
        ["zh-Hans"] = ChineseStrings
    };
}
