// Ported from Windows Terminal src/cascadia/UnitTests_TerminalCore/ScreenSizeLimitsTest.cpp (MIT).

using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Core;

[TestClass]
public sealed class ScreenSizeLimitsTests
{
    private static TerminalCore CreateFromSettings(int historySize, int initialRows, int initialCols)
    {
        var terminal = new TerminalCore(true);
        terminal.CreateFromSettings(new TerminalSettings { HistorySize = historySize, InitialRows = initialRows, InitialCols = initialCols }, new Renderer());
        return terminal;
    }

    [TestMethod]
    public void ScreenWidthAndHeightAreClampedToBounds()
    {
        // Negative or zero sizes are clamped to the minimum, too large ones to short.MaxValue.
        var negativeColumnsTerminal = CreateFromSettings(10000, 9999999, -1234);
        var actualDimensions = negativeColumnsTerminal.GetViewport().Dimensions;
        Assert.AreEqual(short.MaxValue, actualDimensions.Height, "Row count clamped to short.MaxValue");
        Assert.AreEqual(TerminalCore.MINIMUM_VISIBLE_CELLS, actualDimensions.Width, "Column count clamped to MINIMUM_VISIBLE_CELLS");

        var zeroRowsTerminal = CreateFromSettings(10000, 0, 9999999);
        actualDimensions = zeroRowsTerminal.GetViewport().Dimensions;
        Assert.AreEqual(TerminalCore.MINIMUM_VISIBLE_CELLS, actualDimensions.Height, "Row count clamped to MINIMUM_VISIBLE_CELLS");
        Assert.AreEqual(short.MaxValue, actualDimensions.Width, "Column count clamped to short.MaxValue");
    }

    [TestMethod]
    public void ScrollbackHistorySizeIsClampedToBounds()
    {
        // The buffer height, history plus visible rows, is what is clamped.
        const int VISIBLE_ROW_COUNT = 100;

        var noHistoryTerminal = CreateFromSettings(0, VISIBLE_ROW_COUNT, 100);
        Assert.AreEqual(VISIBLE_ROW_COUNT, noHistoryTerminal.GetTextBuffer().TotalRowCount, "History size of 0 is accepted");

        var negativeHistorySizeTerminal = CreateFromSettings(-100, VISIBLE_ROW_COUNT, 100);
        Assert.AreEqual(VISIBLE_ROW_COUNT, negativeHistorySizeTerminal.GetTextBuffer().TotalRowCount, "Negative history size is clamped to 0");

        var maxHistorySizeTerminal = CreateFromSettings(short.MaxValue - VISIBLE_ROW_COUNT, VISIBLE_ROW_COUNT, 100);
        Assert.AreEqual(short.MaxValue, maxHistorySizeTerminal.GetTextBuffer().TotalRowCount, "History size == short.MaxValue - initial row count is accepted");

        var justTooBigHistorySizeTerminal = CreateFromSettings(short.MaxValue - VISIBLE_ROW_COUNT + 1, VISIBLE_ROW_COUNT, 100);
        Assert.AreEqual(short.MaxValue, justTooBigHistorySizeTerminal.GetTextBuffer().TotalRowCount, "History size one past the limit is clamped");

        var farTooBigHistorySizeTerminal = CreateFromSettings(99999999, VISIBLE_ROW_COUNT, 100);
        Assert.AreEqual(short.MaxValue, farTooBigHistorySizeTerminal.GetTextBuffer().TotalRowCount, "History size that is far too large is clamped");
    }

    [TestMethod]
    public void ResizeIsClampedToBounds()
    {
        // GH#2630, GH#2815: resizing must not grow the buffer past short.MaxValue rows.
        const int INITIAL_VISIBLE_COL_COUNT = 50;
        const int INITIAL_VISIBLE_ROW_COUNT = 50;
        int historySize = short.MaxValue - (INITIAL_VISIBLE_ROW_COUNT * 2);

        var terminal = CreateFromSettings(historySize, INITIAL_VISIBLE_ROW_COUNT, INITIAL_VISIBLE_COL_COUNT);
        Assert.AreEqual(historySize + INITIAL_VISIBLE_ROW_COUNT, terminal.GetTextBuffer().TotalRowCount);

        // Exactly short.MaxValue rows.
        terminal.UserResize(new TilSize(INITIAL_VISIBLE_COL_COUNT, INITIAL_VISIBLE_ROW_COUNT * 2));
        Assert.AreEqual(short.MaxValue, terminal.GetTextBuffer().TotalRowCount);

        // More than short.MaxValue rows are clamped.
        terminal.UserResize(new TilSize(INITIAL_VISIBLE_COL_COUNT, INITIAL_VISIBLE_ROW_COUNT * 3));
        Assert.AreEqual(short.MaxValue, terminal.GetTextBuffer().TotalRowCount);

        // Back to the original size.
        terminal.UserResize(new TilSize(INITIAL_VISIBLE_COL_COUNT, INITIAL_VISIBLE_ROW_COUNT));
        Assert.AreEqual(historySize + INITIAL_VISIBLE_ROW_COUNT, terminal.GetTextBuffer().TotalRowCount);
    }
}
