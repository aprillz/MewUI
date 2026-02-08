using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A multi-line text input control with thin scrollbars.
/// </summary>
public sealed class MultiLineTextBox : TextBase
    , IVisualTreeHost
{
    private const int WrapSegmentHardLimit = 4096;
    private const int WrapLineCountHardLimit = 4096;
    private const int ExtentWidthLineCountHardLimit = 4096;

    private double _lineHeight;
    private readonly List<int> _lineStarts = new() { 0 };

    private int _pendingViewAnchorIndex = -1;
    private double _pendingViewAnchorYOffset;
    private double _pendingViewAnchorXOffset;

    private readonly MultiLineTextView _textView;
    private readonly TextWrapVirtualizer _wrapVirtualizer;
    private readonly TextLineWidthEstimator _lineWidthEstimator;

    // Undo/Redo handled by TextBase.

    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    public MultiLineTextBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(4);
        AcceptReturn = true;

        _textView = new MultiLineTextView(
            () => DocumentVersion,
            () => _lineStarts.Count,
            GetTextSubstringCore,
            () => FontFamily,
            () => FontSize,
            () => FontWeight,
            GetDpi);

        _wrapVirtualizer = new TextWrapVirtualizer(
            GetLineSpan,
            _textView.GetLineText,
            () => DocumentVersion,
            () => _lineStarts.Count,
            GetTextLengthCore,
            WrapSegmentHardLimit);

        _lineWidthEstimator = new TextLineWidthEstimator(
            GetLineSpan,
            Document.CopyTo,
            () => _lineStarts.Count,
            GetTextLengthCore);

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _hBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _vBar.Parent = this;
        _hBar.Parent = this;

        _vBar.ValueChanged += v => SetVerticalOffset(v);
        _hBar.ValueChanged += v => SetHorizontalOffset(v);
    }

    protected override Rect GetInteractionContentBounds()
        => GetViewportContentBounds();

    protected override Rect AdjustViewportBoundsForScrollbars(Rect innerBounds, Theme theme)
    {
        // Overlay scrollbars: viewport does not reserve space for bars.
        return innerBounds;
    }

    protected override void SetCaretFromPoint(Point point, Rect contentBounds) => SetCaretFromPointCore(point, contentBounds);

    protected override void AutoScrollForSelectionDrag(Point point, Rect contentBounds)
    {
        const double edgeDip = 10;
        if (point.Y < contentBounds.Y + edgeDip)
        {
            SetVerticalOffset(VerticalOffset + point.Y - (contentBounds.Y + edgeDip), false);
        }
        else if (point.Y > contentBounds.Bottom - edgeDip)
        {
            SetVerticalOffset(VerticalOffset + point.Y - (contentBounds.Bottom - edgeDip), false);
        }

        if (point.X < contentBounds.X + edgeDip)
        {
            SetHorizontalOffset(HorizontalOffset + point.X - (contentBounds.X + edgeDip), false);
        }
        else if (point.X > contentBounds.Right - edgeDip)
        {
            SetHorizontalOffset(HorizontalOffset + point.X - (contentBounds.Right - edgeDip), false);
        }

        ClampOffsets(contentBounds);
    }

    protected override void EnsureCaretVisibleCore(Rect contentBounds) => EnsureCaretVisible(contentBounds);

    /// <summary>
    /// Enables hard-wrapping at the available width. When enabled, horizontal scrolling is disabled.
    /// </summary>
    public bool Wrap
    {
        get => WrapEnabled;
        set
        {
            if (WrapEnabled == value)
            {
                return;
            }

            if (value && _lineStarts.Count > WrapLineCountHardLimit)
            {
                // Cannot re-enable for very large documents.
                NotifyWrapChanged(false);
                return;
            }

            SetWrapEnabled(value);
        }
    }

    protected override bool SupportsWrap => true;

    protected override TextAlignment PlaceholderVerticalAlignment => TextAlignment.Top;

    protected override void OnWrapChanged(bool oldValue, bool newValue)
    {
        CaptureViewAnchor();

        _wrapVirtualizer.Reset();
        _lineWidthEstimator.Reset();
        SetHorizontalOffset(0);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private bool CanComputeExtentWidth() => !WrapEnabled && _lineStarts.Count <= ExtentWidthLineCountHardLimit;

    protected override void OnTextChanged(string oldText, string newText)
    {
        base.OnTextChanged(oldText, newText);
        InvalidateMeasure();
    }

    protected override void SetTextCore(string normalizedText)
    {
        _textView.Reset();
        _wrapVirtualizer.Reset();
        _lineWidthEstimator.Reset();
        base.SetTextCore(normalizedText);
        RebuildLineStartsFromDocument();
        EnforceWrapLineLimit();
    }

    protected override void ApplyInsertForEdit(int index, string text) => ApplyInsert(index, text);

    protected override void ApplyRemoveForEdit(int index, int length) => ApplyRemove(index, length);

    protected override void OnEditCommitted()
    {
        EnforceWrapLineLimit();
        InvalidateMeasure();
        InvalidateVisual();
        NotifyTextChanged();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();

        using var measure = BeginTextMeasurement();
        // Include descenders so line height is sufficient for characters like q/y/g.
        var metrics = measure.Context.MeasureText("Mgqy", measure.Font);
        _lineHeight = Math.Max(Math.Max(16, FontSize * 1.4), metrics.Height);

        // Keep previous default width as the baseline; optionally expand a bit for small content.
        double baseWidth = 16;
        double textWidth = baseWidth;

        //// Keep desired width stable (scrollbars handle overflow).
        //// Only allow placeholder to influence the default width.
        //if (Document.Length == 0 && !string.IsNullOrEmpty(Placeholder))
        //{
        //    var placeholder = Placeholder.AsSpan();
        //    if (placeholder.Length > 64)
        //    {
        //        placeholder = placeholder[..64];
        //    }
        //    textWidth = Math.Max(baseWidth, measure.Context.MeasureText(placeholder, measure.Font).Width);
        //}

        // Avoid pathological desired sizes when the document contains very long lines.
        textWidth = Math.Min(textWidth, 2048);

        double chromeW = Padding.HorizontalThickness + borderInset * 2;
        double chromeH = Padding.VerticalThickness + borderInset * 2;

        double desiredW = textWidth + chromeW + 4;
        double desiredH = _lineHeight * 3 + chromeH + 4;

        return new Size(desiredW, desiredH);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        var snapped = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var innerBounds = snapped.Deflate(new Thickness(borderInset));
        var dpiScale = GetDpi() / 96.0;

        const double inset = 0;
        double t = Theme.Metrics.ScrollBarHitThickness;

        using var measure = BeginTextMeasurement();

        // Overlay scrollbars: viewport does not shrink when bars appear/disappear.
        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        var finalViewportContent = LayoutRounding.SnapViewportRectToPixels(innerBounds.Deflate(Padding), dpiScale);
        double finalViewportH = Math.Max(0, finalViewportContent.Height);
        double finalViewportW = Math.Max(0, finalViewportContent.Width);

        double finalExtentH = GetExtentHeight(Math.Max(1, finalViewportW));
        double finalExtentW = WrapEnabled ? 0 : GetExtentWidthForViewport(measure.Context, measure.Font, finalViewportH);

        bool needV = finalExtentH > finalViewportH + 0.5;
        bool needH = !WrapEnabled && finalExtentW > finalViewportW + 0.5;

        _vBar.IsVisible = needV;
        _hBar.IsVisible = needH;

        SetVerticalOffset(ClampOffset(VerticalOffset, finalExtentH, finalViewportH, dpiScale), false);
        SetHorizontalOffset(needH ? ClampOffset(HorizontalOffset, finalExtentW, finalViewportW, dpiScale) : 0, false);

        if (needV)
        {
            _vBar.Minimum = 0;
            _vBar.Maximum = Math.Max(0, finalExtentH - finalViewportH);
            _vBar.ViewportSize = finalViewportH;
            _vBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _vBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _vBar.Value = VerticalOffset;
            _vBar.Arrange(new Rect(
                innerBounds.Right - t - inset,
                innerBounds.Y + inset,
                t,
                Math.Max(0, innerBounds.Height - (needH ? t : 0) - inset * 2)));
        }
        else
        {
            // Ensure stale bounds from a previous visible pass do not affect nested layout/hit-testing.
            _vBar.Value = 0;
            _vBar.Arrange(Rect.Empty);
        }

        if (needH)
        {
            _hBar.Minimum = 0;
            _hBar.Maximum = Math.Max(0, finalExtentW - finalViewportW);
            _hBar.ViewportSize = finalViewportW;
            _hBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _hBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _hBar.Value = HorizontalOffset;
            _hBar.Arrange(new Rect(
                innerBounds.X + inset,
                innerBounds.Bottom - t - inset,
                Math.Max(0, innerBounds.Width - (needV ? t : 0) - inset * 2),
                t));
        }
        else
        {
            _hBar.Value = 0;
            _hBar.Arrange(Rect.Empty);
        }

        ApplyViewAnchorIfPending();
    }

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        visitor(_vBar);
        visitor(_hBar);
    }

    protected override void RenderTextContent(IGraphicsContext context, Rect contentBounds, IFont font, Theme theme, in VisualState state)
    {
        RenderText(context, contentBounds, font, theme);
    }

    protected override void RenderAfterContent(IGraphicsContext context, Theme theme, in VisualState state)
    {
        if (_vBar.IsVisible)
        {
            _vBar.Render(context);
        }

        if (_hBar.IsVisible)
        {
            _hBar.Render(context);
        }
    }

    protected override UIElement? HitTestOverride(Point point)
    {
        if (!IsEnabled)
        {
            return null;
        }

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            return _vBar;
        }

        if (_hBar.IsVisible && _hBar.Bounds.Contains(point))
        {
            return _hBar;
        }

        return null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || !_vBar.IsVisible)
        {
            return;
        }

        int notches = Math.Sign(e.Delta);
        if (notches == 0)
        {
            return;
        }

        var viewportBounds = GetViewportContentBounds();
        double viewportH = viewportBounds.Height;
        double viewportW = viewportBounds.Width;
        var dpiScale = GetDpi() / 96.0;
        SetVerticalOffset(ClampOffset(VerticalOffset - notches * Theme.Metrics.ScrollWheelStep, GetExtentHeight(viewportW), viewportH, dpiScale), false);
        _vBar.Value = VerticalOffset;
        InvalidateVisual();
        e.Handled = true;
    }

    // Key handling is centralized in TextBase.

    protected override void MoveCaretToLineEdge(bool start, bool extendSelection)
        => MoveToLineEdge(start, extendSelection);

    protected override void MoveCaretVerticalKey(int deltaLines, bool extendSelection)
        => MoveCaretVertical(deltaLines, extendSelection);

    private void RenderText(IGraphicsContext context, Rect contentBounds, IFont font, Theme theme)
    {
        double lineHeight = GetLineHeight();
        int lineCount = Math.Max(1, _lineStarts.Count);
        var textColor = IsEnabled ? Foreground : theme.Palette.DisabledText;

        if (!WrapEnabled)
        {
            int caretLine = -1;
            int caretLineStart = 0;
            if (IsFocused && IsEnabled)
            {
                GetLineFromIndex(CaretPosition, out caretLine, out caretLineStart, out _);
            }

            int firstLine = lineHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(VerticalOffset / lineHeight));
            double offsetInLine = lineHeight <= 0 ? 0 : VerticalOffset - firstLine * lineHeight;
            double y = contentBounds.Y - offsetInLine;

            int maxLines = lineHeight <= 0 ? lineCount : (int)Math.Ceiling((contentBounds.Height + offsetInLine) / lineHeight) + 1;
            int lastExclusive = Math.Min(lineCount, firstLine + Math.Max(0, maxLines));

            for (int line = firstLine; line < lastExclusive; line++)
            {
                GetLineSpan(line, out int start, out int end);
                var cache = _textView.EnsureLineMeasureCache(line, start, end, context, font);
                ReadOnlySpan<char> lineSpan = cache.Text.AsSpan();

                double xFrom = Math.Max(0, HorizontalOffset);
                double xTo = xFrom + Math.Max(0, contentBounds.Width);

                int startCol = MultiLineTextView.GetCharIndexFromXCached(cache, xFrom, context, font);
                int endCol = MultiLineTextView.GetCharIndexFromXCached(cache, xTo, context, font);
                if (endCol < startCol)
                {
                    endCol = startCol;
                }
                endCol = Math.Min(lineSpan.Length, endCol + 2);

                double prefixW = startCol <= 0 ? 0 : MultiLineTextView.GetPrefixWidthCached(cache, startCol, context, font);
                double drawX = contentBounds.X - HorizontalOffset + prefixW;
                ReadOnlySpan<char> visible = lineSpan[startCol..endCol];

                var lineRect = new Rect(drawX, y, 1_000_000, lineHeight);
                RenderSelectionForRow(context, font, theme, start, startCol, visible, y, drawX);
                context.DrawText(visible, lineRect, font, textColor, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);

                if (line == caretLine)
                {
                    int caret = Math.Clamp(CaretPosition - caretLineStart, 0, lineSpan.Length);
                    if (caret >= startCol && caret <= endCol)
                    {
                        double caretX = contentBounds.X - HorizontalOffset + MultiLineTextView.GetPrefixWidthCached(cache, caret, context, font);
                        DrawCaret(context, caretX, y, lineHeight, theme);
                    }
                }

                y += lineHeight;
            }

            return;
        }

        using var measure = BeginTextMeasurement();
        double wrapWidth = Math.Max(1, contentBounds.Width);

        int firstRow = lineHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(VerticalOffset / lineHeight));
        double offsetInRow = lineHeight <= 0 ? 0 : VerticalOffset - firstRow * lineHeight;
        double yRow = contentBounds.Y - offsetInRow;

        _wrapVirtualizer.MapVisualRowToLine(firstRow, wrapWidth, measure.Context, measure.Font, out int lineIndex, out int rowInLine);

        double yWrap = yRow;
        int maxRows = lineHeight <= 0 ? 1 : (int)Math.Ceiling((contentBounds.Height + offsetInRow) / lineHeight) + 1;
        int rendered = 0;

        (int start, int end) selection = default;
        bool canDrawCaret = IsFocused && IsEnabled;
        bool canDrawSelection = HasSelection;
        if (canDrawSelection)
        {
            selection = GetSelectionRange();
        }

        while (rendered < maxRows && lineIndex < lineCount)
        {
            GetLineSpan(lineIndex, out int lineStart, out int lineEnd);
            string fullLine = _textView.GetLineText(lineIndex, lineStart, lineEnd);
            var layout = _wrapVirtualizer.GetWrapLayout(lineIndex, fullLine, wrapWidth, measure.Context, measure.Font);

            MultiLineTextView.CachedLineMeasure? lineMeasure = null;
            if ((canDrawCaret && CaretPosition >= lineStart && CaretPosition <= lineEnd) ||
                (canDrawSelection && selection.start < lineEnd && selection.end > lineStart))
            {
                lineMeasure = _textView.EnsureLineMeasureCache(lineIndex, lineStart, lineEnd, measure.Context, measure.Font);
            }

            for (int row = rowInLine; row < layout.SegmentStarts.Length && rendered < maxRows; row++)
            {
                int segStart = layout.SegmentStarts[row];
                int segEnd = (row + 1 < layout.SegmentStarts.Length) ? layout.SegmentStarts[row + 1] : fullLine.Length;
                ReadOnlySpan<char> rowText = segStart < segEnd ? fullLine.AsSpan(segStart, segEnd - segStart) : ReadOnlySpan<char>.Empty;

                var rowRect = new Rect(contentBounds.X, yWrap, wrapWidth, lineHeight);
                RenderSelectionForRow(context, font, theme, lineStart, segStart, rowText, yWrap, contentBounds.X, lineMeasure);
                context.DrawText(rowText, rowRect, font, textColor, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
                if (lineMeasure != null)
                {
                    DrawCaretForWrappedRow(context, contentBounds, font, theme, lineStart, segStart, segEnd, rowText, yWrap, lineMeasure);
                }

                yWrap += lineHeight;
                rendered++;
            }

            lineIndex++;
            rowInLine = 0;
        }
    }

    private void SetCaretFromPointCore(Point p, Rect contentBounds)
    {
        double lineHeight = GetLineHeight();
        if (!WrapEnabled)
        {
            int line = lineHeight <= 0 ? 0 : (int)Math.Floor((p.Y - contentBounds.Y + VerticalOffset) / lineHeight);
            line = Math.Clamp(line, 0, _lineStarts.Count - 1);

            GetLineSpan(line, out int start, out int end);
            double x = p.X - contentBounds.X + HorizontalOffset;

            using var m = BeginTextMeasurement();
            var cache = _textView.EnsureLineMeasureCache(line, start, end, m.Context, m.Font);
            CaretPosition = start + MultiLineTextView.GetCharIndexFromXCached(cache, x, m.Context, m.Font);
            return;
        }

        using var measure = BeginTextMeasurement();
        double wrapWidth = Math.Max(1, contentBounds.Width);
        int row = lineHeight <= 0 ? 0 : (int)Math.Floor((p.Y - contentBounds.Y + VerticalOffset) / lineHeight);
        _wrapVirtualizer.MapVisualRowToLine(row, wrapWidth, measure.Context, measure.Font, out int lineIndex, out int rowInLine, out int lineStartRow);

        GetLineSpan(lineIndex, out int lineStart, out int lineEnd);
        var lineMeasure = _textView.EnsureLineMeasureCache(lineIndex, lineStart, lineEnd, measure.Context, measure.Font);
        string fullLine = lineMeasure.Text;
        var layout = _wrapVirtualizer.GetWrapLayout(lineIndex, fullLine, wrapWidth, measure.Context, measure.Font);
        rowInLine = Math.Clamp(rowInLine, 0, layout.SegmentStarts.Length - 1);
        int segStart = layout.SegmentStarts[rowInLine];
        int segEnd = (rowInLine + 1 < layout.SegmentStarts.Length) ? layout.SegmentStarts[rowInLine + 1] : fullLine.Length;

        double xInRow = p.X - contentBounds.X;
        double baseX = MultiLineTextView.GetPrefixWidthCached(lineMeasure, segStart, measure.Context, measure.Font);
        int colInLine = MultiLineTextView.GetCharIndexFromXCached(lineMeasure, baseX + xInRow, measure.Context, measure.Font);
        colInLine = Math.Clamp(colInLine, segStart, segEnd);
        CaretPosition = lineStart + colInLine;
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
        {
            return;
        }

        using var measure = BeginTextMeasurement();
        double x;
        if (CaretPosition <= lineStart)
        {
            x = 0;
        }
        else
        {
            var cache = _textView.EnsureLineMeasureCache(line, lineStart, lineEnd, measure.Context, measure.Font);
            x = MultiLineTextView.GetPrefixWidthCached(cache, CaretPosition - lineStart, measure.Context, measure.Font);
        }

        GetLineSpan(newLine, out int ns, out int ne);
        var newCache = _textView.EnsureLineMeasureCache(newLine, ns, ne, measure.Context, measure.Font);
        int newPos = ns + MultiLineTextView.GetCharIndexFromXCached(newCache, x, measure.Context, measure.Font);
        SetCaretAndSelection(newPos, extendSelection);
    }

    private void MoveToLineEdge(bool start, bool extendSelection)
    {
        GetLineFromIndex(CaretPosition, out _, out int lineStart, out int lineEnd);
        int newPos = start ? lineStart : lineEnd;
        SetCaretAndSelection(newPos, extendSelection);
    }

    private void EnsureCaretVisible(Rect contentBounds)
    {
        using var measure = BeginTextMeasurement();
        var font = measure.Font;

        GetLineFromIndex(CaretPosition, out int line, out int lineStart, out int lineEnd);
        double lineHeight = GetLineHeight();

        double caretY;
        double caretX;

        if (!WrapEnabled)
        {
            caretY = line * lineHeight;
            if (CaretPosition <= lineStart)
            {
                caretX = 0;
            }
            else
            {
                var cache = _textView.EnsureLineMeasureCache(line, lineStart, lineEnd, measure.Context, font);
                caretX = MultiLineTextView.GetPrefixWidthCached(cache, CaretPosition - lineStart, measure.Context, font);
            }
        }
        else
        {
            double wrapWidth = Math.Max(1, contentBounds.Width);
            GetLineSpan(line, out _, out int wrapLineEnd);
            var lineMeasure = _textView.EnsureLineMeasureCache(line, lineStart, wrapLineEnd, measure.Context, font);
            string fullLine = lineMeasure.Text;
            var layout = _wrapVirtualizer.GetWrapLayout(line, fullLine, wrapWidth, measure.Context, font);
            int caretCol = Math.Clamp(CaretPosition - lineStart, 0, fullLine.Length);
            int caretRow = TextWrapVirtualizer.GetWrapRowFromColumn(layout, caretCol);
            int lineStartRow = _wrapVirtualizer.GetVisualRowStartForLine(line, wrapWidth, measure.Context, font);
            caretY = (lineStartRow + caretRow) * lineHeight;

            int segStart = layout.SegmentStarts[caretRow];
            caretX = MultiLineTextView.GetPrefixWidthCached(lineMeasure, caretCol, measure.Context, font) -
                     MultiLineTextView.GetPrefixWidthCached(lineMeasure, segStart, measure.Context, font);
        }

        double viewportH = Math.Max(1, contentBounds.Height);
        double viewportW = Math.Max(1, contentBounds.Width);
        double extentH = GetExtentHeight(viewportW);
        double extentW = (!_hBar.IsVisible || WrapEnabled) ? 0 : GetExtentWidthForViewport(measure.Context, font, viewportH);

        if (caretY < VerticalOffset)
        {
            SetVerticalOffset(caretY, false);
        }
        else if (caretY + lineHeight > VerticalOffset + viewportH)
        {
            SetVerticalOffset(caretY + lineHeight - viewportH, false);
        }

        if (!WrapEnabled)
        {
            if (caretX < HorizontalOffset)
            {
                SetHorizontalOffset(caretX, false);
            }
            else if (caretX > HorizontalOffset + viewportW)
            {
                SetHorizontalOffset(caretX - viewportW, false);
            }
        }

        var dpiScale = GetDpi() / 96.0;
        SetVerticalOffset(ClampOffset(VerticalOffset, extentH, viewportH, dpiScale), false);
        SetHorizontalOffset((_hBar.IsVisible && !WrapEnabled) ? ClampOffset(HorizontalOffset, extentW, viewportW, dpiScale) : 0, false);

        if (_vBar.IsVisible)
        {
            _vBar.Value = VerticalOffset;
        }

        if (_hBar.IsVisible)
        {
            _hBar.Value = HorizontalOffset;
        }
    }

    private void ClampOffsets(Rect contentBounds)
    {
        var dpiScale = GetDpi() / 96.0;
        double wrapWidth = Math.Max(1, contentBounds.Width);
        SetVerticalOffset(ClampOffset(VerticalOffset, GetExtentHeight(wrapWidth), Math.Max(1, contentBounds.Height), dpiScale), false);
        if (!_hBar.IsVisible || WrapEnabled)
        {
            SetHorizontalOffset(0, false);
        }
        else
        {
            using var measure = BeginTextMeasurement();
            double extentW = GetExtentWidthForViewport(measure.Context, measure.Font, Math.Max(1, contentBounds.Height));
            SetHorizontalOffset(ClampOffset(HorizontalOffset, extentW, Math.Max(1, contentBounds.Width), dpiScale), false);
        }
        if (_vBar.IsVisible)
        {
            _vBar.Value = VerticalOffset;
        }

        if (_hBar.IsVisible)
        {
            _hBar.Value = HorizontalOffset;
        }
    }

    private double GetExtentHeight(double wrapWidth)
    {
        if (!WrapEnabled)
        {
            return Math.Max(0, _lineStarts.Count * GetLineHeight());
        }

        return _wrapVirtualizer.GetExtentHeight(wrapWidth, GetLineHeight(), FontSize);
    }

    private double GetExtentWidth()
    {
        int version = DocumentVersion;
        var fontKey = new TextLineWidthEstimator.FontKey(FontFamily, FontSize, FontWeight, GetDpi());
        if (_lineWidthEstimator.TryGetCached(version, fontKey, out double cached))
        {
            return cached;
        }

        using var measure = BeginTextMeasurement();
        return _lineWidthEstimator.Compute(measure.Context, measure.Font, version, fontKey);
    }

    private double GetExtentWidth(IGraphicsContext context, IFont font)
    {
        int version = DocumentVersion;
        var fontKey = new TextLineWidthEstimator.FontKey(FontFamily, FontSize, FontWeight, GetDpi());
        if (_lineWidthEstimator.TryGetCached(version, fontKey, out double cached))
        {
            return cached;
        }

        return _lineWidthEstimator.Compute(context, font, version, fontKey);
    }

    private double GetExtentWidthForViewport(IGraphicsContext context, IFont font, double viewportHeightDip)
    {
        if (WrapEnabled)
        {
            return 0;
        }

        int version = DocumentVersion;
        var fontKey = new TextLineWidthEstimator.FontKey(FontFamily, FontSize, FontWeight, GetDpi());

        if (CanComputeExtentWidth())
        {
            return GetExtentWidth(context, font);
        }

        // For very large documents, avoid scanning all lines. Instead, update an observed max using the visible line range.
        double lineHeight = GetLineHeight();
        int lineCount = Math.Max(0, _lineStarts.Count);
        if (lineCount <= 0 || lineHeight <= 0)
        {
            return 0;
        }

        int firstLine = (int)Math.Floor(VerticalOffset / lineHeight);
        firstLine = Math.Clamp(firstLine, 0, Math.Max(0, lineCount - 1));
        int maxLines = (int)Math.Ceiling(Math.Max(0, viewportHeightDip) / lineHeight) + 2;
        int lastExclusive = Math.Min(lineCount, firstLine + Math.Max(1, maxLines));

        return _lineWidthEstimator.ComputeObservedMax(context, font, version, fontKey, firstLine, lastExclusive);
    }

    private double GetLineHeight() => _lineHeight > 0 ? _lineHeight : Math.Max(16, FontSize * 1.4);

    private void RebuildLineStartsFromDocument()
    {
        _lineStarts.Clear();
        _lineStarts.Add(0);

        for (int i = 0; i < Document.Length; i++)
        {
            if (Document[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }

        if (_lineStarts.Count == 0)
        {
            _lineStarts.Add(0);
        }
    }

    private void EnforceWrapLineLimit()
    {
        if (_lineStarts.Count <= WrapLineCountHardLimit)
        {
            return;
        }

        if (!WrapEnabled)
        {
            return;
        }

        SetWrapEnabled(false);
    }

    private void GetLineSpan(int line, out int start, out int end)
    {
        if (_lineStarts.Count == 0)
        {
            RebuildLineStartsFromDocument();
        }

        line = Math.Clamp(line, 0, _lineStarts.Count - 1);
        start = _lineStarts[line];
        end = line + 1 < _lineStarts.Count ? _lineStarts[line + 1] - 1 : Document.Length;
        if (end < start)
        {
            end = start;
        }

        if (end > start && Document[end - 1] == '\r')
        {
            end--;
        }
    }

    private void GetLineFromIndex(int index, out int line, out int lineStart, out int lineEnd)
    {
        if (_lineStarts.Count == 0)
        {
            RebuildLineStartsFromDocument();
        }

        index = Math.Clamp(index, 0, Document.Length);

        int lo = 0;
        int hi = _lineStarts.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            int s = _lineStarts[mid];
            if (s <= index)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        line = Math.Clamp(lo - 1, 0, _lineStarts.Count - 1);
        GetLineSpan(line, out lineStart, out lineEnd);
    }

    private void RenderSelectionForRow(
        IGraphicsContext context,
        IFont font,
        Theme theme,
        int lineStart,
        int rowSegmentStart,
        ReadOnlySpan<char> rowText,
        double y,
        double xBase,
        MultiLineTextView.CachedLineMeasure? fullLineMeasure = null)
    {
        if (!HasSelection || rowText.IsEmpty)
        {
            return;
        }

        var (selA, selB) = GetSelectionRange();

        int rowStart = lineStart + rowSegmentStart;
        int rowEnd = rowStart + rowText.Length;
        int s = Math.Max(selA, rowStart);
        int t = Math.Min(selB, rowEnd);
        if (s >= t)
        {
            return;
        }

        int relS = s - rowStart;
        int relT = t - rowStart;

        double beforeW;
        double selW;
        if (fullLineMeasure != null)
        {
            int a = rowSegmentStart + relS;
            int b = rowSegmentStart + relT;
            beforeW = relS <= 0 ? 0 : MultiLineTextView.GetSpanWidthCached(fullLineMeasure, rowSegmentStart, a, context, font);
            selW = MultiLineTextView.GetSpanWidthCached(fullLineMeasure, a, b, context, font);
        }
        else
        {
            beforeW = relS <= 0 ? 0 : context.MeasureText(rowText[..relS], font).Width;
            selW = context.MeasureText(rowText[relS..relT], font).Width;
        }

        context.FillRectangle(new Rect(xBase + beforeW, y, selW, GetLineHeight()), theme.Palette.SelectionBackground);
    }

    private void DrawCaretForWrappedRow(
        IGraphicsContext context,
        Rect contentBounds,
        IFont font,
        Theme theme,
        int lineStart,
        int segStart,
        int segEnd,
        ReadOnlySpan<char> rowText,
        double y,
        MultiLineTextView.CachedLineMeasure lineMeasure)
    {
        if (!IsFocused || !IsEnabled)
        {
            return;
        }

        int caret = CaretPosition;
        int rowStart = lineStart + segStart;
        int rowEnd = lineStart + segEnd;
        if (caret < rowStart || caret > rowEnd)
        {
            return;
        }

        int rel = Math.Clamp(caret - rowStart, 0, rowText.Length);
        double x = contentBounds.X +
                   (rel <= 0
                       ? 0
                       : MultiLineTextView.GetSpanWidthCached(lineMeasure, segStart, segStart + rel, context, font));
        DrawCaret(context, x, y, GetLineHeight(), theme);
    }

    // Note: text measurement caches live in MultiLineTextView.

    private static void DrawCaret(IGraphicsContext context, double x, double y, double lineHeight, Theme theme)
    {
        if (lineHeight <= 0)
        {
            return;
        }

        // Keep caret slightly inset to match single-line TextBox behavior and avoid a "top aligned" look.
        if (lineHeight <= 4)
        {
            context.FillRectangle(new Rect(x, y, 1, Math.Max(1, lineHeight)), theme.Palette.WindowText);
            return;
        }

        const double pad = 2;
        double top = y + pad;
        double bottom = y + lineHeight - pad;
        if (bottom <= top)
        {
            top = y;
            bottom = y + lineHeight;
        }

        context.DrawLine(new Point(x, top), new Point(x, bottom), theme.Palette.WindowText, 1);
    }

    private string GetLineText(int lineIndex, int start, int end)
    {
        return _textView.GetLineText(lineIndex, start, end);
    }

    private void ApplyInsert(int index, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _textView.Reset();
        _wrapVirtualizer.Reset();
        _lineWidthEstimator.Reset();

        index = ApplyInsertCore(index, text.AsSpan());
        UpdateLineStartsOnInsert(index, text);

        CaretPosition = Math.Clamp(CaretPosition, 0, GetTextLengthCore());
    }

    private void ApplyRemove(int index, int length)
    {
        if (length <= 0)
        {
            return;
        }

        _textView.Reset();
        _wrapVirtualizer.Reset();
        _lineWidthEstimator.Reset();

        int removed = ApplyRemoveCore(index, length);
        if (removed > 0)
        {
            UpdateLineStartsOnRemove(index, removed);
        }

        CaretPosition = Math.Clamp(CaretPosition, 0, GetTextLengthCore());
    }

    private void UpdateLineStartsOnInsert(int index, string insertedText)
    {
        int len = insertedText.Length;
        for (int i = 0; i < _lineStarts.Count; i++)
        {
            if (_lineStarts[i] > index)
            {
                _lineStarts[i] += len;
            }
        }

        int insertPos = LowerBoundLineStart(index + 1);
        for (int i = 0; i < insertedText.Length; i++)
        {
            if (insertedText[i] != '\n')
            {
                continue;
            }

            _lineStarts.Insert(insertPos, index + i + 1);
            insertPos++;
        }
    }

    private void UpdateLineStartsOnRemove(int index, int removedLength)
    {
        int end = index + removedLength;

        for (int i = _lineStarts.Count - 1; i >= 0; i--)
        {
            int s = _lineStarts[i];
            if (s > index && s <= end)
            {
                _lineStarts.RemoveAt(i);
            }
        }

        for (int i = 0; i < _lineStarts.Count; i++)
        {
            if (_lineStarts[i] > end)
            {
                _lineStarts[i] -= removedLength;
            }
        }

        if (_lineStarts.Count == 0)
        {
            _lineStarts.Add(0);
        }
    }

    private int LowerBoundLineStart(int value)
    {
        int lo = 0;
        int hi = _lineStarts.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_lineStarts[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _vBar.Dispose();
        _hBar.Dispose();
    }

    private readonly record struct WrapLayout(int Version, double Width, int[] SegmentStarts);
    private readonly record struct WrapAnchor(int LineIndex, int StartRow);

    private void CaptureViewAnchor()
    {
        _pendingViewAnchorIndex = -1;
        _pendingViewAnchorYOffset = 0;
        _pendingViewAnchorXOffset = 0;

        double lineHeight = GetLineHeight();
        if (lineHeight <= 0)
        {
            return;
        }

        _pendingViewAnchorYOffset = VerticalOffset - Math.Floor(VerticalOffset / lineHeight) * lineHeight;

        using var measure = BeginTextMeasurement();
        double viewportW = GetViewportContentBounds().Width;
        double wrapWidth = Math.Max(1, viewportW);

        if (WrapEnabled)
        {
            int firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / lineHeight));
            _wrapVirtualizer.MapVisualRowToLine(firstRow, wrapWidth, measure.Context, measure.Font, out int lineIndex, out int rowInLine);

            GetLineSpan(lineIndex, out int lineStart, out int lineEnd);
            string fullLine = GetLineText(lineIndex, lineStart, lineEnd);
            var layout = _wrapVirtualizer.GetWrapLayout(lineIndex, fullLine, wrapWidth, measure.Context, measure.Font);
            rowInLine = Math.Clamp(rowInLine, 0, Math.Max(0, layout.SegmentStarts.Length - 1));

            int segStart = layout.SegmentStarts.Length == 0 ? 0 : layout.SegmentStarts[rowInLine];
            _pendingViewAnchorIndex = Math.Clamp(lineStart + segStart, 0, GetTextLengthCore());
            _pendingViewAnchorXOffset = 0;
            return;
        }

        int firstLine = Math.Max(0, (int)Math.Floor(VerticalOffset / lineHeight));
        firstLine = Math.Clamp(firstLine, 0, Math.Max(0, _lineStarts.Count - 1));

        GetLineSpan(firstLine, out int start, out int end);
        var lineMeasure = _textView.EnsureLineMeasureCache(firstLine, start, end, measure.Context, measure.Font);
        int col = MultiLineTextView.GetCharIndexFromXCached(lineMeasure, HorizontalOffset, measure.Context, measure.Font);
        double colWidth = col <= 0 ? 0 : MultiLineTextView.GetPrefixWidthCached(lineMeasure, col, measure.Context, measure.Font);
        _pendingViewAnchorXOffset = HorizontalOffset - colWidth;
        _pendingViewAnchorIndex = Math.Clamp(start + col, 0, GetTextLengthCore());
    }

    private void ApplyViewAnchorIfPending()
    {
        if (_pendingViewAnchorIndex < 0)
        {
            return;
        }

        double lineHeight = GetLineHeight();
        if (lineHeight <= 0)
        {
            _pendingViewAnchorIndex = -1;
            return;
        }

        using var measure = BeginTextMeasurement();

        var viewportBounds = GetViewportContentBounds();
        double viewportW = viewportBounds.Width;
        double viewportH = viewportBounds.Height;
        double wrapWidth = Math.Max(1, viewportW);

        GetLineFromIndex(_pendingViewAnchorIndex, out int line, out int lineStart, out int lineEnd);
        int col = Math.Clamp(_pendingViewAnchorIndex - lineStart, 0, Math.Max(0, lineEnd - lineStart));

        if (WrapEnabled)
        {
            string fullLine = GetLineText(line, lineStart, lineEnd);
            var layout = _wrapVirtualizer.GetWrapLayout(line, fullLine, wrapWidth, measure.Context, measure.Font);
            int rowInLine = TextWrapVirtualizer.GetWrapRowFromColumn(layout, col);
            int rowStart = _wrapVirtualizer.GetVisualRowStartForLine(line, wrapWidth, measure.Context, measure.Font);
            SetVerticalOffset((rowStart + rowInLine) * lineHeight + _pendingViewAnchorYOffset, false);
            SetHorizontalOffset(0, false);
        }
        else
        {
            SetVerticalOffset(line * lineHeight + _pendingViewAnchorYOffset, false);

            var lineMeasure = _textView.EnsureLineMeasureCache(line, lineStart, lineEnd, measure.Context, measure.Font);
            col = Math.Clamp(col, 0, lineMeasure.Text.Length);
            double colWidth = col <= 0 ? 0 : MultiLineTextView.GetPrefixWidthCached(lineMeasure, col, measure.Context, measure.Font);
            SetHorizontalOffset(Math.Max(0, colWidth + _pendingViewAnchorXOffset), false);
        }

        _pendingViewAnchorIndex = -1;
        _pendingViewAnchorYOffset = 0;
        _pendingViewAnchorXOffset = 0;

        var dpiScale = GetDpi() / 96.0;
        double extentH = GetExtentHeight(wrapWidth);
        SetVerticalOffset(ClampOffset(VerticalOffset, extentH, viewportH, dpiScale), false);

        double extentW = (!_hBar.IsVisible || WrapEnabled) ? 0 : GetExtentWidthForViewport(measure.Context, measure.Font, viewportH);
        SetHorizontalOffset((_hBar.IsVisible && !WrapEnabled) ? ClampOffset(HorizontalOffset, extentW, viewportW, dpiScale) : 0, false);

        if (_vBar.IsVisible)
        {
            _vBar.Value = VerticalOffset;
        }

        if (_hBar.IsVisible)
        {
            _hBar.Value = HorizontalOffset;
        }
    }
}
