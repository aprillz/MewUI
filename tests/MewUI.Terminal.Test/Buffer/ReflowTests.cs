// Ported from Windows Terminal src/buffer/out/ut_textbuffer/ReflowTests.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;
using Aprillz.MewUI.Terminal.Unicode;

namespace MewUI.Terminal.Test.Buffer;

[TestClass]
public sealed class ReflowTests
{
    public static IEnumerable<object[]> CaseIndices => Enumerable.Range(0, ReflowTestData.Cases.Length).Select(index => new object[] { index });

    [TestMethod]
    [DynamicData(nameof(CaseIndices))]
    public void TestReflowCases(int caseIndex)
    {
        var testCase = ReflowTestData.Cases[caseIndex];

        var textBuffer = TextBufferFromTestBuffer(testCase.Buffers[0]);
        for (int bufferIndex = 1; bufferIndex < testCase.Buffers.Length; bufferIndex++)
        {
            var testBuffer = testCase.Buffers[bufferIndex];
            var newBuffer = new TextBuffer(testBuffer.Size, default, 0, false, new Renderer());
            TextBuffer.Reflow(textBuffer, newBuffer);
            textBuffer = newBuffer;

            CompareTextBufferAgainstTestBuffer(textBuffer, testBuffer, $"{testCase.Name} [{bufferIndex}]");
        }
    }

    internal static bool IsGlyphFullWidth(char character)
    {
        var state = new GraphemeState();
        new CodepointWidthDetector().GraphemeNext(ref state, [character]);
        return state.Width == 2;
    }

    private static TextBuffer TextBufferFromTestBuffer(ReflowTestBuffer testBuffer)
    {
        var buffer = new TextBuffer(testBuffer.Size, default, 0, false, new Renderer());
        int y = 0;
        foreach (var testRow in testBuffer.Rows)
        {
            var row = buffer.GetMutableRowByOffset(y);
            row.SetWrapForced(testRow.Wrap);

            int x = 0;
            foreach (char character in testRow.Text)
            {
                int width = IsGlyphFullWidth(character) ? 2 : 1;
                row.ReplaceCharacters(x, width, [character]);
                x += width;
            }

            y++;
        }

        buffer.Cursor.SetPosition(testBuffer.Cursor);
        return buffer;
    }

    private static void CompareTextBufferAgainstTestBuffer(TextBuffer buffer, ReflowTestBuffer testBuffer, string context)
    {
        Assert.AreEqual(testBuffer.Cursor, buffer.Cursor.Position, $"{context}: cursor");
        Assert.AreEqual(testBuffer.Size, buffer.Size.Dimensions, $"{context}: size");

        int y = 0;
        foreach (var testRow in testBuffer.Rows)
        {
            var row = buffer.GetRowByOffset(y);
            Assert.AreEqual(testRow.Wrap, row.WasWrapForced, $"{context}: [Row {y}] wrap");

            int x = 0;
            int textIndex = 0;
            foreach (char character in testRow.Text)
            {
                string where = $"{context}: [Cell {x}, {y}; Text line index {textIndex}]";
                if (IsGlyphFullWidth(character))
                {
                    Assert.AreEqual(DbcsAttribute.Leading, row.DbcsAttrAt(x), where);
                    Assert.AreEqual(character, row.GlyphAt(x)[0], where);
                    x++;
                    Assert.AreEqual(DbcsAttribute.Trailing, row.DbcsAttrAt(x), where);
                    Assert.AreEqual(character, row.GlyphAt(x)[0], where);
                    x++;
                }
                else
                {
                    Assert.AreEqual(DbcsAttribute.Single, row.DbcsAttrAt(x), where);
                    Assert.AreEqual(character, row.GlyphAt(x)[0], where);
                    x++;
                }

                textIndex++;
            }

            y++;
        }
    }
}
