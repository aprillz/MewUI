// Ported from Windows Terminal src/cascadia/UnitTests_TerminalCore/ScrollTest.cpp (MIT).

using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Core;

[TestClass]
public sealed class ScrollTests
{
    private const int TERMINAL_VIEW_WIDTH = 80;
    private const int TERMINAL_VIEW_HEIGHT = 32;

    // Around 9000 rows: a much smaller buffer would not reproduce the overflow this test guards against.
    private const int TERMINAL_HISTORY_LENGTH = 9001;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TestNotifyScrolling(bool notifyOnCircling)
    {
        // GH#5540: as newlines were written, a scroll delta accumulated until it overflowed and scroll
        // notifications stopped. This writes twice the buffer height of lines and checks every notification.
        var term = new TerminalCore(true);
        (int ViewportTop, int ViewportHeight, int BufferHeight)? scrollBarNotification = null;
        term.ScrollPositionChanged = (top, height, bottom) => scrollBarNotification = (top, height, bottom);

        var renderer = new Renderer();
        term.Create(new TilSize(TERMINAL_VIEW_WIDTH, TERMINAL_VIEW_HEIGHT), TERMINAL_HISTORY_LENGTH, renderer);
        term.AlwaysNotifyOnBufferRotation = notifyOnCircling;

        var termTb = term.MainBuffer;
        var termSm = term.StateMachine;
        int totalBufferSize = termTb.Size.Height;

        for (int currentRow = 0; currentRow < totalBufferSize * 2; currentRow++)
        {
            scrollBarNotification = null;
            renderer.Clear();

            termSm.ProcessString("X\r\n");

            // The line feed on the last viewport row is the first that scrolls.
            bool scrolled = currentRow >= TERMINAL_VIEW_HEIGHT - 1;

            // Circling the buffer leaves the scroll bar where it is, so it is only reported when asked for (GH#14045).
            bool circledBuffer = currentRow >= totalBufferSize - 1;
            bool expectScrollBarNotification = (scrolled && !circledBuffer) || (circledBuffer && notifyOnCircling);

            if (expectScrollBarNotification)
            {
                Assert.IsTrue(scrollBarNotification.HasValue, $"Expected a 'scroll bar position changed' notification for row {currentRow}");
            }
            else
            {
                Assert.IsFalse(scrollBarNotification.HasValue, $"Expected to not see a 'scroll bar position changed' notification for row {currentRow}");
            }

            // A scroll that circled the buffer tells the renderer the content moved up a row.
            if (scrolled && circledBuffer)
            {
                Assert.IsTrue(renderer.ScrollDelta.HasValue, $"Expected a 'trigger scroll' notification for row {currentRow}");
                Assert.AreEqual(new TilPoint(0, -1), renderer.ScrollDelta.Value, $"Wrong 'trigger scroll' delta for row {currentRow}");
            }
            else
            {
                Assert.IsFalse(renderer.ScrollDelta.HasValue, $"Expected to not see a 'trigger scroll' notification for row {currentRow}");
            }

            if (scrollBarNotification.HasValue)
            {
                var notification = scrollBarNotification.Value;
                int expectedTop = Math.Clamp(currentRow - TERMINAL_VIEW_HEIGHT + 2, 0, TERMINAL_HISTORY_LENGTH);
                int expectedHeight = TERMINAL_VIEW_HEIGHT;
                int expectedBottom = expectedTop + TERMINAL_VIEW_HEIGHT;
                Assert.AreEqual(expectedTop, notification.ViewportTop, $"Viewport top on line {currentRow}");
                Assert.AreEqual(expectedHeight, notification.ViewportHeight, $"Viewport height on line {currentRow}");
                Assert.AreEqual(expectedBottom, notification.BufferHeight, $"Buffer height on line {currentRow}");
            }
        }
    }
}
