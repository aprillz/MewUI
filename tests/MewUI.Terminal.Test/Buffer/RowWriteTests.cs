using Aprillz.MewUI.Terminal.Buffer;

namespace MewUI.Terminal.Test.Buffer;

/// <summary>
/// Writing text into a row keeps a glyph per cell: wide glyphs take two columns, grapheme clusters stay together even
/// when they arrive in separate writes, and a wide glyph that does not fit leaves a padding cell.
/// </summary>
[TestClass]
public sealed class RowWriteTests
{
    private static Row Write(int columns, params string[] writes)
    {
        var row = new Row(columns, default);
        int column = 0;
        foreach (string text in writes)
        {
            var state = new RowWriteState(text, column);
            row.ReplaceText(ref state);
            column = state.ColumnEnd;
        }

        return row;
    }

    [TestMethod]
    public void AWideGlyphTakesTwoColumns()
    {
        var row = Write(4, "a猫d");

        Assert.AreEqual("a", row.GlyphAt(0).ToString());
        Assert.AreEqual("猫", row.GlyphAt(1).ToString());
        Assert.AreEqual(DbcsAttribute.Leading, row.DbcsAttrAt(1));
        Assert.AreEqual(DbcsAttribute.Trailing, row.DbcsAttrAt(2));
        Assert.AreEqual("d", row.GlyphAt(3).ToString());
        Assert.AreEqual("a猫d", row.GetText().ToString());
    }

    [TestMethod]
    public void ASurrogatePairEmojiIsOneWideGlyph()
    {
        var row = Write(4, "a\U0001F600d");

        Assert.AreEqual("\U0001F600", row.GlyphAt(1).ToString());
        Assert.AreEqual("\U0001F600", row.GlyphAt(2).ToString());
        Assert.AreEqual(3, row.GetLeadingColumnAtCharOffset(3));
    }

    [TestMethod]
    public void ACombiningMarkWrittenLaterJoinsTheGlyphBeforeIt()
    {
        var row = Write(4, "e", "́x");

        Assert.AreEqual("é", row.GlyphAt(0).ToString());
        Assert.AreEqual("x", row.GlyphAt(1).ToString());
    }

    [TestMethod]
    public void AVariationSelectorWrittenLaterWidensTheEmojiBeforeIt()
    {
        // U+2764 HEAVY BLACK HEART is narrow alone and wide with VS16.
        var row = Write(4, "❤", "️x");

        Assert.AreEqual("❤️", row.GlyphAt(0).ToString());
        Assert.AreEqual(DbcsAttribute.Leading, row.DbcsAttrAt(0));
        Assert.AreEqual("x", row.GlyphAt(2).ToString());
    }

    [TestMethod]
    public void AWideGlyphThatDoesNotFitLeavesAPaddingCell()
    {
        var row = new Row(3, default);
        var state = new RowWriteState("ab猫", 0);
        row.ReplaceText(ref state);

        Assert.AreEqual("猫", state.Text.ToString(), "the wide glyph is left for the next row");
        Assert.AreEqual(3, state.ColumnEnd, "the end reaches the limit so the caller wraps");
        Assert.IsTrue(row.WasDoubleBytePadded);
        Assert.AreEqual(2, row.GetReadableColumnCount());
    }

    [TestMethod]
    public void WritingOverHalfAWideGlyphBlanksTheOtherHalf()
    {
        var row = Write(4, "猫猫");

        var state = new RowWriteState("x", 1);
        row.ReplaceText(ref state);

        Assert.AreEqual(" ", row.GlyphAt(0).ToString());
        Assert.AreEqual("x", row.GlyphAt(1).ToString());
        Assert.AreEqual("猫", row.GlyphAt(2).ToString());
    }

    [TestMethod]
    public void AttributesAreKeptPerColumn()
    {
        var row = new Row(6, default);
        var bold = default(TextAttribute);
        bold.SetIntense(true);

        row.ReplaceAttributes(2, 4, bold);

        Assert.IsFalse(row.GetAttrByColumn(1).IsIntense());
        Assert.IsTrue(row.GetAttrByColumn(2).IsIntense());
        Assert.IsTrue(row.GetAttrByColumn(3).IsIntense());
        Assert.IsFalse(row.GetAttrByColumn(4).IsIntense());
        Assert.AreEqual(3, row.Attributes.Runs.Length);
    }
}
