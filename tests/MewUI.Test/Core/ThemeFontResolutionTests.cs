using Aprillz.MewUI;

namespace MewUI.Test.Core;

/// <summary>
/// Verifies theme font resolution: an unspecified family follows the platform system UI font,
/// and an assigned family (including "Segoe UI") is used as-is.
/// </summary>
[TestClass]
public sealed class ThemeFontResolutionTests
{
    private const string PLATFORM_FONT = "Malgun Gothic";

    private string _originalPlatformFont = null!;
    private ThemeMetrics _originalMetrics = null!;

    [TestInitialize]
    public void Initialize()
    {
        _originalPlatformFont = ThemeMetrics.PlatformFontFamily;
        _originalMetrics = ThemeManager.DefaultMetrics;
        ThemeMetrics.PlatformFontFamily = PLATFORM_FONT;
    }

    [TestCleanup]
    public void Cleanup()
    {
        ThemeMetrics.PlatformFontFamily = _originalPlatformFont;
        ThemeManager.DefaultMetrics = _originalMetrics;
    }

    [TestMethod]
    public void UnspecifiedFont_ResolvesToPlatformFont()
    {
        Assert.AreEqual(PLATFORM_FONT, ThemeMetrics.Default.FontFamily);
        Assert.IsTrue(ThemeMetrics.Default.IsSystemFontFamily);
    }

    [TestMethod]
    public void ExplicitFont_IsPreserved()
    {
        var metrics = ThemeMetrics.Default with { FontFamily = "Segoe UI" };

        Assert.AreEqual("Segoe UI", metrics.FontFamily);
        Assert.IsFalse(metrics.IsSystemFontFamily);
    }

    [TestMethod]
    public void UnrelatedOverride_KeepsFollowingPlatformFont()
    {
        var metrics = ThemeMetrics.Default with { FontSize = 13 };

        Assert.AreEqual(PLATFORM_FONT, metrics.FontFamily);
        Assert.IsTrue(metrics.IsSystemFontFamily);
    }

    [TestMethod]
    public void NullOrWhitespaceFont_FollowsPlatformFont()
    {
        Assert.IsTrue((ThemeMetrics.Default with { FontFamily = null! }).IsSystemFontFamily);
        Assert.IsTrue((ThemeMetrics.Default with { FontFamily = "   " }).IsSystemFontFamily);
    }

    [TestMethod]
    public void PlatformFontChange_IsVisibleToExistingInstance()
    {
        var metrics = ThemeMetrics.Default with { FontSize = 13 };
        Assert.AreEqual(PLATFORM_FONT, metrics.FontFamily);

        ThemeMetrics.PlatformFontFamily = ".AppleSystemUIFont";

        // Resolution happens on read, so no instance holds a stale platform font.
        Assert.AreEqual(".AppleSystemUIFont", metrics.FontFamily);
    }

    [TestMethod]
    public void EmptyPlatformFont_FallsBackInsteadOfBlanking()
    {
        ThemeMetrics.PlatformFontFamily = "";

        Assert.AreEqual("Segoe UI", ThemeMetrics.Default.FontFamily);
    }

    [TestMethod]
    public void DefaultMetricsAssignment_SurvivesPlatformFontInjection()
    {
        ThemeManager.DefaultMetrics = ThemeMetrics.Default with { FontFamily = "Segoe UI" };

        ThemeMetrics.PlatformFontFamily = PLATFORM_FONT;

        Assert.AreEqual("Segoe UI", ThemeManager.DefaultMetrics.FontFamily);
    }

    [TestMethod]
    public void SystemFontAndExplicitFont_AreNotEqual()
    {
        var followsSystem = ThemeMetrics.Default;
        var explicitSegoe = ThemeMetrics.Default with { FontFamily = "Segoe UI" };

        Assert.AreNotEqual(followsSystem, explicitSegoe);
    }
}
