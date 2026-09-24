using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Elements;

/// <summary>
/// An inherited property read while nothing above supplied it gives the default. When the element is later placed
/// under an ancestor that does supply it, the value it read has changed and it has to be told, the same as for a
/// value it had inherited from somewhere.
/// </summary>
[TestClass]
public sealed class InheritedDefaultTests
{
    private const double ANCESTOR_FONT_SIZE = 40;

    private sealed class FontProbe : ContentControl
    {
        public List<int> Invalidated { get; } = new();

        protected override void OnFontCacheInvalidated(MewProperty property)
        {
            base.OnFontCacheInvalidated(property);
            Invalidated.Add(property.Id);
        }
    }

    [TestMethod]
    public void AFontSizeReadAsTheDefault_IsReportedWhenAnAncestorSuppliesOne()
    {
        var probe = new FontProbe();
        double detached = probe.FontSize;
        var ancestor = new ContentControl { FontSize = ANCESTOR_FONT_SIZE };

        probe.Parent = ancestor;

        Assert.AreNotEqual(ANCESTOR_FONT_SIZE, detached);
        Assert.AreEqual(ANCESTOR_FONT_SIZE, probe.FontSize);
        CollectionAssert.Contains(probe.Invalidated, TextElement.FontSizeProperty.Id,
            "The font size the probe read as the default changed on attach without a notice.");
    }

    [TestMethod]
    public void AFontStyleReadAsTheDefault_IsReportedWhenAnAncestorSuppliesOne()
    {
        var probe = new FontProbe();
        _ = probe.FontStyle;
        var ancestor = new ContentControl { FontStyle = FontStyle.Italic };

        probe.Parent = ancestor;

        Assert.AreEqual(FontStyle.Italic, probe.FontStyle);
        CollectionAssert.Contains(probe.Invalidated, TextElement.FontStyleProperty.Id,
            "The font style the probe read as the default changed on attach without a notice.");
    }

    [TestMethod]
    public void ADefaultThatStaysTheDefault_IsNotReported()
    {
        var probe = new FontProbe();
        _ = probe.FontSize;
        var ancestor = new ContentControl();

        probe.Parent = ancestor;

        Assert.IsEmpty(probe.Invalidated);
    }
}
