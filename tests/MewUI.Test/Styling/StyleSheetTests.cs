using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Styling;

[TestClass]
public sealed class StyleSheetTests
{
    [TestMethod]
    public void DefineFactory_DoesNotCreateStyleUntilLookup()
    {
        var sheet = new StyleSheet();
        var style = new Style(typeof(Button));
        var calls = 0;

        sheet.Define("lazy", () =>
        {
            calls++;
            return style;
        });

        Assert.AreEqual(0, calls);

        Assert.AreSame(style, sheet.Get("lazy"));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void DefineFactory_CachesCreatedStyle()
    {
        var sheet = new StyleSheet();
        var calls = 0;

        sheet.Define("lazy", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });

        var first = sheet.Get("lazy");
        var second = sheet.Get("lazy");

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void DefineFactory_ReplacesPendingFactory()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        var replacement = new Style(typeof(Button));

        sheet.Define("style", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });
        sheet.Define("style", () => replacement);

        Assert.AreSame(replacement, sheet.Get("style"));
        Assert.AreEqual(0, calls);
    }
}
