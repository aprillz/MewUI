using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

using RegisteredFace = Aprillz.MewUI.Resources.FontRegistry.RegisteredFace;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class FontRegistryTests
{
    [TestMethod]
    public void Register_KeepsEveryFileOfAFamily()
    {
        string family = UniqueFamily();
        var regular = new RegisteredFace(FakePath("Regular"), 400, false);
        var extraBold = new RegisteredFace(FakePath("ExtraBold"), 800, false);
        try
        {
            FontRegistry.Register(family, regular);
            FontRegistry.Register(family, extraBold);
            int version = FontRegistry.Version;
            FontRegistry.Register(family, regular);

            var resolved = FontRegistry.Resolve(family);

            Assert.IsNotNull(resolved);
            CollectionAssert.AreEqual(new[] { regular, extraBold }, resolved.Value.Faces);
            Assert.AreEqual(version, FontRegistry.Version);
        }
        finally
        {
            FontRegistry.Unregister(regular.FilePath);
            FontRegistry.Unregister(extraBold.FilePath);
        }
    }

    [TestMethod]
    [DataRow(400, 400)]
    [DataRow(700, 800)]
    [DataRow(900, 800)]
    [DataRow(300, 400)]
    [DataRow(500, 400)]
    public void SelectFace_MatchesWeightTheWayCssDoes(int requested, int expected)
    {
        var resolved = new FontRegistry.ResolvedFont("Family",
        [
            new RegisteredFace("regular", 400, false),
            new RegisteredFace("extra-bold", 800, false),
        ]);

        Assert.AreEqual(expected, resolved.SelectFace((FontWeight)requested, false).Weight);
    }

    [TestMethod]
    public void SelectFace_BetweenNormalAndMedium_PrefersHeavierUpToMediumThenLighter()
    {
        var resolved = new FontRegistry.ResolvedFont("Family",
        [
            new RegisteredFace("light", 300, false),
            new RegisteredFace("medium", 500, false),
            new RegisteredFace("bold", 700, false),
        ]);

        Assert.AreEqual(500, resolved.SelectFace(FontWeight.Normal, false).Weight);
        Assert.AreEqual(300, new FontRegistry.ResolvedFont("Family",
        [
            new RegisteredFace("light", 300, false),
            new RegisteredFace("bold", 700, false),
        ]).SelectFace(FontWeight.Normal, false).Weight);
    }

    [TestMethod]
    public void SelectFace_PrefersRequestedSlantOverCloserWeight()
    {
        var resolved = new FontRegistry.ResolvedFont("Family",
        [
            new RegisteredFace("bold", 700, false),
            new RegisteredFace("italic", 400, true),
        ]);

        Assert.AreEqual("italic", resolved.SelectFace(FontWeight.Bold, true).FilePath);
        Assert.AreEqual("bold", resolved.SelectFace(FontWeight.Normal, false).FilePath);
    }

    [TestMethod]
    public void Unregister_DropsFileAndEmptyFamily()
    {
        string family = UniqueFamily();
        var regular = new RegisteredFace(FakePath("Regular"), 400, false);
        var bold = new RegisteredFace(FakePath("Bold"), 700, false);
        FontRegistry.Register(family, regular);
        FontRegistry.Register(family, bold);

        int version = FontRegistry.Version;
        FontRegistry.Unregister(regular.FilePath);

        CollectionAssert.AreEqual(new[] { bold }, FontRegistry.Resolve(family)!.Value.Faces);
        Assert.AreNotEqual(version, FontRegistry.Version);

        FontRegistry.Unregister(bold.FilePath);

        Assert.IsNull(FontRegistry.Resolve(family));
    }

    [TestMethod]
    public void TextEngine_DoesNotReuseFontCreatedBeforeRegistration()
    {
        string family = UniqueFamily();
        var face = new RegisteredFace(FakePath("Regular"), 400, false);
        using var factory = new GdiGraphicsFactory();
        using var engine = new ManagedTextEngine(factory);
        try
        {
            var before = DefaultFont(engine, family);
            Assert.AreSame(before, DefaultFont(engine, family));

            FontRegistry.Register(family, face);

            Assert.AreNotSame(before, DefaultFont(engine, family));
        }
        finally
        {
            FontRegistry.Unregister(face.FilePath);
        }
    }

    private static object DefaultFont(ManagedTextEngine engine, string family)
    {
        var layout = (ManagedTextLayout)engine.CreateLayout(new TextLayoutRequest
        {
            Text = "Font".AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle(family, 12),
            Paragraph = new TextParagraphStyle(),
        });
        return layout.GetDefaultFont();
    }

    private static string UniqueFamily() => "MewUI Registry Test " + Guid.NewGuid().ToString("N");

    private static string FakePath(string style) => Path.Combine(Path.GetTempPath(), $"mewui-registry-{Guid.NewGuid():N}-{style}.ttf");
}
