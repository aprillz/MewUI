// Ported from Windows Terminal src/buffer/out/textBuffer.cpp (MIT): TextBuffer::Reflow.

using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Buffer;

/// <summary>Viewport tops to follow through a reflow; on return they hold the matching rows of the new buffer.</summary>
internal sealed class PositionInformation
{
    public int MutableViewportTop { get; set; }

    public int VisibleViewportTop { get; set; }
}

internal sealed partial class TextBuffer
{
    /// <summary>
    /// Copies <paramref name="oldBuffer"/> into <paramref name="newBuffer"/> of another width, rewrapping the lines that
    /// wrapped and keeping the cursor on the cell it was on.
    /// </summary>
    public static void Reflow(TextBuffer oldBuffer, TextBuffer newBuffer, Viewport? lastCharacterViewport = null, PositionInformation? positionInfo = null)
    {
        var oldCursor = oldBuffer.Cursor;
        var newCursor = newBuffer.Cursor;

        // Clamped so that a cursor outside the buffer cannot break the row counts or the copy loop below.
        var oldCursorPos = new TilPoint(
            Math.Clamp(oldCursor.Position.X, 0, oldBuffer._width - 1),
            Math.Clamp(oldCursor.Position.Y, 0, oldBuffer._height - 1));
        var newCursorPos = default(TilPoint);

        int lastRowWithText = oldBuffer.GetLastNonSpaceCharacter(lastCharacterViewport).Y;

        int mutableViewportTop = positionInfo?.MutableViewportTop ?? int.MaxValue;
        int visibleViewportTop = positionInfo?.VisibleViewportTop ?? int.MaxValue;

        int oldY = 0;
        int newY = 0;
        int newX = 0;
        int newWidth = newBuffer.Size.Width;
        int newYLimit = int.MaxValue;

        int oldHeight = Math.Max(lastRowWithText, oldCursorPos.Y) + 1;
        int newHeight = newBuffer.Size.Height;

        for (; oldY < oldHeight && newY < newYLimit; oldY++)
        {
            var oldRow = oldBuffer.GetRowByOffset(oldY);

            // Rows of another rendition are cut rather than rewrapped, as if the row before ended in a newline.
            if (oldRow.LineRendition != LineRendition.SingleWidth)
            {
                if (newX != 0)
                {
                    newX = 0;
                    newY++;
                }

                var renditionRow = newBuffer.GetMutableRowByOffset(newY);
                if (newY >= newHeight)
                {
                    renditionRow.Reset(newBuffer._initialAttributes);
                }

                renditionRow.CopyFrom(oldRow);
                renditionRow.SetWrapForced(false);

                if (oldY == oldCursorPos.Y)
                {
                    newCursorPos = new TilPoint(renditionRow.AdjustToGlyphStart(oldCursorPos.X), newY);
                }

                if (oldY >= mutableViewportTop)
                {
                    positionInfo!.MutableViewportTop = newY;
                    mutableViewportTop = int.MaxValue;
                }

                if (oldY >= visibleViewportTop)
                {
                    positionInfo!.VisibleViewportTop = newY;
                    visibleViewportTop = int.MaxValue;
                }

                newY++;
                continue;
            }

            // Trailing whitespace is dropped, but the cursor row keeps at least the cell under the cursor.
            int oldRowLimit = oldRow.MeasureRight();
            if (oldY == oldCursorPos.Y)
            {
                oldRowLimit = Math.Max(oldRowLimit, oldCursorPos.X + 1);
            }

            // A mark stays on the row its logical line starts on.
            if (oldRow.ScrollbarData != null)
            {
                newBuffer.GetMutableRowByOffset(newY).ScrollbarData = oldRow.ScrollbarData.Clone();
            }

            int oldX = 0;

            // At least one pass, so that wrapping happens and attributes of empty rows are copied.
            do
            {
                if (newX >= newWidth)
                {
                    newBuffer.GetMutableRowByOffset(newY).SetWrapForced(true);
                    newX = 0;
                    newY++;
                }

                // Shrinking writes some rows twice, so they are blanked first; the cursor row is never overwritten.
                if (newY >= newHeight && newX == 0)
                {
                    if (newY >= newYLimit)
                    {
                        break;
                    }

                    newBuffer.GetMutableRowByOffset(newY).Reset(newBuffer._initialAttributes);
                }

                var newRow = newBuffer.GetMutableRowByOffset(newY);

                var state = new RowCopyTextFromState(oldRow)
                {
                    ColumnBegin = newX,
                    ColumnLimit = int.MaxValue,
                    SourceColumnBegin = oldX,
                    SourceColumnLimit = oldRowLimit,
                };
                newRow.CopyTextFrom(ref state);

                var oldAttr = oldRow.Attributes;
                var newAttr = newRow.Attributes;
                var attributes = oldAttr.Slice(oldX, oldAttr.Size);
                newAttr.Replace(newX, newAttr.Size, attributes);
                newAttr.ResizeTrailingExtent(newWidth);

                if (oldY == oldCursorPos.Y && oldCursorPos.X >= oldX)
                {
                    newCursorPos = new TilPoint(newRow.AdjustToGlyphStart(oldCursorPos.X - oldX + newX), newY);

                    // Stop before text past the cursor could push the cursor row out of the new buffer.
                    newYLimit = newY + newHeight;
                }

                if (oldY >= mutableViewportTop)
                {
                    positionInfo!.MutableViewportTop = newY;
                    mutableViewportTop = int.MaxValue;
                }

                if (oldY >= visibleViewportTop)
                {
                    positionInfo!.VisibleViewportTop = newY;
                    visibleViewportTop = int.MaxValue;
                }

                oldX = state.SourceColumnEnd;
                newX = state.ColumnEnd;
            }
            while (oldX < oldRowLimit);

            if (!oldRow.WasWrapForced)
            {
                newX = 0;
                newY++;
            }
        }

        // The attribute copy below works on whole rows, so it starts on a fresh row.
        if (newX != 0)
        {
            newX = 0;
            newY++;
        }

        // Rows below the last text keep their colors (a "color 2f" background survives a resize).
        int initializedRowsEnd = oldBuffer.EstimateOffsetOfLastCommittedRow() + 1;
        for (; oldY < initializedRowsEnd && newY < newHeight; oldY++, newY++)
        {
            var oldRow = oldBuffer.GetRowByOffset(oldY);
            var newRow = newBuffer.GetMutableRowByOffset(newY);
            newRow.Attributes.CopyFrom(oldRow.Attributes);
            newRow.Attributes.ResizeTrailingExtent(newWidth);
        }

        // Rows were written past the end without IncrementCircularBuffer, so the first row and the cursor row are
        // mapped back from storage order here.
        if (newY > newHeight)
        {
            newBuffer._firstRow = newY % newHeight;
            newCursorPos = newCursorPos with { Y = (newCursorPos.Y - newBuffer._firstRow + newHeight) % newHeight };
        }

        newBuffer.CopyProperties(oldBuffer);
        newBuffer.CopyHyperlinkMaps(oldBuffer);

        newCursor.SetSize(oldCursor.Size);
        newCursor.SetPosition(newCursorPos);
    }
}
