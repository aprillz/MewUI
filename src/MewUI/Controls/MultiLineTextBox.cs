using Aprillz.MewUI.Core;
using Aprillz.MewUI.Binding;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Panels;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A multi-line text input control with thin scrollbars.
/// </summary>
public sealed class MultiLineTextBox : Control
{
    private int _selectionStart;
    private int _selectionLength;
    private double _verticalOffset;
    private double _horizontalOffset;
    private double _lineHeight;
    private readonly List<int> _lineStarts = new() { 0 };
    private bool _suppressTextInputNewline;
    private bool _suppressTextInputTab;
    private ValueBinding<string>? _textBinding;
    private bool _suppressBindingSet;

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

    public string Text
    {
        get;
        set
        {
            if (field == value)
                return;

            field = NormalizeText(value ?? string.Empty);
            CaretPosition = Math.Min(CaretPosition, field.Length);
            _selectionStart = 0;
            _selectionLength = 0;
            RebuildLineStarts();
            TextChanged?.Invoke(Text);
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = string.Empty;

    public string Placeholder
    {
        get;
        set { field = value ?? string.Empty; InvalidateVisual(); }
    } = string.Empty;

    public bool IsReadOnly
    {
        get;
        set { field = value; InvalidateVisual(); }
    }

    public bool AcceptTab { get; set; }

    public int CaretPosition
    {
        get;
        set { field = Math.Clamp(value, 0, Text.Length); InvalidateVisual(); }
    }

    public Action<string>? TextChanged { get; set; }

    public override bool Focusable => true;

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

        double extentH = GetExtentHeight();
        double extentW = GetExtentWidth();

        double viewportH = Math.Max(0, innerBounds.Height - Padding.VerticalThickness);
        double viewportW = Math.Max(0, innerBounds.Width - Padding.HorizontalThickness);

        _verticalOffset = ClampOffset(_verticalOffset, extentH, viewportH);
        _horizontalOffset = ClampOffset(_horizontalOffset, extentW, viewportW);

        bool needV = extentH > viewportH + 0.5;
        bool needH = extentW > viewportW + 0.5;

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

        _verticalOffset = ClampOffset(_verticalOffset - notches * GetTheme().ScrollWheelStep, GetExtentHeight(), GetViewportHeight());
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

        if (!IsReadOnly && ctrl && e.Key == Key.A)
        {
            _selectionStart = 0;
            _selectionLength = Text.Length;
            CaretPosition = Text.Length;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.C)
        {
            CopyToClipboard();
            e.Handled = true;
            return;
        }

        if (!IsReadOnly && ctrl && e.Key == Key.X)
        {
            CutToClipboard();
            e.Handled = true;
            return;
        }

        if (!IsReadOnly && ctrl && e.Key == Key.V)
        {
            PasteFromClipboard();
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

        int firstLine = lineHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(_verticalOffset / lineHeight));
        double offsetInLine = lineHeight <= 0 ? 0 : _verticalOffset - firstLine * lineHeight;
        double y = contentBounds.Y - offsetInLine;

        int maxLines = lineHeight <= 0 ? lineCount : (int)Math.Ceiling((contentBounds.Height + offsetInLine) / lineHeight) + 1;
        int lastExclusive = Math.Min(lineCount, firstLine + Math.Max(0, maxLines));

        var textColor = IsEnabled ? Foreground : theme.DisabledText;

        for (int line = firstLine; line < lastExclusive; line++)
        {
            GetLineSpan(line, out int start, out int end);
            string lineText = start < end ? Text.Substring(start, end - start) : string.Empty;

            var lineRect = new Rect(contentBounds.X - _horizontalOffset, y, 1_000_000, lineHeight);

            // Selection background for this line
            if (IsFocused && _selectionLength != 0)
            {
                int selA = Math.Min(_selectionStart, _selectionStart + _selectionLength);
                int selB = Math.Max(_selectionStart, _selectionStart + _selectionLength);

                int s = Math.Max(selA, start);
                int t = Math.Min(selB, end);
                if (s < t)
                {
                    double beforeW = MeasureSubstringWidth(context, font, start, s);
                    double selW = MeasureSubstringWidth(context, font, s, t);
                    var selRect = new Rect(contentBounds.X - _horizontalOffset + beforeW, y, selW, lineHeight);
                    context.FillRectangle(selRect, theme.SelectionBackground);
                }
            }

            context.DrawText(lineText, lineRect, font, textColor, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
            y += lineHeight;
        }

        if (IsFocused)
            DrawCaret(context, contentBounds, font, theme);
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
        int line = lineHeight <= 0 ? 0 : (int)Math.Floor((p.Y - contentBounds.Y + _verticalOffset) / lineHeight);
        line = Math.Clamp(line, 0, _lineStarts.Count - 1);

        GetLineSpan(line, out int start, out int end);
        double x = p.X - contentBounds.X + _horizontalOffset;

        CaretPosition = GetCharIndexFromXInLine(x, start, end);
    }

    private int GetCharIndexFromXInLine(double x, int start, int end)
    {
        if (start >= end)
            return start;

        using var measure = BeginTextMeasurement();
        var font = measure.Font;

        if (x <= 0)
            return start;

        string line = Text.Substring(start, end - start);
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
        int newPos = Math.Clamp(CaretPosition + delta, 0, Text.Length);
        SetCaretAndSelection(newPos, extendSelection);
    }

    private void MoveCaretVertical(int deltaLines, bool extendSelection)
    {
        GetLineFromIndex(CaretPosition, out int line, out int lineStart, out int lineEnd);
        int newLine = Math.Clamp(line + deltaLines, 0, _lineStarts.Count - 1);
        if (newLine == line)
            return;

        using var measure = BeginTextMeasurement();
        double x = measure.Context.MeasureText(Text.Substring(lineStart, CaretPosition - lineStart), measure.Font).Width;

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

        Text = Text.Remove(CaretPosition - 1, 1);
        CaretPosition--;
    }

    private void Delete()
    {
        if (DeleteSelectionIfAny())
            return;

        if (CaretPosition >= Text.Length)
            return;

        Text = Text.Remove(CaretPosition, 1);
    }

    private bool DeleteSelectionIfAny()
    {
        if (_selectionLength == 0)
            return false;

        int a = Math.Min(_selectionStart, _selectionStart + _selectionLength);
        int b = Math.Max(_selectionStart, _selectionStart + _selectionLength);

        Text = Text.Remove(a, b - a);
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

        Text = Text.Insert(CaretPosition, normalized);
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

        string s = Text.Substring(a, b - a);
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

    private bool TryClipboardSetText(string text)
    {
        if (!Application.IsRunning)
            return false;

        IClipboardService clipboard = Application.Current.PlatformHost.Clipboard;
        return clipboard.TrySetText(text);
    }

    private bool TryClipboardGetText(out string text)
    {
        text = string.Empty;
        if (!Application.IsRunning)
            return false;

        IClipboardService clipboard = Application.Current.PlatformHost.Clipboard;
        return clipboard.TryGetText(out text);
    }

    private void EnsureCaretVisible(Rect contentBounds)
    {
        using var measure = BeginTextMeasurement();
        var font = measure.Font;

        GetLineFromIndex(CaretPosition, out int line, out int lineStart, out _);
        double lineHeight = GetLineHeight();

        double caretY = line * lineHeight;
        double caretX = measure.Context.MeasureText(Text.Substring(lineStart, CaretPosition - lineStart), font).Width;

        double viewportH = Math.Max(1, contentBounds.Height);
        double viewportW = Math.Max(1, contentBounds.Width);
        double extentH = GetExtentHeight();
        double extentW = GetExtentWidth();

        if (caretY < _verticalOffset)
            _verticalOffset = caretY;
        else if (caretY + lineHeight > _verticalOffset + viewportH)
            _verticalOffset = caretY + lineHeight - viewportH;

        if (caretX < _horizontalOffset)
            _horizontalOffset = caretX;
        else if (caretX > _horizontalOffset + viewportW)
            _horizontalOffset = caretX - viewportW;

        _verticalOffset = ClampOffset(_verticalOffset, extentH, viewportH);
        _horizontalOffset = ClampOffset(_horizontalOffset, extentW, viewportW);

        if (_vBar.IsVisible) _vBar.Value = _verticalOffset;
        if (_hBar.IsVisible) _hBar.Value = _horizontalOffset;
    }

    private void ClampOffsets(Rect contentBounds)
    {
        _verticalOffset = ClampOffset(_verticalOffset, GetExtentHeight(), Math.Max(1, contentBounds.Height));
        _horizontalOffset = ClampOffset(_horizontalOffset, GetExtentWidth(), Math.Max(1, contentBounds.Width));
        if (_vBar.IsVisible) _vBar.Value = _verticalOffset;
        if (_hBar.IsVisible) _hBar.Value = _horizontalOffset;
    }

    private double GetExtentHeight()
        => Math.Max(0, _lineStarts.Count * GetLineHeight());

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
            max = Math.Max(max, measure.Context.MeasureText(Text.Substring(s, e - s), measure.Font).Width);
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

    private double GetLineHeight() => _lineHeight > 0 ? _lineHeight : Math.Max(16, FontSize * 1.4);

    private void RebuildLineStarts()
    {
        _lineStarts.Clear();
        _lineStarts.Add(0);

        for (int i = 0; i < Text.Length; i++)
        {
            if (Text[i] == '\n')
                _lineStarts.Add(i + 1);
        }

        if (_lineStarts.Count == 0)
            _lineStarts.Add(0);
    }

    private void GetLineSpan(int line, out int start, out int end)
    {
        if (_lineStarts.Count == 0)
            RebuildLineStarts();

        line = Math.Clamp(line, 0, _lineStarts.Count - 1);
        start = _lineStarts[line];
        end = line + 1 < _lineStarts.Count ? _lineStarts[line + 1] - 1 : Text.Length;
        if (end < start) end = start;

        if (end > start && Text[end - 1] == '\r')
            end--;
    }

    private void GetLineFromIndex(int index, out int line, out int lineStart, out int lineEnd)
    {
        if (_lineStarts.Count == 0)
            RebuildLineStarts();

        index = Math.Clamp(index, 0, Text.Length);

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

        return context.MeasureText(Text.Substring(start, end - start), font).Width;
    }

    private static string NormalizeText(string text)
    {
        if (text.Length == 0)
            return string.Empty;

        // Normalize Windows line endings and stray CR into '\n' to keep layout/caret math consistent.
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return text;
    }

    protected override void OnDispose()
    {
        _textBinding?.Dispose();
        _textBinding = null;
        _vBar.Dispose();
        _hBar.Dispose();
    }

    public void SetTextBinding(
        Func<string> get,
        Action<string> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        _textBinding?.Dispose();
        _textBinding = new ValueBinding<string>(
            get,
            set,
            subscribe,
            unsubscribe,
            onSourceChanged: () =>
            {
                if (IsFocused)
                    return;

                var value = NormalizeText(get() ?? string.Empty);
                if (Text == value)
                    return;

                _suppressBindingSet = true;
                try { Text = value; }
                finally { _suppressBindingSet = false; }
            });

        var existing = TextChanged;
        TextChanged = text =>
        {
            existing?.Invoke(text);

            if (_suppressBindingSet)
                return;

            _textBinding?.Set(text);
        };

        _suppressBindingSet = true;
        try { Text = NormalizeText(get() ?? string.Empty); }
        finally { _suppressBindingSet = false; }
    }
}
