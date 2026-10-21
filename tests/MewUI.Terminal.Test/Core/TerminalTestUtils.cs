// Ported from Windows Terminal src/inc/TestUtils.h (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Core;

internal static class TerminalTestUtils
{
    public const string TEST_100_CHARS_STRING =
        "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~!\"#$%&";

    /// <summary>Checks that the cells from <paramref name="position"/> on, in reading order, hold the characters of <paramref name="expected"/>.</summary>
    public static void VerifyExpectedString(TextBuffer buffer, string expected, TilPoint position)
    {
        var size = buffer.Size;
        var cell = position;
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.IsTrue(size.IsInBounds(cell), $"Cell {cell} for character [{index}] is outside the buffer.");
            string glyph = buffer.GetRowByOffset(cell.Y).GlyphAt(cell.X).ToString();
            Assert.AreEqual(expected[index].ToString(), glyph, $"Character [{index}] at {cell} was mismatched.");
            size.IncrementInBounds(ref cell, true);
        }
    }
}
