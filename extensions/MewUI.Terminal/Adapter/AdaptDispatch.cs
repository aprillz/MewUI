// Ported from Windows Terminal src/terminal/adapter/adaptDispatch.hpp and adaptDispatch.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;
using Aprillz.MewUI.Terminal.Unicode;

namespace Aprillz.MewUI.Terminal.Adapter;

using CursorType = Aprillz.MewUI.Terminal.Buffer.CursorType;

/// <summary>Carries out the dispatched VT sequences on the terminal's buffers, modes and settings.</summary>
internal sealed partial class AdaptDispatch : TermDispatch
{
    [Flags]
    private enum Mode
    {
        None = 0,
        InsertReplace = 1 << 0,
        Origin = 1 << 1,
        Column = 1 << 2,
        AllowDECCOLM = 1 << 3,
        AllowDECSLRM = 1 << 4,
        SixelDisplay = 1 << 5,
        EraseColor = 1 << 6,
        RectangularChangeExtent = 1 << 7,
        PageCursorCoupling = 1 << 8,
    }

    /// <summary>What DECSC saves and DECRC restores.</summary>
    private sealed class CursorState
    {
        public int Row = 1;
        public int Column = 1;
        public int Page = 1;
        public bool IsDelayedEolWrap;
        public bool IsOriginModeRelative;
        public TextAttribute Attributes;
        public TerminalOutput TermOutput = new();
    }

    /// <summary>A cursor movement along one axis: to an absolute 1-based position, or by a relative distance.</summary>
    private readonly record struct Offset(int Value, bool IsAbsolute)
    {
        public static Offset Absolute(int value) => new(value - 1, true);

        public static Offset Forward(int value) => new(value, false);

        public static Offset Backward(int value) => new(-value, false);

        public static Offset Unchanged => Forward(0);
    }

    /// <summary>The attribute changes of DECCARA and DECRARA.</summary>
    private readonly record struct ChangeOps(
        CharacterAttributes AndAttrMask,
        CharacterAttributes XorAttrMask,
        TextColor? Foreground,
        TextColor? Background,
        TextColor? UnderlineColor);

    private const string WHITESPACE = " ";

    private readonly ITerminalApi _api;
    private readonly Renderer? _renderer;
    private readonly RenderSettings _renderSettings;
    private readonly TerminalInput _terminalInput;
    private readonly PageManager _pages;
    private readonly CursorState[] _savedCursorState = [new(), new()];
    private readonly SgrStack _sgrStack = new();
    private readonly List<bool> _tabStopColumns = new();
    private TerminalOutput _termOutput = new();
    private bool _initDefaultTabStops = true;
    private OptionalFeature _optionalFeatures = OptionalFeature.ClipboardWrite;
    private bool _usingAltBuffer;
    private int _scrollMarginTop;
    private int _scrollMarginBottom;
    private int _scrollMarginLeft;
    private int _scrollMarginRight;
    private Mode _modes = Mode.PageCursorCoupling;

    public AdaptDispatch(ITerminalApi api, Renderer? renderer, RenderSettings renderSettings, TerminalInput terminalInput)
    {
        _api = api;
        _renderer = renderer;
        _renderSettings = renderSettings;
        _terminalInput = terminalInput;
        _pages = new PageManager(api, renderer);
    }

    private bool HasMode(Mode mode) => (_modes & mode) != 0;

    private void SetModeFlag(Mode mode, bool enabled)
    {
        if (enabled)
        {
            _modes |= mode;
        }
        else
        {
            _modes &= ~mode;
        }
    }

    public override void Print(char printable)
    {
        char translated = _termOutput.TranslateKey(printable);

        // DEL is never printed, though a 96-character set can map it to something else.
        if (translated != AsciiChars.DEL)
        {
            WriteToBuffer([translated]);
        }
    }

    public override void PrintString(ReadOnlySpan<char> text)
    {
        if (_termOutput.NeedToTranslate)
        {
            Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
            for (int index = 0; index < text.Length; index++)
            {
                buffer[index] = _termOutput.TranslateKey(text[index]);
            }

            WriteToBuffer(buffer);
        }
        else
        {
            WriteToBuffer(text);
        }
    }

    private void WriteToBuffer(ReadOnlySpan<char> text)
    {
        var page = _pages.ActivePage();
        var textBuffer = page.Buffer;
        var cursor = page.Cursor;
        var cursorPosition = cursor.Position;
        bool wrapAtEol = _api.GetSystemMode(ITerminalApi.Mode.AutoWrap);
        var attributes = page.Attributes;

        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (_, rightMargin) = GetHorizontalMargins(page.Width);

        // The right margin only limits the line when the cursor starts inside the margins.
        int lineWidth = textBuffer.GetLineWidth(cursorPosition.Y);
        if (cursorPosition.X <= rightMargin && cursorPosition.Y >= topMargin && cursorPosition.Y <= bottomMargin)
        {
            lineWidth = Math.Min(lineWidth, rightMargin + 1);
        }

        var state = new RowWriteState(text, 0, lineWidth);

        while (!state.Text.IsEmpty)
        {
            if (cursor.DelayEolWrap is TilPoint delayedCursorPosition && wrapAtEol)
            {
                cursor.ResetDelayEolWrap();

                // The delayed wrap only applies if the cursor has not moved since.
                if (delayedCursorPosition == cursorPosition)
                {
                    if (DoLineFeed(page, true, true))
                    {
                        page.MoveViewportDown();
                        (topMargin, bottomMargin) = GetVerticalMargins(page, true);
                    }

                    cursorPosition = cursor.Position;

                    // A new line inside the margins is limited by the right margin.
                    lineWidth = textBuffer.GetLineWidth(cursorPosition.Y);
                    if (cursorPosition.Y >= topMargin && cursorPosition.Y <= bottomMargin)
                    {
                        lineWidth = Math.Min(lineWidth, rightMargin + 1);
                    }

                    state.ColumnLimit = lineWidth;
                }
            }

            state.ColumnBegin = cursorPosition.X;

            int lengthBefore = state.Text.Length;
            if (HasMode(Mode.InsertReplace))
            {
                textBuffer.Insert(cursorPosition.Y, attributes, ref state);
            }
            else
            {
                textBuffer.Replace(cursorPosition.Y, attributes, ref state);
            }

            int lengthAfter = state.Text.Length;

            bool isWrapping = state.ColumnEnd >= state.ColumnLimit;
            cursorPosition = cursorPosition with { X = isWrapping ? state.ColumnLimit - 1 : state.ColumnEnd };
            cursor.SetPosition(cursorPosition);

            if (isWrapping)
            {
                // A glyph that fits nowhere (wide glyph in one column, or no wrap) is dropped to avoid looping.
                if (lengthBefore == lengthAfter && (state.ColumnBegin == 0 || !wrapAtEol))
                {
                    state.Text = state.Text[TextBuffer.GraphemeNext(state.Text, 0)..];
                }

                if (wrapAtEol)
                {
                    cursor.DelayEolWrapAtCurrentPosition();
                }
            }
        }

        textBuffer.TriggerNewTextNotification(text);
    }

    public override void CursorUp(int distance) => CursorMovePosition(Offset.Backward(distance), Offset.Unchanged, true);

    public override void CursorDown(int distance) => CursorMovePosition(Offset.Forward(distance), Offset.Unchanged, true);

    public override void CursorForward(int distance) => CursorMovePosition(Offset.Unchanged, Offset.Forward(distance), true);

    public override void CursorBackward(int distance) => CursorMovePosition(Offset.Unchanged, Offset.Backward(distance), true);

    public override void CursorNextLine(int distance) => CursorMovePosition(Offset.Forward(distance), Offset.Absolute(1), true);

    public override void CursorPrevLine(int distance) => CursorMovePosition(Offset.Backward(distance), Offset.Absolute(1), true);

    /// <summary>The top and bottom margins, page-relative or (with <paramref name="absolute"/>) buffer rows.</summary>
    private (int Top, int Bottom) GetVerticalMargins(in Page page, bool absolute)
    {
        // Margins that no longer fit the page are reset.
        int bottommostRow = page.Height - 1;
        if (_scrollMarginTop >= bottommostRow)
        {
            _scrollMarginTop = 0;
            _scrollMarginBottom = 0;
        }

        bool marginsSet = _scrollMarginTop < _scrollMarginBottom;
        int topMargin = marginsSet ? _scrollMarginTop : 0;
        int bottomMargin = marginsSet ? _scrollMarginBottom : bottommostRow;
        bottomMargin = Math.Min(bottomMargin, bottommostRow);
        if (absolute)
        {
            topMargin += page.Top;
            bottomMargin += page.Top;
        }

        return (topMargin, bottomMargin);
    }

    private (int Left, int Right) GetHorizontalMargins(int pageWidth)
    {
        int rightmostColumn = pageWidth - 1;
        if (_scrollMarginLeft >= rightmostColumn)
        {
            _scrollMarginLeft = 0;
            _scrollMarginRight = 0;
        }

        bool marginsSet = _scrollMarginLeft < _scrollMarginRight;
        int leftMargin = marginsSet ? _scrollMarginLeft : 0;
        int rightMargin = marginsSet ? _scrollMarginRight : rightmostColumn;
        rightMargin = Math.Min(rightMargin, rightmostColumn);
        return (leftMargin, rightMargin);
    }

    private void CursorMovePosition(Offset rowOffset, Offset colOffset, bool clampInMargins)
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        int pageWidth = page.Width;
        var cursorPosition = cursor.Position;
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (leftMargin, rightMargin) = GetHorizontalMargins(pageWidth);

        int row = cursorPosition.Y;
        int col = cursorPosition.X;

        // Absolute positions count from the margins in origin mode.
        if (rowOffset.IsAbsolute)
        {
            row = HasMode(Mode.Origin) ? topMargin : page.Top;
        }

        if (colOffset.IsAbsolute)
        {
            col = HasMode(Mode.Origin) ? leftMargin : 0;
        }

        row = Math.Clamp(row + rowOffset.Value, page.Top, page.Bottom - 1);
        col = Math.Clamp(col + colOffset.Value, 0, pageWidth - 1);

        // Relative moves (and any move in origin mode) stop at a margin the cursor started inside of.
        if (clampInMargins || HasMode(Mode.Origin))
        {
            if (cursorPosition.X >= leftMargin && cursorPosition.X <= rightMargin)
            {
                if (cursorPosition.Y >= topMargin)
                {
                    row = Math.Max(row, topMargin);
                }

                if (cursorPosition.Y <= bottomMargin)
                {
                    row = Math.Min(row, bottomMargin);
                }
            }

            if (row >= topMargin && row <= bottomMargin)
            {
                if (cursorPosition.X >= leftMargin)
                {
                    col = Math.Max(col, leftMargin);
                }

                if (cursorPosition.X <= rightMargin)
                {
                    col = Math.Min(col, rightMargin);
                }
            }
        }

        cursor.SetPosition(page.Buffer.ClampPositionWithinLine(new TilPoint(col, row)));
    }

    public override void CursorHorizontalPositionAbsolute(int column) => CursorMovePosition(Offset.Unchanged, Offset.Absolute(column), false);

    public override void VerticalLinePositionAbsolute(int line) => CursorMovePosition(Offset.Absolute(line), Offset.Unchanged, false);

    public override void HorizontalPositionRelative(int distance) => CursorMovePosition(Offset.Unchanged, Offset.Forward(distance), false);

    public override void VerticalPositionRelative(int distance) => CursorMovePosition(Offset.Forward(distance), Offset.Unchanged, false);

    public override void CursorPosition(int line, int column) => CursorMovePosition(Offset.Absolute(line), Offset.Absolute(column), false);

    public override void CursorSaveState()
    {
        var page = _pages.ActivePage();

        // The position is saved page-relative, and margin-relative in origin mode.
        var cursorPosition = page.Cursor.Position;
        cursorPosition = cursorPosition with { Y = cursorPosition.Y - page.Top };
        if (HasMode(Mode.Origin))
        {
            cursorPosition = new TilPoint(
                cursorPosition.X - GetHorizontalMargins(page.Width).Left,
                cursorPosition.Y - GetVerticalMargins(page, false).Top);
        }

        var savedCursorState = _savedCursorState[_usingAltBuffer ? 1 : 0];
        savedCursorState.Column = cursorPosition.X + 1;
        savedCursorState.Row = cursorPosition.Y + 1;
        savedCursorState.Page = page.Number;
        savedCursorState.IsDelayedEolWrap = page.Cursor.DelayEolWrap.HasValue;
        savedCursorState.IsOriginModeRelative = HasMode(Mode.Origin);
        savedCursorState.Attributes = page.Attributes;
        savedCursorState.TermOutput = _termOutput.Clone();
    }

    public override void CursorRestoreState()
    {
        var savedCursorState = _savedCursorState[_usingAltBuffer ? 1 : 0];

        // Origin mode is restored first, since the position may be relative to it.
        SetModeFlag(Mode.Origin, savedCursorState.IsOriginModeRelative);

        PagePositionAbsolute(savedCursorState.Page);
        CursorPosition(savedCursorState.Row, savedCursorState.Column);

        var page = _pages.ActivePage();
        if (savedCursorState.IsDelayedEolWrap)
        {
            page.Cursor.DelayEolWrapAtCurrentPosition();
        }

        page.SetAttributes(savedCursorState.Attributes);
        _termOutput.RestoreFrom(savedCursorState.TermOutput);
    }

    /// <summary>The attributes erased cells get: the current colors (DECECM reset) or the defaults (DECECM set).</summary>
    private TextAttribute GetEraseAttributes(in Page page)
    {
        if (HasMode(Mode.EraseColor))
        {
            return default;
        }
        else
        {
            var eraseAttributes = page.Attributes;
            eraseAttributes.SetStandardErase();
            return eraseAttributes;
        }
    }

    /// <summary>Scrolls a rectangle down by <paramref name="delta"/> rows (up when negative), erasing the rows uncovered.</summary>
    private void ScrollRectVertically(in Page page, TilRect scrollRect, int delta)
    {
        var textBuffer = page.Buffer;
        int absoluteDelta = Math.Min(Math.Abs(delta), scrollRect.Height);
        if (absoluteDelta < scrollRect.Height)
        {
            int top = delta > 0 ? scrollRect.Top : scrollRect.Top + absoluteDelta;
            int width = scrollRect.Width;
            int height = scrollRect.Height - absoluteDelta;
            int actualDelta = delta > 0 ? absoluteDelta : -absoluteDelta;
            if (width == page.Width)
            {
                // Full-width scrolls move whole rows, which also keeps line renditions with them.
                textBuffer.ScrollRows(top, height, actualDelta);
                textBuffer.TriggerRedraw(Viewport.FromExclusive(scrollRect));
            }
            else
            {
                var sourceView = Viewport.FromDimensions(new TilPoint(scrollRect.Left, top), new TilSize(width, height));
                var targetView = Viewport.FromDimensions(new TilPoint(scrollRect.Left, top + actualDelta), new TilSize(width, height));
                CopyCells(textBuffer, sourceView, textBuffer, targetView, false);
            }
        }

        var eraseRect = scrollRect with { Top = delta > 0 ? scrollRect.Top : scrollRect.Bottom - absoluteDelta };
        eraseRect = eraseRect with { Bottom = eraseRect.Top + absoluteDelta };
        var eraseAttributes = GetEraseAttributes(page);
        FillRect(page, eraseRect, WHITESPACE, eraseAttributes);

        // Erased rows go back to single width.
        textBuffer.ResetLineRenditionRange(eraseRect.Top, eraseRect.Bottom);
    }

    /// <summary>Scrolls a rectangle right by <paramref name="delta"/> columns (left when negative), erasing the columns uncovered.</summary>
    private void ScrollRectHorizontally(in Page page, TilRect scrollRect, int delta)
    {
        var textBuffer = page.Buffer;
        int absoluteDelta = Math.Min(Math.Abs(delta), scrollRect.Width);
        if (absoluteDelta < scrollRect.Width)
        {
            int left = delta > 0 ? scrollRect.Left : scrollRect.Left + absoluteDelta;
            int top = scrollRect.Top;
            int width = scrollRect.Width - absoluteDelta;
            int height = scrollRect.Height;
            int actualDelta = delta > 0 ? absoluteDelta : -absoluteDelta;

            var sourceView = Viewport.FromDimensions(new TilPoint(left, top), new TilSize(width, height));
            var targetView = Viewport.Offset(sourceView, new TilPoint(actualDelta, 0));
            CopyCells(textBuffer, sourceView, textBuffer, targetView, true);
        }

        var eraseRect = scrollRect with { Left = delta > 0 ? scrollRect.Left : scrollRect.Right - absoluteDelta };
        eraseRect = eraseRect with { Right = eraseRect.Left + absoluteDelta };
        var eraseAttributes = GetEraseAttributes(page);
        FillRect(page, eraseRect, WHITESPACE, eraseAttributes);
    }

    /// <summary>
    /// Copies cells one at a time in the direction that keeps an overlapping source intact. With
    /// <paramref name="readAhead"/> the next source cell is read before the current one is written, as a row shift needs.
    /// </summary>
    private static void CopyCells(TextBuffer sourceBuffer, Viewport sourceView, TextBuffer targetBuffer, Viewport targetView, bool readAhead)
    {
        int walkDirection = Viewport.DetermineWalkDirection(sourceView, targetView);
        var sourcePos = sourceView.GetWalkOrigin(walkDirection);
        var targetPos = targetView.GetWalkOrigin(walkDirection);
        if (readAhead)
        {
            var next = sourceBuffer.ReadCell(sourcePos);
            do
            {
                var current = next;
                sourceView.WalkInBounds(ref sourcePos, walkDirection);
                next = sourceBuffer.ReadCell(sourcePos);
                targetBuffer.WriteCell(targetPos, current.Glyph, current.DbcsAttribute, current.Attribute);
            }
            while (targetView.WalkInBounds(ref targetPos, walkDirection));
        }
        else
        {
            do
            {
                var current = sourceBuffer.ReadCell(sourcePos);
                targetBuffer.WriteCell(targetPos, current.Glyph, current.DbcsAttribute, current.Attribute);
                sourceView.WalkInBounds(ref sourcePos, walkDirection);
            }
            while (targetView.WalkInBounds(ref targetPos, walkDirection));
        }
    }

    private void InsertDeleteCharacterHelper(int delta)
    {
        var page = _pages.ActivePage();
        int row = page.Cursor.Position.Y;
        int col = page.Cursor.Position.X;
        int lineWidth = page.Buffer.GetLineWidth(row);
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);

        // The horizontal margins only apply to rows inside the vertical margins.
        var (leftMargin, rightMargin) = row >= topMargin && row <= bottomMargin ? GetHorizontalMargins(lineWidth) : (0, lineWidth - 1);
        if (col >= leftMargin && col <= rightMargin)
        {
            ScrollRectHorizontally(page, new TilRect(col, row, rightMargin + 1, row + 1), delta);
            page.Cursor.ResetDelayEolWrap();
        }
    }

    public override void InsertCharacter(int count) => InsertDeleteCharacterHelper(count);

    public override void DeleteCharacter(int count) => InsertDeleteCharacterHelper(-count);

    private static void FillRect(in Page page, TilRect fillRect, ReadOnlySpan<char> fillChar, TextAttribute fillAttrs)
        => page.Buffer.FillRect(fillRect, fillChar, fillAttrs);

    public override void EraseCharacters(int numChars)
    {
        var page = _pages.ActivePage();
        int row = page.Cursor.Position.Y;
        int startCol = page.Cursor.Position.X;
        int endCol = Math.Min(startCol + numChars, page.Buffer.GetLineWidth(row));

        page.Cursor.ResetDelayEolWrap();

        var eraseAttributes = GetEraseAttributes(page);
        FillRect(page, new TilRect(startCol, row, endCol, row + 1), WHITESPACE, eraseAttributes);
    }

    public override void EraseInDisplay(EraseType eraseType)
    {
        if (eraseType > EraseType.Scrollback)
        {
            return;
        }

        if (eraseType == EraseType.Scrollback)
        {
            EraseScrollback();
            return;
        }
        else if (eraseType == EraseType.All)
        {
            EraseAll();
            return;
        }

        var page = _pages.ActivePage();
        var textBuffer = page.Buffer;
        int pageWidth = page.Width;
        int row = page.Cursor.Position.Y;
        int col = page.Cursor.Position.X;

        page.Cursor.ResetDelayEolWrap();

        var eraseAttributes = GetEraseAttributes(page);

        // Rows erased in full go back to single width; the cursor row keeps its rendition.
        if (eraseType == EraseType.FromBeginning)
        {
            textBuffer.ResetLineRenditionRange(page.Top, row);
            FillRect(page, new TilRect(0, page.Top, pageWidth, row), WHITESPACE, eraseAttributes);
            FillRect(page, new TilRect(0, row, col + 1, row + 1), WHITESPACE, eraseAttributes);
        }

        if (eraseType == EraseType.ToEnd)
        {
            textBuffer.ResetLineRenditionRange(col > 0 ? row + 1 : row, page.Bottom);
            FillRect(page, new TilRect(col, row, pageWidth, row + 1), WHITESPACE, eraseAttributes);
            FillRect(page, new TilRect(0, row + 1, pageWidth, page.Bottom), WHITESPACE, eraseAttributes);
        }
    }

    public override void EraseInLine(EraseType eraseType)
    {
        var page = _pages.ActivePage();
        var textBuffer = page.Buffer;
        int row = page.Cursor.Position.Y;
        int col = page.Cursor.Position.X;

        page.Cursor.ResetDelayEolWrap();

        var eraseAttributes = GetEraseAttributes(page);
        switch (eraseType)
        {
            case EraseType.FromBeginning:
                FillRect(page, new TilRect(0, row, col + 1, row + 1), WHITESPACE, eraseAttributes);
                break;
            case EraseType.ToEnd:
                FillRect(page, new TilRect(col, row, textBuffer.GetLineWidth(row), row + 1), WHITESPACE, eraseAttributes);
                break;
            case EraseType.All:
                FillRect(page, new TilRect(0, row, textBuffer.GetLineWidth(row), row + 1), WHITESPACE, eraseAttributes);
                break;
        }
    }

    /// <summary>Erases the characters of the unprotected cells in a rectangle, keeping their attributes.</summary>
    private static void SelectiveEraseRect(in Page page, TilRect eraseRect)
    {
        if (eraseRect.IsEmpty)
        {
            return;
        }

        for (int row = eraseRect.Top; row < eraseRect.Bottom; row++)
        {
            var rowBuffer = page.Buffer.GetMutableRowByOffset(row);
            for (int col = eraseRect.Left; col < eraseRect.Right; col++)
            {
                if (!rowBuffer.GetAttrByColumn(col).IsProtected())
                {
                    rowBuffer.ClearCell(col);
                    page.Buffer.TriggerRedraw(Viewport.FromDimensions(new TilPoint(col, row), new TilSize(1, 1)));
                }
            }
        }
    }

    public override void SelectiveEraseInDisplay(EraseType eraseType)
    {
        var page = _pages.ActivePage();
        int pageWidth = page.Width;
        int row = page.Cursor.Position.Y;
        int col = page.Cursor.Position.X;

        page.Cursor.ResetDelayEolWrap();

        switch (eraseType)
        {
            case EraseType.FromBeginning:
                SelectiveEraseRect(page, new TilRect(0, page.Top, pageWidth, row));
                SelectiveEraseRect(page, new TilRect(0, row, col + 1, row + 1));
                break;
            case EraseType.ToEnd:
                SelectiveEraseRect(page, new TilRect(col, row, pageWidth, row + 1));
                SelectiveEraseRect(page, new TilRect(0, row + 1, pageWidth, page.Bottom));
                break;
            case EraseType.All:
                SelectiveEraseRect(page, new TilRect(0, page.Top, pageWidth, page.Bottom));
                break;
        }
    }

    public override void SelectiveEraseInLine(EraseType eraseType)
    {
        var page = _pages.ActivePage();
        var textBuffer = page.Buffer;
        int row = page.Cursor.Position.Y;
        int col = page.Cursor.Position.X;

        page.Cursor.ResetDelayEolWrap();

        switch (eraseType)
        {
            case EraseType.FromBeginning:
                SelectiveEraseRect(page, new TilRect(0, row, col + 1, row + 1));
                break;
            case EraseType.ToEnd:
                SelectiveEraseRect(page, new TilRect(col, row, textBuffer.GetLineWidth(row), row + 1));
                break;
            case EraseType.All:
                SelectiveEraseRect(page, new TilRect(0, row, textBuffer.GetLineWidth(row), row + 1));
                break;
        }
    }

    private static void ChangeRectAttributes(in Page page, TilRect changeRect, in ChangeOps changeOps)
    {
        if (changeRect.IsEmpty)
        {
            return;
        }

        for (int row = changeRect.Top; row < changeRect.Bottom; row++)
        {
            var rowBuffer = page.Buffer.GetMutableRowByOffset(row);
            for (int col = changeRect.Left; col < changeRect.Right; col++)
            {
                var attr = rowBuffer.GetAttrByColumn(col);
                var characterAttributes = attr.CharacterAttributes;
                characterAttributes &= changeOps.AndAttrMask;
                characterAttributes ^= changeOps.XorAttrMask;
                attr.CharacterAttributes = characterAttributes;
                if (changeOps.Foreground is TextColor foreground)
                {
                    attr.SetForeground(foreground);
                }

                if (changeOps.Background is TextColor background)
                {
                    attr.SetBackground(background);
                }

                if (changeOps.UnderlineColor is TextColor underlineColor)
                {
                    attr.SetUnderlineColor(underlineColor);
                }

                rowBuffer.ReplaceAttributes(col, col + 1, attr);
            }
        }

        page.Buffer.TriggerRedraw(Viewport.FromExclusive(changeRect));
    }

    /// <summary>Changes attributes over a rectangle, or over the stream of cells between its corners (DECSACE).</summary>
    private void ChangeRectOrStreamAttributes(TilRect changeArea, in ChangeOps changeOps)
    {
        var page = _pages.ActivePage();
        var changeRect = CalculateRectArea(page, changeArea.Top, changeArea.Left, changeArea.Bottom, changeArea.Right);
        int lineCount = changeRect.Height;

        if (HasMode(Mode.RectangularChangeExtent) || lineCount == 1)
        {
            ChangeRectAttributes(page, changeRect, changeOps);
        }
        else if (lineCount > 1 && changeRect.Right > changeRect.Left)
        {
            // A stream is the rest of the first line, the full lines between, and the start of the last line.
            int pageWidth = page.Width;
            ChangeRectAttributes(page, new TilRect(changeRect.Origin, new TilSize(pageWidth - changeRect.Left, 1)), changeOps);
            ChangeRectAttributes(page, new TilRect(new TilPoint(0, changeRect.Top + 1), new TilSize(pageWidth, lineCount - 2)), changeOps);
            ChangeRectAttributes(page, new TilRect(new TilPoint(0, changeRect.Bottom - 1), new TilSize(changeRect.Right, 1)), changeOps);
        }
    }

    /// <summary>Converts 1-based rectangle parameters (0 meaning the far edge) to buffer coordinates, honoring origin mode.</summary>
    private TilRect CalculateRectArea(in Page page, int top, int left, int bottom, int right)
    {
        int pageWidth = page.Width;
        int pageHeight = page.Height;

        var (topMargin, bottomMargin) = GetVerticalMargins(page, false);
        var (leftMargin, rightMargin) = GetHorizontalMargins(pageWidth);
        int yOffset = HasMode(Mode.Origin) ? topMargin : 0;
        int yMaximum = HasMode(Mode.Origin) ? bottomMargin + 1 : pageHeight;
        int xOffset = HasMode(Mode.Origin) ? leftMargin : 0;
        int xMaximum = HasMode(Mode.Origin) ? rightMargin + 1 : pageWidth;

        int fillLeft = left + xOffset;
        int fillTop = top + yOffset;
        int fillRight = right != 0 ? right + xOffset : xMaximum;
        int fillBottom = bottom != 0 ? bottom + yOffset : yMaximum;

        fillLeft = Math.Min(fillLeft, xMaximum) - 1;
        fillRight = Math.Min(fillRight, xMaximum) - 1;
        fillTop = Math.Min(fillTop, yMaximum) - 1;
        fillBottom = Math.Min(fillBottom, yMaximum) - 1;

        fillTop += page.Top;
        fillBottom += page.Top;

        return new TilInclusiveRect(fillLeft, fillTop, fillRight, fillBottom).ToExclusive();
    }

    public override void ChangeAttributesRectangularArea(int top, int left, int bottom, int right, VTParameters attrs)
    {
        // Applying the options to all-off and all-on attributes shows which bits each option sets and clears.
        var allAttrsOff = default(TextAttribute);
        var allAttrsOn = new TextAttribute(0, 0, 0);
        allAttrsOn.CharacterAttributes = CharacterAttributes.All;
        ApplyGraphicsOptions(attrs, ref allAttrsOff);
        ApplyGraphicsOptions(attrs, ref allAttrsOn);
        var orAttrMask = allAttrsOff.CharacterAttributes;
        var andAttrMask = allAttrsOn.CharacterAttributes;

        // A color counts as changed if it was set, or if it was reset to the default.
        var foreground = allAttrsOff.Foreground;
        var background = allAttrsOff.Background;
        bool foregroundChanged = !foreground.IsDefault() || allAttrsOn.Foreground.IsDefault();
        bool backgroundChanged = !background.IsDefault() || allAttrsOn.Background.IsDefault();
        var underlineColor = allAttrsOff.UnderlineColor;
        bool underlineColorChanged = !underlineColor.IsDefault() || allAttrsOn.UnderlineColor.IsDefault();

        var changeOps = new ChangeOps(
            andAttrMask & ~orAttrMask,
            orAttrMask,
            foregroundChanged ? foreground : null,
            backgroundChanged ? background : null,
            underlineColorChanged ? underlineColor : null);
        ChangeRectOrStreamAttributes(new TilRect(left, top, right, bottom), changeOps);
    }

    public override void ReverseAttributesRectangularArea(int top, int left, int bottom, int right, VTParameters attrs)
    {
        var reverseMask = CharacterAttributes.Normal;
        if (!attrs.IsEmpty)
        {
            for (int index = 0; index < attrs.Size;)
            {
                // A 0 reverses every rendition attribute, with a single underline as the underline.
                if (attrs.At(index).ValueOr(0) == 0)
                {
                    var singlyUnderlinedAttr = (CharacterAttributes)((int)UnderlineStyle.SinglyUnderlined << TextAttribute.UNDERLINE_STYLE_SHIFT);
                    reverseMask ^= (CharacterAttributes.Rendition & ~CharacterAttributes.UnderlineStyle) | singlyUnderlinedAttr;
                    index++;
                }
                else
                {
                    var allAttrsOff = default(TextAttribute);
                    index += ApplyGraphicsOption(attrs, index, ref allAttrsOff);
                    reverseMask ^= allAttrsOff.CharacterAttributes;
                }
            }
        }

        if (reverseMask != CharacterAttributes.Normal)
        {
            var changeOps = new ChangeOps(CharacterAttributes.All, reverseMask, null, null, null);
            ChangeRectOrStreamAttributes(new TilRect(left, top, right, bottom), changeOps);
        }
    }

    public override void CopyRectangularArea(int top, int left, int bottom, int right, int page, int destinationTop, int destinationLeft, int destinationPage)
    {
        var source = _pages.Get(page);
        var target = _pages.Get(destinationPage);
        var sourceRect = CalculateRectArea(source, top, left, bottom, right);
        int targetBottom = destinationTop + sourceRect.Height - 1;
        int targetRight = destinationLeft + sourceRect.Width - 1;
        var targetRect = CalculateRectArea(target, destinationTop, destinationLeft, targetBottom, targetRight);

        if (!targetRect.IsEmpty && (targetRect.Origin != sourceRect.Origin || source.Number != target.Number))
        {
            // The target may be clipped, so its size limits the source too.
            var sourceView = Viewport.FromDimensions(sourceRect.Origin, targetRect.Size);
            var targetView = Viewport.FromDimensions(targetRect.Origin, targetRect.Size);
            int walkDirection = Viewport.DetermineWalkDirection(sourceView, targetView);
            var sourcePos = sourceView.GetWalkOrigin(walkDirection);
            var targetPos = targetView.GetWalkOrigin(walkDirection);
            var next = source.Buffer.ReadCell(sourcePos);
            do
            {
                var current = next;
                var currentSourcePos = sourcePos;
                sourceView.WalkInBounds(ref sourcePos, walkDirection);
                next = source.Buffer.ReadCell(sourcePos);

                // Cells past the end of a double-width line are not copied.
                if (currentSourcePos.X < source.Buffer.GetLineWidth(currentSourcePos.Y))
                {
                    target.Buffer.WriteCell(targetPos, current.Glyph, current.DbcsAttribute, current.Attribute);
                }
            }
            while (targetView.WalkInBounds(ref targetPos, walkDirection));
        }
    }

    public override void FillRectangularArea(VTParameter character, int top, int left, int bottom, int right)
    {
        var page = _pages.ActivePage();
        var fillRect = CalculateRectArea(page, top, left, bottom, right);

        // Only printable characters are allowed; 0 means space.
        int charValue = character.ValueOr(0) == 0 ? 32 : character.Value;
        bool glChar = charValue >= 32 && charValue <= 126;
        bool grChar = charValue >= 160 && charValue <= 255;
        bool unicodeChar = charValue >= 256 && charValue <= 65535;
        if (glChar || grChar || unicodeChar)
        {
            char fillChar = _termOutput.TranslateKey((char)charValue);
            FillRect(page, fillRect, [fillChar], page.Attributes);
        }
    }

    public override void EraseRectangularArea(int top, int left, int bottom, int right)
    {
        var page = _pages.ActivePage();
        var eraseRect = CalculateRectArea(page, top, left, bottom, right);
        var eraseAttributes = GetEraseAttributes(page);
        FillRect(page, eraseRect, WHITESPACE, eraseAttributes);
    }

    public override void SelectiveEraseRectangularArea(int top, int left, int bottom, int right)
    {
        var page = _pages.ActivePage();
        var eraseRect = CalculateRectArea(page, top, left, bottom, right);
        SelectiveEraseRect(page, eraseRect);
    }

    public override void SelectAttributeChangeExtent(ChangeExtent changeExtent)
    {
        switch (changeExtent)
        {
            case ChangeExtent.Default:
            case ChangeExtent.Stream:
                SetModeFlag(Mode.RectangularChangeExtent, false);
                break;
            case ChangeExtent.Rectangle:
                SetModeFlag(Mode.RectangularChangeExtent, true);
                break;
        }
    }

    public override void SetLineRendition(LineRendition rendition)
    {
        // Line renditions are not allowed while left and right margins can be set.
        if (!HasMode(Mode.AllowDECSLRM))
        {
            var page = _pages.ActivePage();
            var eraseAttributes = GetEraseAttributes(page);
            page.Buffer.SetCurrentLineRendition(rendition, eraseAttributes);

            // The line rendition can change the line width, which invalidates a delayed wrap.
            page.Cursor.ResetDelayEolWrap();
        }
    }

    private void ScrollMovement(int delta)
    {
        var page = _pages.ActivePage();
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (leftMargin, rightMargin) = GetHorizontalMargins(page.Width);
        ScrollRectVertically(page, new TilRect(leftMargin, topMargin, rightMargin + 1, bottomMargin + 1), delta);
    }

    public override void ScrollUp(int distance) => ScrollMovement(-distance);

    public override void ScrollDown(int distance) => ScrollMovement(distance);

    public override void NextPage(int pageCount)
    {
        PagePositionRelative(pageCount);
        CursorPosition(1, 1);
    }

    public override void PrecedingPage(int pageCount)
    {
        PagePositionBack(pageCount);
        CursorPosition(1, 1);
    }

    public override void PagePositionAbsolute(int page) => _pages.MoveTo(page, HasMode(Mode.PageCursorCoupling));

    public override void PagePositionRelative(int pageCount) => _pages.MoveRelative(pageCount, HasMode(Mode.PageCursorCoupling));

    public override void PagePositionBack(int pageCount) => _pages.MoveRelative(-pageCount, HasMode(Mode.PageCursorCoupling));

    private void InsertDeleteLineHelper(int delta)
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        int col = cursor.Position.X;
        int row = cursor.Position.Y;

        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (leftMargin, rightMargin) = GetHorizontalMargins(page.Width);
        if (row >= topMargin && row <= bottomMargin && col >= leftMargin && col <= rightMargin)
        {
            ScrollRectVertically(page, new TilRect(leftMargin, row, rightMargin + 1, bottomMargin + 1), delta);

            // IL and DL also move the cursor to the left margin.
            cursor.SetXPosition(leftMargin);
        }
    }

    public override void InsertLine(int distance) => InsertDeleteLineHelper(distance);

    public override void DeleteLine(int distance) => InsertDeleteLineHelper(-distance);

    private void InsertDeleteColumnHelper(int delta)
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        int col = cursor.Position.X;
        int row = cursor.Position.Y;

        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (leftMargin, rightMargin) = GetHorizontalMargins(page.Width);
        if (row >= topMargin && row <= bottomMargin && col >= leftMargin && col <= rightMargin)
        {
            ScrollRectHorizontally(page, new TilRect(col, topMargin, rightMargin + 1, bottomMargin + 1), delta);
        }
    }

    public override void InsertColumn(int distance) => InsertDeleteColumnHelper(distance);

    public override void DeleteColumn(int distance) => InsertDeleteColumnHelper(-distance);

    private void DoSetTopBottomScrollingMargins(int topMargin, int bottomMargin, bool homeCursor = false)
    {
        int actualTop = topMargin;
        int actualBottom = bottomMargin;

        var page = _pages.ActivePage();
        int pageHeight = page.Height;

        // 0 means the page edge.
        if (actualTop == 0)
        {
            actualTop = 1;
        }

        if (actualBottom == 0)
        {
            actualBottom = pageHeight;
        }

        // Margins of fewer than two lines, or past the page, are ignored.
        if (actualTop < actualBottom && actualBottom <= pageHeight)
        {
            // Margins at the page edges are stored as "not set".
            if (actualTop == 1 && actualBottom == pageHeight)
            {
                actualTop = 0;
                actualBottom = 0;
            }
            else
            {
                actualTop -= 1;
                actualBottom -= 1;
            }

            _scrollMarginTop = actualTop;
            _scrollMarginBottom = actualBottom;
            if (homeCursor)
            {
                CursorPosition(1, 1);
            }
        }
    }

    public override void SetTopBottomScrollingMargins(int topMargin, int bottomMargin)
        => DoSetTopBottomScrollingMargins(topMargin, bottomMargin, true);

    private void DoSetLeftRightScrollingMargins(int leftMargin, int rightMargin, bool homeCursor = false)
    {
        int actualLeft = leftMargin;
        int actualRight = rightMargin;

        var page = _pages.ActivePage();
        int pageWidth = page.Width;

        if (actualLeft == 0)
        {
            actualLeft = 1;
        }

        if (actualRight == 0)
        {
            actualRight = pageWidth;
        }

        if (actualLeft < actualRight && actualRight <= pageWidth)
        {
            if (actualLeft == 1 && actualRight == pageWidth)
            {
                actualLeft = 0;
                actualRight = 0;
            }
            else
            {
                actualLeft -= 1;
                actualRight -= 1;
            }

            _scrollMarginLeft = actualLeft;
            _scrollMarginRight = actualRight;
            if (homeCursor)
            {
                CursorPosition(1, 1);
            }
        }
    }

    public override void SetLeftRightScrollingMargins(int leftMargin, int rightMargin)
    {
        // Without DECLRMM, CSI s is the SCOSC save cursor.
        if (HasMode(Mode.AllowDECSLRM))
        {
            DoSetLeftRightScrollingMargins(leftMargin, rightMargin, true);
        }
        else
        {
            CursorSaveState();
        }
    }

    public override void EnquireAnswerback() => _api.ReturnAnswerback();

    public override void WarningBell() => _api.WarningBell();

    public override void CarriageReturn() => CursorMovePosition(Offset.Unchanged, Offset.Absolute(1), true);

    /// <summary>Moves the cursor down a line, scrolling at the bottom margin; true when the viewport moved down instead.</summary>
    private bool DoLineFeed(in Page page, bool withReturn, bool wrapForced)
    {
        var textBuffer = page.Buffer;
        int pageWidth = page.Width;
        int bufferHeight = page.BufferHeight;
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (leftMargin, rightMargin) = GetHorizontalMargins(pageWidth);
        bool viewportMoved = false;

        var cursor = page.Cursor;
        var currentPosition = cursor.Position;
        var newPosition = currentPosition;

        // The row records whether it ended by wrapping, which reflow and copy need.
        textBuffer.GetMutableRowByOffset(currentPosition.Y).SetWrapForced(wrapForced);

        if (withReturn)
        {
            // The return goes to the left margin when the cursor is inside the margins.
            bool clampToMargin = currentPosition.Y >= topMargin && currentPosition.Y <= bottomMargin && currentPosition.X >= leftMargin;
            newPosition = newPosition with { X = clampToMargin ? leftMargin : 0 };
        }

        if (currentPosition.Y != bottomMargin || newPosition.X < leftMargin || newPosition.X > rightMargin)
        {
            // Not at the bottom margin: the cursor just moves down, stopping at the page bottom.
            newPosition = newPosition with { Y = Math.Min(currentPosition.Y + 1, page.Bottom - 1) };
            newPosition = textBuffer.ClampPositionWithinLine(newPosition);
        }
        else if (topMargin > page.Top || leftMargin > 0 || rightMargin < pageWidth - 1)
        {
            // Margins that do not cover the page scroll only the margin area.
            ScrollRectVertically(page, new TilRect(leftMargin, topMargin, rightMargin + 1, bottomMargin + 1), -1);
        }
        else if (page.Bottom < bufferHeight)
        {
            // With buffer below the page, the viewport moves down instead, keeping the top line in the scrollback.
            _api.SetViewportPosition(new TilPoint(page.XPanOffset, page.Top + 1));
            newPosition = newPosition with { Y = newPosition.Y + 1 };
            viewportMoved = true;

            // Rows below a bottom margin are moved down to stay on screen; otherwise the new row is cleared.
            if (bottomMargin < page.Bottom - 1)
            {
                ScrollRectVertically(page, new TilRect(0, bottomMargin + 1, pageWidth, page.Bottom + 1), 1);
            }
            else
            {
                var eraseAttributes = GetEraseAttributes(page);
                textBuffer.GetMutableRowByOffset(newPosition.Y).Reset(eraseAttributes);
            }
        }
        else
        {
            // At the end of the buffer, the circular buffer rotates and the oldest line is lost.
            var eraseAttributes = GetEraseAttributes(page);
            textBuffer.IncrementCircularBuffer(eraseAttributes);
            _api.NotifyBufferRotation(1);

            textBuffer.TriggerScroll(new TilPoint(0, -1));

            if (bottomMargin < page.Bottom - 1)
            {
                ScrollRectVertically(page, new TilRect(0, bottomMargin, pageWidth, bufferHeight), 1);
            }
        }

        cursor.SetPosition(newPosition);
        return viewportMoved;
    }

    public override void LineFeed(LineFeedType lineFeedType)
    {
        var page = _pages.ActivePage();
        switch (lineFeedType)
        {
            case LineFeedType.DependsOnMode:
                DoLineFeed(page, _api.GetSystemMode(ITerminalApi.Mode.LineFeed), false);
                break;
            case LineFeedType.WithoutReturn:
                DoLineFeed(page, false, false);
                break;
            case LineFeedType.WithReturn:
                DoLineFeed(page, true, false);
                break;
        }
    }

    public override void ReverseLineFeed()
    {
        var page = _pages.ActivePage();
        var textBuffer = page.Buffer;
        var cursor = page.Cursor;
        var cursorPosition = cursor.Position;
        var (leftMargin, rightMargin) = GetHorizontalMargins(page.Width);
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);

        // At the top margin the margin area scrolls down; otherwise the cursor moves up.
        if (cursorPosition.Y == topMargin && cursorPosition.X >= leftMargin && cursorPosition.X <= rightMargin)
        {
            ScrollRectVertically(page, new TilRect(leftMargin, topMargin, rightMargin + 1, bottomMargin + 1), 1);
        }
        else if (cursorPosition.Y > page.Top)
        {
            cursor.SetPosition(textBuffer.ClampPositionWithinLine(new TilPoint(cursorPosition.X, cursorPosition.Y - 1)));
        }
    }

    public override void BackIndex()
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        var cursorPosition = cursor.Position;
        var (leftMargin, rightMargin) = GetHorizontalMargins(page.Width);
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);

        if (cursorPosition.X == leftMargin && cursorPosition.Y >= topMargin && cursorPosition.Y <= bottomMargin)
        {
            ScrollRectHorizontally(page, new TilRect(leftMargin, topMargin, rightMargin + 1, bottomMargin + 1), 1);
        }
        else if (cursorPosition.X > 0)
        {
            cursor.SetXPosition(cursorPosition.X - 1);
        }
    }

    public override void ForwardIndex()
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        var cursorPosition = cursor.Position;
        var (leftMargin, rightMargin) = GetHorizontalMargins(page.Width);
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);

        if (cursorPosition.X == rightMargin && cursorPosition.Y >= topMargin && cursorPosition.Y <= bottomMargin)
        {
            ScrollRectHorizontally(page, new TilRect(leftMargin, topMargin, rightMargin + 1, bottomMargin + 1), -1);
        }
        else if (cursorPosition.X < page.Buffer.GetLineWidth(cursorPosition.Y) - 1)
        {
            cursor.SetXPosition(cursorPosition.X + 1);
        }
    }

    public override void SetWindowTitle(ReadOnlySpan<char> title) => _api.SetWindowTitle(title);

    public override void SetCurrentWorkingDirectory(ReadOnlySpan<char> uri)
    {
        // OSC 7 sends a file URL; anything else is ignored.
        if (!Uri.TryCreate(uri.ToString(), UriKind.Absolute, out var parsed) || !parsed.IsFile)
        {
            return;
        }

        string path = parsed.LocalPath;
        if (IsLegalPath(path))
        {
            _api.SetWorkingDirectory(path);
        }
    }

    /// <summary>Whether a path has none of the characters that can never be in a path.</summary>
    private static bool IsLegalPath(ReadOnlySpan<char> path) => path.IndexOfAny("\"*<>?|") < 0;

    public override void HorizontalTabSet()
    {
        var page = _pages.ActivePage();
        int column = page.Cursor.Position.X;

        InitTabStopsForWidth(page.Width);
        _tabStopColumns[column] = true;
    }

    public override void ForwardTab(int numTabs)
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        int column = cursor.Position.X;
        int row = cursor.Position.Y;
        int width = page.Buffer.GetLineWidth(row);
        int tabsPerformed = 0;

        // Tabs stop at the right margin when the cursor is inside the margins.
        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (_, rightMargin) = GetHorizontalMargins(width);
        bool clampToMargin = row >= topMargin && row <= bottomMargin && column <= rightMargin;
        int maxColumn = clampToMargin ? rightMargin : width - 1;

        InitTabStopsForWidth(width);
        while (column < maxColumn && tabsPerformed < numTabs)
        {
            column++;
            if (_tabStopColumns[column])
            {
                tabsPerformed++;
            }
        }

        // A tab keeps a pending delayed wrap.
        bool delayedWrapOriginallySet = cursor.DelayEolWrap.HasValue;
        cursor.SetXPosition(column);
        if (delayedWrapOriginallySet)
        {
            cursor.DelayEolWrapAtCurrentPosition();
        }
    }

    public override void BackwardsTab(int numTabs)
    {
        var page = _pages.ActivePage();
        var cursor = page.Cursor;
        int column = cursor.Position.X;
        int row = cursor.Position.Y;
        int width = page.Buffer.GetLineWidth(row);
        int tabsPerformed = 0;

        var (topMargin, bottomMargin) = GetVerticalMargins(page, true);
        var (leftMargin, _) = GetHorizontalMargins(width);
        bool clampToMargin = row >= topMargin && row <= bottomMargin && column >= leftMargin;
        int minColumn = clampToMargin ? leftMargin : 0;

        InitTabStopsForWidth(width);
        while (column > minColumn && tabsPerformed < numTabs)
        {
            column--;
            if (_tabStopColumns[column])
            {
                tabsPerformed++;
            }
        }

        cursor.SetXPosition(column);
    }

    public override void TabClear(TabClearType clearType)
    {
        switch (clearType)
        {
            case TabClearType.ClearCurrentColumn:
                ClearSingleTabStop();
                break;
            case TabClearType.ClearAllColumns:
                ClearAllTabStops();
                break;
        }
    }

    private void ClearSingleTabStop()
    {
        var page = _pages.ActivePage();
        int column = page.Cursor.Position.X;

        InitTabStopsForWidth(page.Width);
        _tabStopColumns[column] = false;
    }

    private void ClearAllTabStops()
    {
        _tabStopColumns.Clear();
        _initDefaultTabStops = false;
    }

    public override void TabSet(VTParameter setType)
    {
        if (setType.ValueOr((int)TabSetType.SetEvery8Columns) == (int)TabSetType.SetEvery8Columns)
        {
            _tabStopColumns.Clear();
            _initDefaultTabStops = true;
        }
    }

    /// <summary>Grows the tab stop list to <paramref name="width"/>, with default stops every 8 columns unless they were cleared.</summary>
    private void InitTabStopsForWidth(int width)
    {
        int initialWidth = _tabStopColumns.Count;
        if (width > initialWidth)
        {
            while (_tabStopColumns.Count < width)
            {
                _tabStopColumns.Add(false);
            }

            if (_initDefaultTabStops)
            {
                for (int column = 8; column < _tabStopColumns.Count; column += 8)
                {
                    if (column >= initialWidth)
                    {
                        _tabStopColumns[column] = true;
                    }
                }
            }
        }
    }

    public override void DesignateCodingSystem(VTID codingSystem)
    {
        // Output is always UTF-16 here, so only the C1 and GR handling of each coding system applies.
        if (codingSystem == DispatchTypes.CODING_SYSTEM_ISO2022)
        {
            AcceptC1Controls(true);
            _termOutput.EnableGrTranslation(true);
        }
        else if (codingSystem == DispatchTypes.CODING_SYSTEM_UTF8)
        {
            AcceptC1Controls(false);
            _termOutput.EnableGrTranslation(false);
        }
    }

    public override void Designate94Charset(int gsetNumber, VTID charset) => _termOutput.Designate94Charset(gsetNumber, charset);

    public override void Designate96Charset(int gsetNumber, VTID charset) => _termOutput.Designate96Charset(gsetNumber, charset);

    public override void LockingShift(int gsetNumber) => _termOutput.LockingShift(gsetNumber);

    public override void LockingShiftRight(int gsetNumber) => _termOutput.LockingShiftRight(gsetNumber);

    public override void SingleShift(int gsetNumber) => _termOutput.SingleShift(gsetNumber);

    public override void AcceptC1Controls(bool enabled) => _api.GetStateMachine().SetParserMode(StateMachine.Mode.AcceptC1, enabled);

    public override void SendC1Controls(bool enabled) => _terminalInput.SetInputMode(TerminalInput.Mode.SendC1, enabled);

    public override void AnnounceCodeStructure(int ansiLevel)
    {
        // Levels 1 and 2 put Latin-1 in GR; all three put ASCII in GL.
        if (ansiLevel == 1 || ansiLevel == 2)
        {
            Designate96Charset(1, VTID.FromString("A"));
            LockingShiftRight(1);
        }

        if (ansiLevel >= 1 && ansiLevel <= 3)
        {
            Designate94Charset(0, VTID.FromString("B"));
            LockingShift(0);
        }
    }

    public override void SoftReset()
    {
        _pages.ActivePage().Cursor.SetIsVisible(true);
        SetModeFlag(Mode.InsertReplace | Mode.Origin | Mode.AllowDECSLRM, false);
        _api.SetSystemMode(ITerminalApi.Mode.AutoWrap, true);
        _terminalInput.SetInputMode(TerminalInput.Mode.CursorKey, false);
        _terminalInput.SetInputMode(TerminalInput.Mode.Keypad, false);

        DoSetTopBottomScrollingMargins(0, 0);
        DoSetLeftRightScrollingMargins(0, 0);

        _termOutput.SoftReset();
        SetGraphicsRendition(default);
        SetCharacterProtectionAttribute(default);

        // The saved cursor state is reset too, but keeps the character sets just reset.
        _savedCursorState[_usingAltBuffer ? 1 : 0] = new CursorState { TermOutput = _termOutput.Clone() };
    }

    public override void HardReset(bool erase)
    {
        if (_usingAltBuffer)
        {
            _api.UseMainScreenBuffer();
            _usingAltBuffer = false;
        }

        _pages.Reset();
        _termOutput = new TerminalOutput();
        AcceptC1Controls(false);
        SendC1Controls(false);

        SoftReset();

        if (erase)
        {
            EraseInDisplay(EraseType.All);
            EraseInDisplay(EraseType.Scrollback);
        }

        _renderSettings.RestoreDefaultSettings();
        if (_renderer != null)
        {
            _renderer.TriggerRedrawAll(true, true);
            _renderer.NotifySynchronizedOutputChanged(_renderSettings.GetRenderMode(RenderSettings.Mode.SynchronizedOutput));
        }

        if (erase)
        {
            CursorPosition(1, 1);
        }

        // LNM is reset in both the system and the input modes.
        if (_terminalInput.GetInputMode(TerminalInput.Mode.LineFeed))
        {
            _api.SetSystemMode(ITerminalApi.Mode.LineFeed, false);
        }

        _terminalInput.ResetInputModes();
        _api.SetSystemMode(ITerminalApi.Mode.BracketedPaste, false);

        _pages.ActivePage().Cursor.SetIsBlinking(true);

        TabSet(new VTParameter((int)TabSetType.SetEvery8Columns));

        _modes = Mode.PageCursorCoupling;
    }

    public override void ScreenAlignmentPattern()
    {
        var page = _pages.ActivePage();

        FillRect(page, new TilRect(0, page.Top, page.Width, page.Bottom), "E", default);
        page.Buffer.ResetLineRenditionRange(page.Top, page.Bottom);

        // DECALN also resets the rendition, origin mode, and margins, and homes the cursor.
        var attr = page.Attributes;
        attr.SetStandardErase();
        page.SetAttributes(attr);
        SetModeFlag(Mode.Origin | Mode.AllowDECSLRM, false);
        DoSetTopBottomScrollingMargins(0, 0);
        DoSetLeftRightScrollingMargins(0, 0);
        CursorPosition(1, 1);
    }

    private void EraseScrollback()
    {
        var page = _pages.VisiblePage();
        var cursor = page.Cursor;
        int row = cursor.Position.Y;

        // The visible page moves to the top of the buffer and everything above it is dropped.
        page.Buffer.ClearScrollback(page.Top, page.Height);
        _api.SetViewportPosition(new TilPoint(page.XPanOffset, 0));
        cursor.SetYPosition(row - page.Top);
    }

    private void EraseAll()
    {
        var page = _pages.ActivePage();
        int pageWidth = page.Width;
        int pageHeight = page.Height;
        int bufferHeight = page.BufferHeight;
        var textBuffer = page.Buffer;
        var cursor = page.Cursor;
        int row = cursor.Position.Y - page.Top;

        // The page moves down below the last text, so what was on screen goes into the scrollback.
        var lastChar = textBuffer.GetLastNonSpaceCharacter();
        int newPageTop = lastChar == default ? 0 : lastChar.Y + 1;
        int newPageBottom = newPageTop + pageHeight;

        // Past the end of the buffer, the circular buffer rotates to make room.
        int delta = newPageBottom - bufferHeight;
        if (delta > 0)
        {
            for (int rotation = 0; rotation < delta; rotation++)
            {
                textBuffer.IncrementCircularBuffer();
            }

            _api.NotifyBufferRotation(delta);
            newPageTop -= delta;
            newPageBottom -= delta;
            textBuffer.TriggerScroll(new TilPoint(0, -delta));
        }

        if (newPageTop != page.Top)
        {
            _api.SetViewportPosition(new TilPoint(page.XPanOffset, newPageTop));
        }

        cursor.SetYPosition(row + newPageTop);

        var eraseAttributes = GetEraseAttributes(page);
        FillRect(page, new TilRect(0, newPageTop, pageWidth, newPageBottom), WHITESPACE, eraseAttributes);

        textBuffer.ResetLineRenditionRange(newPageTop, newPageBottom);
    }

    public override void SetCursorStyle(CursorStyle cursorStyle)
    {
        CursorType actualType;
        bool enableBlinking;

        switch (cursorStyle)
        {
            case CursorStyle.UserDefault:
                enableBlinking = true;
                actualType = _api.GetUserDefaultCursorStyle();
                break;
            case CursorStyle.BlinkingBlock:
                enableBlinking = true;
                actualType = CursorType.FullBox;
                break;
            case CursorStyle.SteadyBlock:
                enableBlinking = false;
                actualType = CursorType.FullBox;
                break;
            case CursorStyle.BlinkingUnderline:
                enableBlinking = true;
                actualType = CursorType.Underscore;
                break;
            case CursorStyle.SteadyUnderline:
                enableBlinking = false;
                actualType = CursorType.Underscore;
                break;
            case CursorStyle.BlinkingBar:
                enableBlinking = true;
                actualType = CursorType.VerticalBar;
                break;
            case CursorStyle.SteadyBar:
                enableBlinking = false;
                actualType = CursorType.VerticalBar;
                break;
            default:
                return;
        }

        var cursor = _pages.ActivePage().Cursor;
        cursor.SetType(actualType);
        cursor.SetIsBlinking(enableBlinking);
    }

    public override void SetOptionalFeatures(OptionalFeature features) => _optionalFeatures = features;
}
