using Aprillz.MewUI.Core;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Panels;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A multi-line text input control with thin scrollbars.
/// </summary>
public sealed class MultiLineTextBox : TextBase
{
    private double _verticalOffset;
    private double _horizontalOffset;
    private double _lineHeight;
    private readonly List<int> _lineStarts = new() { 0 };
    private bool _suppressTextInputNewline;
    private bool _suppressTextInputTab;
    private bool _wrap;

    private int _pendingViewAnchorIndex = -1;
    private double _pendingViewAnchorYOffset;
    private double _pendingViewAnchorXOffset;

    private readonly Dictionary<int, CachedLine> _lineTextCache = new();
    private readonly Dictionary<int, WrapLayout> _wrapCache = new();
    private readonly List<WrapAnchor> _wrapAnchors = new();

    private readonly List<Edit> _undo = new();
    private readonly List<Edit> _redo = new();

    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;

    protected override Color DefaultBackground => Theme.Current.ControlBackground;
    protected override Color DefaultBorderBrush => Theme.Current.ControlBorder;

    public MultiLineTextBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(4);

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _hBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _vBar.Parent = this;
        _hBar.Parent = this;

        _vBar.ValueChanged = v => { _verticalOffset = v; InvalidateVisual(); };
        _hBar.ValueChanged = v => { _horizontalOffset = v; InvalidateVisual(); };
    }

    /// <summary>
    /// Enables hard-wrapping at the available width. When enabled, horizontal scrolling is disabled.
    /// </summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap == value)
                return;

            CaptureViewAnchor();

            _wrap = value;
            _wrapCache.Clear();
            _wrapAnchors.Clear();
            _horizontalOffset = 0;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        int firstCr = text.IndexOf('\r');
        if (firstCr < 0)
            return text;

        return NormalizeNewlines(text);
    }

    protected override void OnTextChanged(string oldText, string newText)
    {
        base.OnTextChanged(oldText, newText);
        InvalidateMeasure();
    }

    protected override void SetTextCore(string normalizedText)
    {
        _lineTextCache.Clear();
        _wrapCache.Clear();
        _wrapAnchors.Clear();
        _undo.Clear();
        _redo.Clear();
        base.SetTextCore(normalizedText);
        RebuildLineStartsFromDocument();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        _lineHeight = Math.Max(16, FontSize * 1.4);
        if (!string.IsNullOrEmpty(Text))
        {
            using var measure = BeginTextMeasurement();
            _lineHeight = Math.Max(_lineHeight, measure.Context.MeasureText("Mg", measure.Font).Height);
        }

        double minHeight = _lineHeight * 3 + Padding.VerticalThickness + 4;
        return new Size(240, minHeight);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        var theme = GetTheme();
        var snapped = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var innerBounds = snapped.Deflate(new Thickness(borderInset));

        double wrapWidth = Math.Max(1, innerBounds.Width - Padding.HorizontalThickness);
        double extentH = GetExtentHeight(wrapWidth);
        double extentW = !_wrap ? GetExtentWidth() : 0;

        double viewportH = Math.Max(0, innerBounds.Height - Padding.VerticalThickness);
        double viewportW = Math.Max(0, innerBounds.Width - Padding.HorizontalThickness);

        _verticalOffset = ClampOffset(_verticalOffset, extentH, viewportH);
        _horizontalOffset = !_wrap
            ? ClampOffset(_horizontalOffset, extentW, viewportW)
            : 0;

        bool needV = extentH > viewportH + 0.5;

        // Wrap: a vertical scrollbar reduces wrap width, which may increase the total height.
        if (_wrap && needV)
        {
            double reducedWrapWidth = Math.Max(1, wrapWidth - theme.ScrollBarHitThickness - 1);
            extentH = GetExtentHeight(reducedWrapWidth);
            needV = extentH > viewportH + 0.5;
        }

        bool needH = !_wrap && extentW > viewportW + 0.5;

        _vBar.IsVisible = needV;
        _hBar.IsVisible = needH;

        const double inset = 0;
        double t = theme.ScrollBarHitThickness;

        if (_vBar.IsVisible)
        {
            _vBar.Minimum = 0;
            _vBar.Maximum = Math.Max(0, extentH - viewportH);
            _vBar.ViewportSize = viewportH;
            _vBar.SmallChange = theme.ScrollBarSmallChange;
            _vBar.LargeChange = theme.ScrollBarLargeChange;
            _vBar.Value = _verticalOffset;
            _vBar.Arrange(new Rect(innerBounds.Right - t - inset, innerBounds.Y + inset, t, Math.Max(0, innerBounds.Height - inset * 2)));
        }

        if (_hBar.IsVisible)
        {
            _hBar.Minimum = 0;
            _hBar.Maximum = Math.Max(0, extentW - viewportW);
            _hBar.ViewportSize = viewportW;
            _hBar.SmallChange = theme.ScrollBarSmallChange;
            _hBar.LargeChange = theme.ScrollBarLargeChange;
            _hBar.Value = _horizontalOffset;
            _hBar.Arrange(new Rect(innerBounds.X + inset, innerBounds.Bottom - t - inset, Math.Max(0, innerBounds.Width - inset * 2), t));
        }
        else
        {
            _horizontalOffset = 0;
        }

        ApplyViewAnchorIfPending();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var viewportBounds = innerBounds;
        if (_vBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, theme.ScrollBarHitThickness + 1, 0));
        if (_hBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, 0, theme.ScrollBarHitThickness + 1));
        var contentBounds = viewportBounds.Deflate(Padding);
        double radius = theme.ControlCornerRadius;

        var borderColor = BorderBrush;
        if (IsEnabled)
        {
            if (IsFocused)
                borderColor = theme.Accent;
            else if (IsMouseOver)
                borderColor = BorderBrush.Lerp(theme.Accent, 0.6);
        }

        DrawBackgroundAndBorder(
            context,
            bounds,
            IsEnabled ? Background : theme.TextBoxDisabledBackground,
            borderColor,
            radius);

        context.Save();
        context.SetClip(contentBounds);

        var font = GetFont();

        if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Placeholder) && !IsFocused)
        {
            context.DrawText(Placeholder, contentBounds, font, theme.PlaceholderText,
                TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
        }
        else
        {
            RenderText(context, contentBounds, font, theme);
        }

        context.Restore();

        if (_vBar.IsVisible) _vBar.Render(context);
        if (_hBar.IsVisible) _hBar.Render(context);
    }

    public override UIElement? HitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEnabled)
            return null;

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
            return _vBar;
        if (_hBar.IsVisible && _hBar.Bounds.Contains(point))
            return _hBar;

        return base.HitTest(point);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
            return;

        Focus();

        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
        var viewportBounds = innerBounds;
        if (_vBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, theme.ScrollBarHitThickness + 1, 0));
        if (_hBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, 0, theme.ScrollBarHitThickness + 1));
        var contentBounds = viewportBounds.Deflate(Padding);
        SetCaretFromPoint(e.Position, contentBounds);
        _selectionStart = CaretPosition;
        _selectionLength = 0;

        var root = FindVisualRoot();
        if (root is Window window)
            window.CaptureMouse(this);

        EnsureCaretVisible(contentBounds);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEnabled || !IsMouseCaptured || !e.LeftButton)
            return;

        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
        var viewportBounds = innerBounds;
        if (_vBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, theme.ScrollBarHitThickness + 1, 0));
        if (_hBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, 0, theme.ScrollBarHitThickness + 1));
        var contentBounds = viewportBounds.Deflate(Padding);

        const double edgeDip = 10;
        if (e.Position.Y < contentBounds.Y + edgeDip)
            _verticalOffset += e.Position.Y - (contentBounds.Y + edgeDip);
        else if (e.Position.Y > contentBounds.Bottom - edgeDip)
            _verticalOffset += e.Position.Y - (contentBounds.Bottom - edgeDip);

        if (e.Position.X < contentBounds.X + edgeDip)
            _horizontalOffset += e.Position.X - (contentBounds.X + edgeDip);
        else if (e.Position.X > contentBounds.Right - edgeDip)
            _horizontalOffset += e.Position.X - (contentBounds.Right - edgeDip);

        ClampOffsets(contentBounds);

        SetCaretFromPoint(e.Position, contentBounds);
        _selectionLength = CaretPosition - _selectionStart;

        EnsureCaretVisible(contentBounds);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left)
            return;

        var root = FindVisualRoot();
        if (root is Window window)
            window.ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || !_vBar.IsVisible)
            return;

        int notches = Math.Sign(e.Delta);
        if (notches == 0)
            return;

        double viewportH = GetViewportHeight();
        double viewportW = GetViewportWidth();
        _verticalOffset = ClampOffset(_verticalOffset - notches * GetTheme().ScrollWheelStep, GetExtentHeight(viewportW), viewportH);
        _vBar.Value = _verticalOffset;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        bool ctrl = e.ControlKey;
        bool shift = e.ShiftKey;

        if (!IsReadOnly && ctrl && !shift && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
            return;
        }

        if (!IsReadOnly && ctrl && (e.Key == Key.Y || (shift && e.Key == Key.Z)))
        {
            Redo();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Tab:
                if (!IsReadOnly && AcceptTab)
                {
                    InsertText("\t");
                    _suppressTextInputTab = true;
                    e.Handled = true;
                }
                break;

            case Key.Left:
                MoveCaretHorizontal(-1, shift);
                e.Handled = true;
                break;
            case Key.Right:
                MoveCaretHorizontal(1, shift);
                e.Handled = true;
                break;
            case Key.Up:
                MoveCaretVertical(-1, shift);
                e.Handled = true;
                break;
            case Key.Down:
                MoveCaretVertical(1, shift);
                e.Handled = true;
                break;
            case Key.Home:
                MoveToLineEdge(start: true, shift);
                e.Handled = true;
                break;
            case Key.End:
                MoveToLineEdge(start: false, shift);
                e.Handled = true;
                break;
            case Key.Backspace:
                if (!IsReadOnly) Backspace();
                e.Handled = true;
                break;
            case Key.Delete:
                if (!IsReadOnly) Delete();
                e.Handled = true;
                break;
            case Key.Enter:
                if (!IsReadOnly)
                {
                    InsertText("\n");
                    _suppressTextInputNewline = true;
                    e.Handled = true;
                }
                break;
        }

        if (e.Handled)
        {
            var theme = GetTheme();
            var bounds = GetSnappedBorderBounds(Bounds);
            var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
            var viewportBounds = innerBounds;
            if (_vBar.IsVisible)
                viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, theme.ScrollBarHitThickness + 1, 0));
            if (_hBar.IsVisible)
                viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, 0, theme.ScrollBarHitThickness + 1));
            var contentBounds = viewportBounds.Deflate(Padding);
            EnsureCaretVisible(contentBounds);
            InvalidateVisual();
        }
    }

    protected override void CutToClipboardCore() => CutToClipboard();

    protected override void PasteFromClipboardCore() => PasteFromClipboard();

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsReadOnly || e.Handled)
            return;

        var text = e.Text ?? string.Empty;

        if (_suppressTextInputNewline)
        {
            _suppressTextInputNewline = false;
            if (text.Contains('\r') || text.Contains('\n'))
            {
                e.Handled = true;
                return;
            }
        }

        if (_suppressTextInputTab)
        {
            _suppressTextInputTab = false;
            if (text.Contains('\t'))
            {
                e.Handled = true;
                return;
            }
        }

        if (!AcceptTab && text.Contains('\t'))
            text = text.Replace("\t", string.Empty);

        text = NormalizeText(text);
        if (text.Length == 0)
            return;

        InsertText(text);
        e.Handled = true;
    }

    private void RenderText(IGraphicsContext context, Rect contentBounds, IFont font, Theme theme)
    {
        double lineHeight = GetLineHeight();
        int lineCount = Math.Max(1, _lineStarts.Count);
        var textColor = IsEnabled ? Foreground : theme.DisabledText;

        if (!_wrap)
        {
            int firstLine = lineHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(_verticalOffset / lineHeight));
            double offsetInLine = lineHeight <= 0 ? 0 : _verticalOffset - firstLine * lineHeight;
            double y = contentBounds.Y - offsetInLine;

            int maxLines = lineHeight <= 0 ? lineCount : (int)Math.Ceiling((contentBounds.Height + offsetInLine) / lineHeight) + 1;
            int lastExclusive = Math.Min(lineCount, firstLine + Math.Max(0, maxLines));

            for (int line = firstLine; line < lastExclusive; line++)
            {
                GetLineSpan(line, out int start, out int end);
                string lineText = GetLineText(line, start, end);

                var lineRect = new Rect(contentBounds.X - _horizontalOffset, y, 1_000_000, lineHeight);
                RenderSelectionForRow(context, font, theme, line, start, 0, lineText, y, contentBounds.X - _horizontalOffset);
                context.DrawText(lineText, lineRect, font, textColor, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
                y += lineHeight;
            }

            if (IsFocused)
                DrawCaret(context, contentBounds, font, theme);

            return;
        }

        using var measure = BeginTextMeasurement();
        double wrapWidth = Math.Max(1, contentBounds.Width);

        int firstRow = lineHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(_verticalOffset / lineHeight));
        double offsetInRow = lineHeight <= 0 ? 0 : _verticalOffset - firstRow * lineHeight;
        double yRow = contentBounds.Y - offsetInRow;

        MapVisualRowToLine(firstRow, wrapWidth, measure.Context, measure.Font, out int lineIndex, out int rowInLine);

        double yWrap = yRow;
        int maxRows = lineHeight <= 0 ? 1 : (int)Math.Ceiling((contentBounds.Height + offsetInRow) / lineHeight) + 1;
        int rendered = 0;

        while (rendered < maxRows && lineIndex < lineCount)
        {
            GetLineSpan(lineIndex, out int lineStart, out int lineEnd);
            string fullLine = GetLineText(lineIndex, lineStart, lineEnd);
            var layout = GetWrapLayout(lineIndex, fullLine, wrapWidth, measure.Context, measure.Font);

            for (int row = rowInLine; row < layout.SegmentStarts.Length && rendered < maxRows; row++)
            {
                int segStart = layout.SegmentStarts[row];
                int segEnd = (row + 1 < layout.SegmentStarts.Length) ? layout.SegmentStarts[row + 1] : fullLine.Length;
                string rowText = segStart < segEnd ? fullLine.Substring(segStart, segEnd - segStart) : string.Empty;

                var rowRect = new Rect(contentBounds.X, yWrap, wrapWidth, lineHeight);
                RenderSelectionForRow(context, font, theme, lineIndex, lineStart, segStart, rowText, yWrap, contentBounds.X);
                context.DrawText(rowText, rowRect, font, textColor, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
                DrawCaretForWrappedRow(context, contentBounds, font, theme, lineStart, segStart, segEnd, rowText, yWrap);

                yWrap += lineHeight;
                rendered++;
            }

            lineIndex++;
            rowInLine = 0;
        }
    }

    private void DrawCaret(IGraphicsContext context, Rect contentBounds, IFont font, Theme theme)
    {
        if (!IsEnabled)
            return;

        GetLineFromIndex(CaretPosition, out int line, out int lineStart, out int lineEnd);
        double lineHeight = GetLineHeight();
        double y = contentBounds.Y + line * lineHeight - _verticalOffset;

        double x = contentBounds.X - _horizontalOffset + MeasureSubstringWidth(context, font, lineStart, CaretPosition);
        var caretRect = new Rect(x, y, 1, lineHeight);

        if (caretRect.Bottom < contentBounds.Y || caretRect.Y > contentBounds.Bottom)
            return;

        context.FillRectangle(caretRect, theme.WindowText);
    }

    private void SetCaretFromPoint(Point p, Rect contentBounds)
    {
        double lineHeight = GetLineHeight();
        if (!_wrap)
        {
            int line = lineHeight <= 0 ? 0 : (int)Math.Floor((p.Y - contentBounds.Y + _verticalOffset) / lineHeight);
            line = Math.Clamp(line, 0, _lineStarts.Count - 1);

            GetLineSpan(line, out int start, out int end);
            double x = p.X - contentBounds.X + _horizontalOffset;

            CaretPosition = GetCharIndexFromXInLine(x, start, end);
            return;
        }

        using var measure = BeginTextMeasurement();
        double wrapWidth = Math.Max(1, contentBounds.Width);
        int row = lineHeight <= 0 ? 0 : (int)Math.Floor((p.Y - contentBounds.Y + _verticalOffset) / lineHeight);
        MapVisualRowToLine(row, wrapWidth, measure.Context, measure.Font, out int lineIndex, out int rowInLine);

        GetLineSpan(lineIndex, out int lineStart, out int lineEnd);
        string fullLine = GetLineText(lineIndex, lineStart, lineEnd);
        var layout = GetWrapLayout(lineIndex, fullLine, wrapWidth, measure.Context, measure.Font);
        rowInLine = Math.Clamp(rowInLine, 0, layout.SegmentStarts.Length - 1);
        int segStart = layout.SegmentStarts[rowInLine];
        int segEnd = (rowInLine + 1 < layout.SegmentStarts.Length) ? layout.SegmentStarts[rowInLine + 1] : fullLine.Length;
        string rowText = segStart < segEnd ? fullLine.Substring(segStart, segEnd - segStart) : string.Empty;

        double xInRow = p.X - contentBounds.X;
        int col = GetCharIndexFromXInString(xInRow, rowText, measure.Context, measure.Font);
        CaretPosition = lineStart + segStart + col;
    }

    private int GetCharIndexFromXInLine(double x, int start, int end)
    {
        if (start >= end)
            return start;

        using var measure = BeginTextMeasurement();
        var font = measure.Font;

        if (x <= 0)
            return start;

        string line = GetTextSubstringCore(start, end - start);
        double total = measure.Context.MeasureText(line, font).Width;
        if (x >= total)
            return end;

        int lo = 0;
        int hi = line.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            double w = measure.Context.MeasureText(line.Substring(0, mid), font).Width;
            if (w < x) lo = mid + 1;
            else hi = mid;
        }
        return start + lo;
    }

    private void MoveCaretHorizontal(int delta, bool extendSelection)
    {
        int newPos = Math.Clamp(CaretPosition + delta, 0, GetTextLengthCore());
        SetCaretAndSelection(newPos, extendSelection);
    }

    private void MoveCaretVertical(int deltaLines, bool extendSelection)
    {
        GetLineFromIndex(CaretPosition, out int line, out int lineStart, out int lineEnd);
        int newLine = Math.Clamp(line + deltaLines, 0, _lineStarts.Count - 1);
        if (newLine == line)
            return;

        using var measure = BeginTextMeasurement();
        double x = CaretPosition <= lineStart ? 0 : measure.Context.MeasureText(GetTextSubstringCore(lineStart, CaretPosition - lineStart), measure.Font).Width;

        GetLineSpan(newLine, out int ns, out int ne);
        int newPos = GetCharIndexFromXInLine(x, ns, ne);
        SetCaretAndSelection(newPos, extendSelection);
    }

    private void MoveToLineEdge(bool start, bool extendSelection)
    {
        GetLineFromIndex(CaretPosition, out _, out int lineStart, out int lineEnd);
        int newPos = start ? lineStart : lineEnd;
        SetCaretAndSelection(newPos, extendSelection);
    }

    private void SetCaretAndSelection(int newPos, bool extendSelection)
    {
        if (!extendSelection)
        {
            CaretPosition = newPos;
            _selectionStart = newPos;
            _selectionLength = 0;
            return;
        }

        if (_selectionLength == 0)
            _selectionStart = CaretPosition;

        CaretPosition = newPos;
        _selectionLength = CaretPosition - _selectionStart;
    }

    private void Backspace()
    {
        if (DeleteSelectionIfAny())
            return;

        if (CaretPosition <= 0)
            return;

        ApplyRemove(CaretPosition - 1, 1, recordUndo: true);
        CaretPosition--;
    }

    private void Delete()
    {
        if (DeleteSelectionIfAny())
            return;

        if (CaretPosition >= GetTextLengthCore())
            return;

        ApplyRemove(CaretPosition, 1, recordUndo: true);
    }

    private bool DeleteSelectionIfAny()
    {
        if (_selectionLength == 0)
            return false;

        int a = Math.Min(_selectionStart, _selectionStart + _selectionLength);
        int b = Math.Max(_selectionStart, _selectionStart + _selectionLength);

        ApplyRemove(a, b - a, recordUndo: true);
        CaretPosition = a;
        _selectionStart = a;
        _selectionLength = 0;
        return true;
    }

    private void InsertText(string text)
    {
        DeleteSelectionIfAny();

        var normalized = NormalizeText(text);
        if (normalized.Length == 0)
            return;

        ApplyInsert(CaretPosition, normalized, recordUndo: true);
        CaretPosition += normalized.Length;
        _selectionStart = CaretPosition;
        _selectionLength = 0;
    }

    private void CopyToClipboard()
    {
        if (_selectionLength == 0)
            return;

        int a = Math.Min(_selectionStart, _selectionStart + _selectionLength);
        int b = Math.Max(_selectionStart, _selectionStart + _selectionLength);

        string s = GetTextSubstringCore(a, b - a);
        TryClipboardSetText(s);
    }

    private void CutToClipboard()
    {
        if (_selectionLength == 0)
            return;

        CopyToClipboard();
        DeleteSelectionIfAny();
    }

    private void PasteFromClipboard()
    {
        if (!TryClipboardGetText(out var s) || string.IsNullOrEmpty(s))
            return;

        s = NormalizeText(s);
        if (s.Length == 0)
            return;

        InsertText(s);
    }

    private void EnsureCaretVisible(Rect contentBounds)
    {
        using var measure = BeginTextMeasurement();
        var font = measure.Font;

        GetLineFromIndex(CaretPosition, out int line, out int lineStart, out _);
        double lineHeight = GetLineHeight();

        double caretY;
        double caretX;

        if (!_wrap)
        {
            caretY = line * lineHeight;
            caretX = CaretPosition <= lineStart ? 0 : measure.Context.MeasureText(GetTextSubstringCore(lineStart, CaretPosition - lineStart), font).Width;
        }
        else
        {
            double wrapWidth = Math.Max(1, contentBounds.Width);
            GetLineSpan(line, out _, out int lineEnd);
            string fullLine = GetLineText(line, lineStart, lineEnd);
            var layout = GetWrapLayout(line, fullLine, wrapWidth, measure.Context, font);
            int caretCol = Math.Clamp(CaretPosition - lineStart, 0, fullLine.Length);
            int caretRow = GetWrapRowFromColumn(layout, caretCol);
            caretY = (GetVisualRowStartForLine(line, wrapWidth, measure.Context, font) + caretRow) * lineHeight;

            int segStart = layout.SegmentStarts[caretRow];
            int rel = Math.Clamp(caretCol - segStart, 0, fullLine.Length - segStart);
            string before = rel <= 0 ? string.Empty : fullLine.Substring(segStart, rel);
            caretX = string.IsNullOrEmpty(before) ? 0 : measure.Context.MeasureText(before, font).Width;
        }

        double viewportH = Math.Max(1, contentBounds.Height);
        double viewportW = Math.Max(1, contentBounds.Width);
        double extentH = GetExtentHeight(viewportW);
        double extentW = !_wrap ? GetExtentWidth() : 0;

        if (caretY < _verticalOffset)
            _verticalOffset = caretY;
        else if (caretY + lineHeight > _verticalOffset + viewportH)
            _verticalOffset = caretY + lineHeight - viewportH;

        if (!_wrap)
        {
            if (caretX < _horizontalOffset)
                _horizontalOffset = caretX;
            else if (caretX > _horizontalOffset + viewportW)
                _horizontalOffset = caretX - viewportW;
        }

        _verticalOffset = ClampOffset(_verticalOffset, extentH, viewportH);
        _horizontalOffset = !_wrap ? ClampOffset(_horizontalOffset, extentW, viewportW) : 0;

        if (_vBar.IsVisible) _vBar.Value = _verticalOffset;
        if (_hBar.IsVisible) _hBar.Value = _horizontalOffset;
    }

    private void ClampOffsets(Rect contentBounds)
    {
        double wrapWidth = Math.Max(1, contentBounds.Width);
        _verticalOffset = ClampOffset(_verticalOffset, GetExtentHeight(wrapWidth), Math.Max(1, contentBounds.Height));
        _horizontalOffset = !_wrap
            ? ClampOffset(_horizontalOffset, GetExtentWidth(), Math.Max(1, contentBounds.Width))
            : 0;
        if (_vBar.IsVisible) _vBar.Value = _verticalOffset;
        if (_hBar.IsVisible) _hBar.Value = _horizontalOffset;
    }

    private double GetExtentHeight(double wrapWidth)
    {
        if (!_wrap)
            return Math.Max(0, _lineStarts.Count * GetLineHeight());

        int totalRows = GetEstimatedTotalVisualRows(wrapWidth);
        return Math.Max(0, totalRows * GetLineHeight());
    }

    private double GetExtentWidth()
    {
        if (string.IsNullOrEmpty(Text))
            return 0;

        using var measure = BeginTextMeasurement();
        double max = 0;
        for (int i = 0; i < _lineStarts.Count; i++)
        {
            GetLineSpan(i, out int s, out int e);
            if (e <= s) continue;
            max = Math.Max(max, measure.Context.MeasureText(GetLineText(i, s, e), measure.Font).Width);
        }
        return max;
    }

    private double GetViewportHeight()
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
        var viewportBounds = innerBounds;
        if (_vBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, theme.ScrollBarHitThickness + 1, 0));
        if (_hBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, 0, theme.ScrollBarHitThickness + 1));
        return Math.Max(1, viewportBounds.Height - Padding.VerticalThickness);
    }

    private double GetViewportWidth()
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
        var viewportBounds = innerBounds;
        if (_vBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, theme.ScrollBarHitThickness + 1, 0));
        if (_hBar.IsVisible)
            viewportBounds = viewportBounds.Deflate(new Thickness(0, 0, 0, theme.ScrollBarHitThickness + 1));
        return Math.Max(1, viewportBounds.Width - Padding.HorizontalThickness);
    }

    private double GetLineHeight() => _lineHeight > 0 ? _lineHeight : Math.Max(16, FontSize * 1.4);

    private void RebuildLineStartsFromDocument()
    {
        _lineStarts.Clear();
        _lineStarts.Add(0);

        for (int i = 0; i < Document.Length; i++)
        {
            if (Document[i] == '\n')
                _lineStarts.Add(i + 1);
        }

        if (_lineStarts.Count == 0)
            _lineStarts.Add(0);
    }

    private void GetLineSpan(int line, out int start, out int end)
    {
        if (_lineStarts.Count == 0)
            RebuildLineStartsFromDocument();

        line = Math.Clamp(line, 0, _lineStarts.Count - 1);
        start = _lineStarts[line];
        end = line + 1 < _lineStarts.Count ? _lineStarts[line + 1] - 1 : Document.Length;
        if (end < start) end = start;

        if (end > start && Document[end - 1] == '\r')
            end--;
    }

    private void GetLineFromIndex(int index, out int line, out int lineStart, out int lineEnd)
    {
        if (_lineStarts.Count == 0)
            RebuildLineStartsFromDocument();

        index = Math.Clamp(index, 0, Document.Length);

        int lo = 0;
        int hi = _lineStarts.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            int s = _lineStarts[mid];
            if (s <= index)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        line = Math.Clamp(lo - 1, 0, _lineStarts.Count - 1);
        GetLineSpan(line, out lineStart, out lineEnd);
    }

    private static double ClampOffset(double value, double extent, double viewport)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;
        return Math.Clamp(value, 0, Math.Max(0, extent - viewport));
    }

    private double MeasureSubstringWidth(IGraphicsContext context, IFont font, int start, int end)
    {
        if (end <= start)
            return 0;

        return context.MeasureText(GetTextSubstringCore(start, end - start), font).Width;
    }

    private void RenderSelectionForRow(
        IGraphicsContext context,
        IFont font,
        Theme theme,
        int lineIndex,
        int lineStart,
        int rowSegmentStart,
        string rowText,
        double y,
        double xBase)
    {
        if (!IsFocused || _selectionLength == 0 || string.IsNullOrEmpty(rowText))
            return;

        int selA = Math.Min(_selectionStart, _selectionStart + _selectionLength);
        int selB = Math.Max(_selectionStart, _selectionStart + _selectionLength);

        int rowStart = lineStart + rowSegmentStart;
        int rowEnd = rowStart + rowText.Length;
        int s = Math.Max(selA, rowStart);
        int t = Math.Min(selB, rowEnd);
        if (s >= t)
            return;

        int relS = s - rowStart;
        int relT = t - rowStart;

        string before = relS <= 0 ? string.Empty : rowText.Substring(0, relS);
        string selected = rowText.Substring(relS, relT - relS);
        double beforeW = string.IsNullOrEmpty(before) ? 0 : context.MeasureText(before, font).Width;
        double selW = string.IsNullOrEmpty(selected) ? 0 : context.MeasureText(selected, font).Width;

        context.FillRectangle(new Rect(xBase + beforeW, y, selW, GetLineHeight()), theme.SelectionBackground);
    }

    private void DrawCaretForWrappedRow(
        IGraphicsContext context,
        Rect contentBounds,
        IFont font,
        Theme theme,
        int lineStart,
        int segStart,
        int segEnd,
        string rowText,
        double y)
    {
        if (!IsFocused || !IsEnabled)
            return;

        int caret = CaretPosition;
        int rowStart = lineStart + segStart;
        int rowEnd = lineStart + segEnd;
        if (caret < rowStart || caret > rowEnd)
            return;

        int rel = Math.Clamp(caret - rowStart, 0, rowText.Length);
        string before = rel <= 0 ? string.Empty : rowText.Substring(0, rel);
        double x = contentBounds.X + (string.IsNullOrEmpty(before) ? 0 : context.MeasureText(before, font).Width);
        context.FillRectangle(new Rect(x, y, 1, GetLineHeight()), theme.WindowText);
    }

    private static int GetCharIndexFromXInString(double x, string text, IGraphicsContext context, IFont font)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (x <= 0)
            return 0;

        double total = context.MeasureText(text, font).Width;
        if (x >= total)
            return text.Length;

        int lo = 0;
        int hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            double w = mid <= 0 ? 0 : context.MeasureText(text.Substring(0, mid), font).Width;
            if (w < x) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private string GetLineText(int lineIndex, int start, int end)
    {
        if (end <= start)
            return string.Empty;

        if (_lineTextCache.Count > 256)
            _lineTextCache.Clear();

        if (_lineTextCache.TryGetValue(lineIndex, out var cached) &&
            cached.Version == DocumentVersion &&
            cached.Start == start &&
            cached.End == end)
        {
            return cached.Text;
        }

        var text = GetTextSubstringCore(start, end - start);
        _lineTextCache[lineIndex] = new CachedLine(DocumentVersion, start, end, text);
        return text;
    }

    private WrapLayout GetWrapLayout(int lineIndex, string lineText, double wrapWidth, IGraphicsContext context, IFont font)
    {
        if (_wrapCache.TryGetValue(lineIndex, out var layout) &&
            layout.Version == DocumentVersion &&
            Math.Abs(layout.Width - wrapWidth) < 0.01)
        {
            return layout;
        }

        var segmentStarts = BuildWrapSegments(lineText, wrapWidth, context, font);
        layout = new WrapLayout(DocumentVersion, wrapWidth, segmentStarts);
        if (_wrapCache.Count > 512)
            _wrapCache.Clear();
        _wrapCache[lineIndex] = layout;
        return layout;
    }

    private static int[] BuildWrapSegments(string text, double wrapWidth, IGraphicsContext context, IFont font)
    {
        if (string.IsNullOrEmpty(text))
            return new[] { 0 };

        var segments = new List<int>(8) { 0 };
        int start = 0;
        while (start < text.Length)
        {
            int end = FindWrapEnd(text, start, wrapWidth, context, font);
            if (end <= start)
                end = start + 1;

            start = end;

            if (start < text.Length)
                segments.Add(start);
        }

        return segments.ToArray();
    }

    private static int FindWrapEnd(string text, int start, double wrapWidth, IGraphicsContext context, IFont font)
    {
        int min = start + 1;
        int max = text.Length;
        int best = min;

        if (context.MeasureText(text.Substring(start, max - start), font).Width <= wrapWidth)
            return max;

        while (min <= max)
        {
            int mid = (min + max) / 2;
            double w = context.MeasureText(text.Substring(start, mid - start), font).Width;
            if (w <= wrapWidth)
            {
                best = mid;
                min = mid + 1;
            }
            else
            {
                max = mid - 1;
            }
        }

        return best;
    }

    private void MapVisualRowToLine(int visualRow, double wrapWidth, IGraphicsContext context, IFont font, out int lineIndex, out int rowInLine)
    {
        visualRow = Math.Max(0, visualRow);
        int lineCount = Math.Max(1, _lineStarts.Count);

        int anchorLine = 0;
        int anchorRow = 0;
        for (int i = _wrapAnchors.Count - 1; i >= 0; i--)
        {
            if (_wrapAnchors[i].StartRow <= visualRow)
            {
                anchorLine = _wrapAnchors[i].LineIndex;
                anchorRow = _wrapAnchors[i].StartRow;
                break;
            }
        }

        int row = anchorRow;
        int line = anchorLine;
        while (line < lineCount)
        {
            GetLineSpan(line, out int s, out int e);
            string text = GetLineText(line, s, e);
            int rows = GetWrapLayout(line, text, wrapWidth, context, font).SegmentStarts.Length;
            if (visualRow < row + rows)
            {
                lineIndex = line;
                rowInLine = visualRow - row;
                return;
            }

            row += rows;
            line++;

            if (line % 256 == 0)
                _wrapAnchors.Add(new WrapAnchor(line, row));
        }

        lineIndex = Math.Max(0, lineCount - 1);
        rowInLine = 0;
    }

    private int GetEstimatedTotalVisualRows(double wrapWidth)
    {
        int lineCount = Math.Max(1, _lineStarts.Count);
        if (!_wrap)
            return lineCount;

        int extra = 0;
        foreach (var kv in _wrapCache)
        {
            if (kv.Value.Version != DocumentVersion)
                continue;
            if (Math.Abs(kv.Value.Width - wrapWidth) >= 0.01)
                continue;
            extra += Math.Max(0, kv.Value.SegmentStarts.Length - 1);
        }

        return lineCount + extra;
    }

    private int GetVisualRowStartForLine(int lineIndex, double wrapWidth, IGraphicsContext context, IFont font)
    {
        if (lineIndex <= 0)
            return 0;

        int anchorLine = 0;
        int anchorRow = 0;
        for (int i = _wrapAnchors.Count - 1; i >= 0; i--)
        {
            if (_wrapAnchors[i].LineIndex <= lineIndex)
            {
                anchorLine = _wrapAnchors[i].LineIndex;
                anchorRow = _wrapAnchors[i].StartRow;
                break;
            }
        }

        int row = anchorRow;
        for (int line = anchorLine; line < lineIndex; line++)
        {
            GetLineSpan(line, out int s, out int e);
            string text = GetLineText(line, s, e);
            row += GetWrapLayout(line, text, wrapWidth, context, font).SegmentStarts.Length;
            if ((line + 1) % 256 == 0)
                _wrapAnchors.Add(new WrapAnchor(line + 1, row));
        }

        return row;
    }

    private static int GetWrapRowFromColumn(WrapLayout layout, int col)
    {
        for (int i = layout.SegmentStarts.Length - 1; i >= 0; i--)
        {
            if (layout.SegmentStarts[i] <= col)
                return i;
        }
        return 0;
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        var edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        ApplyEdit(edit.Inverse(), recordUndo: false);
        _redo.Add(edit);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;

        var edit = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        ApplyEdit(edit, recordUndo: false);
        _undo.Add(edit);
    }

    private void ApplyInsert(int index, string text, bool recordUndo)
        => ApplyEdit(new Edit(EditKind.Insert, index, text), recordUndo);

    private void ApplyRemove(int index, int length, bool recordUndo)
    {
        if (length <= 0)
            return;

        int max = GetTextLengthCore();
        index = Math.Clamp(index, 0, max);
        length = Math.Min(length, max - index);
        if (length <= 0)
            return;

        string deleted = GetTextSubstringCore(index, length);
        ApplyEdit(new Edit(EditKind.Delete, index, deleted), recordUndo);
    }

    private void ApplyEdit(Edit edit, bool recordUndo)
    {
        BumpDocumentVersion();
        _lineTextCache.Clear();
        _wrapCache.Clear();
        _wrapAnchors.Clear();

        if (edit.Kind == EditKind.Insert)
        {
            Document.Insert(edit.Index, edit.Text.AsSpan());
            UpdateLineStartsOnInsert(edit.Index, edit.Text);
        }
        else
        {
            Document.Remove(edit.Index, edit.Text.Length);
            UpdateLineStartsOnRemove(edit.Index, edit.Text);
        }

        CaretPosition = Math.Clamp(CaretPosition, 0, GetTextLengthCore());
        if (recordUndo)
        {
            _undo.Add(edit);
            _redo.Clear();
        }

        InvalidateMeasure();
        InvalidateVisual();
        if (TextChanged != null)
            TextChanged(GetTextCore());
    }

    private void UpdateLineStartsOnInsert(int index, string insertedText)
    {
        int len = insertedText.Length;
        for (int i = 0; i < _lineStarts.Count; i++)
        {
            if (_lineStarts[i] > index)
                _lineStarts[i] += len;
        }

        int insertPos = LowerBoundLineStart(index + 1);
        for (int i = 0; i < insertedText.Length; i++)
        {
            if (insertedText[i] != '\n')
                continue;
            _lineStarts.Insert(insertPos, index + i + 1);
            insertPos++;
        }
    }

    private void UpdateLineStartsOnRemove(int index, string deletedText)
    {
        int len = deletedText.Length;
        int end = index + len;

        for (int i = _lineStarts.Count - 1; i >= 0; i--)
        {
            int s = _lineStarts[i];
            if (s > index && s <= end)
                _lineStarts.RemoveAt(i);
        }

        for (int i = 0; i < _lineStarts.Count; i++)
        {
            if (_lineStarts[i] > end)
                _lineStarts[i] -= len;
        }

        if (_lineStarts.Count == 0)
            _lineStarts.Add(0);
    }

    private int LowerBoundLineStart(int value)
    {
        int lo = 0;
        int hi = _lineStarts.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_lineStarts[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static string NormalizeNewlines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        int len = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                len++;
                continue;
            }
            len++;
        }

        if (len == text.Length)
            return text;

        return string.Create(len, text, static (span, s) =>
        {
            int j = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\n')
                        i++;
                    span[j++] = '\n';
                    continue;
                }

                span[j++] = c;
            }
        });
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _vBar.Dispose();
        _hBar.Dispose();
    }

    private readonly record struct CachedLine(int Version, int Start, int End, string Text);
    private readonly record struct WrapLayout(int Version, double Width, int[] SegmentStarts);
    private readonly record struct WrapAnchor(int LineIndex, int StartRow);

    private enum EditKind { Insert, Delete }

    private readonly record struct Edit(EditKind Kind, int Index, string Text)
    {
        public Edit Inverse() => Kind == EditKind.Insert
            ? new Edit(EditKind.Delete, Index, Text)
            : new Edit(EditKind.Insert, Index, Text);
    }

    private void CaptureViewAnchor()
    {
        _pendingViewAnchorIndex = -1;
        _pendingViewAnchorYOffset = 0;
        _pendingViewAnchorXOffset = 0;

        double lineHeight = GetLineHeight();
        if (lineHeight <= 0)
            return;

        _pendingViewAnchorYOffset = _verticalOffset - Math.Floor(_verticalOffset / lineHeight) * lineHeight;

        using var measure = BeginTextMeasurement();
        double viewportW = GetViewportWidth();
        double wrapWidth = Math.Max(1, viewportW);

        if (_wrap)
        {
            int firstRow = Math.Max(0, (int)Math.Floor(_verticalOffset / lineHeight));
            MapVisualRowToLine(firstRow, wrapWidth, measure.Context, measure.Font, out int lineIndex, out int rowInLine);

            GetLineSpan(lineIndex, out int lineStart, out int lineEnd);
            string fullLine = GetLineText(lineIndex, lineStart, lineEnd);
            var layout = GetWrapLayout(lineIndex, fullLine, wrapWidth, measure.Context, measure.Font);
            rowInLine = Math.Clamp(rowInLine, 0, Math.Max(0, layout.SegmentStarts.Length - 1));

            int segStart = layout.SegmentStarts.Length == 0 ? 0 : layout.SegmentStarts[rowInLine];
            _pendingViewAnchorIndex = Math.Clamp(lineStart + segStart, 0, GetTextLengthCore());
            _pendingViewAnchorXOffset = 0;
            return;
        }

        int firstLine = Math.Max(0, (int)Math.Floor(_verticalOffset / lineHeight));
        firstLine = Math.Clamp(firstLine, 0, Math.Max(0, _lineStarts.Count - 1));

        GetLineSpan(firstLine, out int start, out int end);
        string lineText = GetLineText(firstLine, start, end);

        int col = GetCharIndexFromXInString(_horizontalOffset, lineText, measure.Context, measure.Font);
        col = Math.Clamp(col, 0, lineText.Length);

        double colWidth = col <= 0 ? 0 : measure.Context.MeasureText(lineText.Substring(0, col), measure.Font).Width;
        _pendingViewAnchorXOffset = _horizontalOffset - colWidth;
        _pendingViewAnchorIndex = Math.Clamp(start + col, 0, GetTextLengthCore());
    }

    private void ApplyViewAnchorIfPending()
    {
        if (_pendingViewAnchorIndex < 0)
            return;

        double lineHeight = GetLineHeight();
        if (lineHeight <= 0)
        {
            _pendingViewAnchorIndex = -1;
            return;
        }

        using var measure = BeginTextMeasurement();

        double viewportW = GetViewportWidth();
        double viewportH = GetViewportHeight();
        double wrapWidth = Math.Max(1, viewportW);

        GetLineFromIndex(_pendingViewAnchorIndex, out int line, out int lineStart, out int lineEnd);
        int col = Math.Clamp(_pendingViewAnchorIndex - lineStart, 0, Math.Max(0, lineEnd - lineStart));

        if (_wrap)
        {
            string fullLine = GetLineText(line, lineStart, lineEnd);
            var layout = GetWrapLayout(line, fullLine, wrapWidth, measure.Context, measure.Font);
            int rowInLine = GetWrapRowFromColumn(layout, col);
            int rowStart = GetVisualRowStartForLine(line, wrapWidth, measure.Context, measure.Font);
            _verticalOffset = (rowStart + rowInLine) * lineHeight + _pendingViewAnchorYOffset;
            _horizontalOffset = 0;
        }
        else
        {
            _verticalOffset = line * lineHeight + _pendingViewAnchorYOffset;

            string lineText = GetLineText(line, lineStart, lineEnd);
            col = Math.Clamp(col, 0, lineText.Length);
            double colWidth = col <= 0 ? 0 : measure.Context.MeasureText(lineText.Substring(0, col), measure.Font).Width;
            _horizontalOffset = Math.Max(0, colWidth + _pendingViewAnchorXOffset);
        }

        _pendingViewAnchorIndex = -1;
        _pendingViewAnchorYOffset = 0;
        _pendingViewAnchorXOffset = 0;

        double extentH = GetExtentHeight(wrapWidth);
        _verticalOffset = ClampOffset(_verticalOffset, extentH, viewportH);

        double extentW = !_wrap ? GetExtentWidth() : 0;
        _horizontalOffset = !_wrap ? ClampOffset(_horizontalOffset, extentW, viewportW) : 0;

        if (_vBar.IsVisible)
            _vBar.Value = _verticalOffset;
        if (_hBar.IsVisible)
            _hBar.Value = _horizontalOffset;
    }
}
