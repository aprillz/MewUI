using Aprillz.MewUI.Core;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A single-line text input control.
/// </summary>
public class TextBox : TextBase
{
    private double _scrollOffset;
    private bool _suppressTextInputTab;

    protected override Color DefaultBackground => Theme.Current.ControlBackground;
    protected override Color DefaultBorderBrush => Theme.Current.ControlBorder;

    public TextBox()
    {
        BorderThickness = 1; 
        Padding = new Thickness(4);
        MinHeight = 28;
  }

    protected override string NormalizeText(string text)
    {
        if (text.Length == 0)
            return string.Empty;

        text = text.Replace("\r\n", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (!AcceptTab && text.Contains('\t'))
            text = text.Replace("\t", string.Empty);
        return text;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // Default size for empty text box
        var minHeight = FontSize + Padding.VerticalThickness + 4;
        return new Size(100, minHeight);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(borderInset));
        double radius = theme.ControlCornerRadius;

        var state = GetVisualState();
        var borderColor = PickAccentBorder(theme, BorderBrush, state, hoverMix: 0.6);

        DrawBackgroundAndBorder(
            context,
            bounds,
            state.IsEnabled ? Background : theme.TextBoxDisabledBackground,
            borderColor,
            radius);

        // Set up clipping for content
        context.Save();
        context.SetClip(contentBounds);

        var font = GetFont();

        // Draw placeholder or text
        if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Placeholder) && !state.IsFocused)
        {
            context.DrawText(Placeholder, contentBounds, font, theme.PlaceholderText,
                TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
        else if (!string.IsNullOrEmpty(Text))
        {
            // Calculate text position with scroll offset
            var textX = contentBounds.X - _scrollOffset;

            // Draw selection background if any
            if (_selectionLength != 0 && IsFocused)
            {
                var selStart = Math.Min(_selectionStart, _selectionStart + _selectionLength);
                var selEnd = Math.Max(_selectionStart, _selectionStart + _selectionLength);

                var beforeSel = Text[..selStart];
                var selection = Text[selStart..selEnd];

                var beforeWidth = context.MeasureText(beforeSel, font).Width;
                var selWidth = context.MeasureText(selection, font).Width;

                var selRect = new Rect(textX + beforeWidth, contentBounds.Y,
                    selWidth, contentBounds.Height);
                context.FillRectangle(selRect, theme.SelectionBackground);
            }

            // Draw text
            var textColor = state.IsEnabled ? Foreground : theme.DisabledText;
            // Use backend vertical centering (font metrics differ from FontSize across renderers).
            context.DrawText(Text, new Rect(textX, contentBounds.Y, 1_000_000, contentBounds.Height), font, textColor,
                TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }

        // Draw caret if focused
        if (state.IsFocused && !IsReadOnly)
        {
            var caretX = contentBounds.X - _scrollOffset;
            if (CaretPosition > 0)
            {
                var textBefore = Text[..CaretPosition];
                caretX += context.MeasureText(textBefore, font).Width;
            }

            // Simple caret - could be animated
            context.DrawLine(
                new Point(caretX, contentBounds.Y + 2),
                new Point(caretX, contentBounds.Bottom - 2),
                theme.WindowText, 1);
        }

        context.Restore();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButton.Left)
        {
            Focus();

            // Calculate caret position from click
            var contentBounds = Bounds.Deflate(Padding);
            var clickX = e.Position.X - contentBounds.X + _scrollOffset;

            CaretPosition = GetCharacterIndexFromX(clickX);
            _selectionStart = CaretPosition;
            _selectionLength = 0;

            // Capture for selection
            var root = FindVisualRoot();
            if (root is Window window)
                window.CaptureMouse(this);

            EnsureCaretVisible();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (IsMouseCaptured && e.LeftButton)
        {
            // Update selection (with auto-scroll when dragging outside the visible region)
            var contentBounds = Bounds.Deflate(Padding);

            // If the pointer goes beyond the text box, scroll in that direction.
            // This matches common native text box behavior and enables selecting off-screen text.
            const double edgeDip = 8;
            if (e.Position.X < contentBounds.X + edgeDip)
                _scrollOffset += e.Position.X - (contentBounds.X + edgeDip);
            else if (e.Position.X > contentBounds.Right - edgeDip)
                _scrollOffset += e.Position.X - (contentBounds.Right - edgeDip);

            ClampScrollOffset();

            var clickX = e.Position.X - contentBounds.X + _scrollOffset;

            var newPos = GetCharacterIndexFromX(clickX);
            _selectionLength = newPos - _selectionStart;
            CaretPosition = newPos;

            EnsureCaretVisible();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButton.Left)
        {
            var root = FindVisualRoot();
            if (root is Window window)
                window.ReleaseMouseCapture();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || IsReadOnly && !IsNavigationKey(e.Key)) return;

        bool shift = e.ShiftKey;
        bool ctrl = e.ControlKey;

        switch (e.Key)
        {
            case Key.Tab:
                if (!IsReadOnly && AcceptTab)
                {
                    DeleteSelection();
                    InsertIntoDocument(CaretPosition, "\t".AsSpan());
                    CaretPosition += 1;
                    NotifyTextChanged();
                    _suppressTextInputTab = true;
                    EnsureCaretVisible();
                    e.Handled = true;
                }
                break;

            case Key.Left:
                MoveCaret(-1, shift, ctrl);
                e.Handled = true;
                break;

            case Key.Right:
                MoveCaret(1, shift, ctrl);
                e.Handled = true;
                break;

            case Key.Home:
                MoveCaret(-Text.Length, shift, false);
                e.Handled = true;
                break;

            case Key.End:
                MoveCaret(Text.Length, shift, false);
                e.Handled = true;
                break;

            case Key.Backspace:
                if (!IsReadOnly) HandleBackspace(ctrl);
                e.Handled = true;
                break;

            case Key.Delete:
                if (!IsReadOnly) HandleDelete(ctrl);
                e.Handled = true;
                break;
        }

        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsReadOnly) return;

        var text = e.Text ?? string.Empty;

        if (_suppressTextInputTab)
        {
            _suppressTextInputTab = false;
            if (text.Contains('\t'))
            {
                e.Handled = true;
                return;
            }
        }

        if (text.Contains('\r') || text.Contains('\n'))
            text = text.Replace("\r\n", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

        if (!AcceptTab && text.Contains('\t'))
            text = text.Replace("\t", string.Empty);

        if (text.Length == 0)
            return;

        // Delete selection if any
        DeleteSelection();

        // Insert text
        InsertIntoDocument(CaretPosition, text.AsSpan());
        CaretPosition += text.Length;
        NotifyTextChanged();

        EnsureCaretVisible();
        InvalidateVisual();
        e.Handled = true;
    }

    private int GetCharacterIndexFromX(double x)
    {
        if (string.IsNullOrEmpty(Text)) return 0;

        using var measure = BeginTextMeasurement();

        if (x <= 0)
            return 0;

        double totalWidth = measure.Context.MeasureText(Text, measure.Font).Width;
        if (x >= totalWidth)
            return Text.Length;

        // Binary search to avoid O(n^2) measurement cost for long text.
        int lo = 0;
        int hi = Text.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            double w = mid > 0 ? measure.Context.MeasureText(Text[..mid], measure.Font).Width : 0;
            if (w < x)
                lo = mid + 1;
            else
                hi = mid;
        }

        int idx = Math.Clamp(lo, 0, Text.Length);

        // Snap to the nearest caret position using midpoints for better feel.
        if (idx <= 0)
            return 0;
        double w0 = measure.Context.MeasureText(Text[..(idx - 1)], measure.Font).Width;
        double w1 = measure.Context.MeasureText(Text[..idx], measure.Font).Width;
        return x < (w0 + w1) / 2 ? idx - 1 : idx;
    }

    private void MoveCaret(int direction, bool extend, bool word)
    {
        int newPos = CaretPosition;

        if (word)
        {
            // Move by word
            if (direction < 0)
            {
                newPos = FindPreviousWordBoundary(CaretPosition);
            }
            else
            {
                newPos = FindNextWordBoundary(CaretPosition);
            }
        }
        else
        {
            newPos = Math.Clamp(CaretPosition + direction, 0, Text.Length);
        }

        if (extend)
        {
            _selectionLength += newPos - CaretPosition;
        }
        else
        {
            _selectionStart = newPos;
            _selectionLength = 0;
        }

        CaretPosition = newPos;
        EnsureCaretVisible();
    }

    private int FindPreviousWordBoundary(int from)
    {
        if (from <= 0) return 0;
        int pos = from - 1;
        while (pos > 0 && char.IsWhiteSpace(Text[pos])) pos--;
        while (pos > 0 && !char.IsWhiteSpace(Text[pos - 1])) pos--;
        return pos;
    }

    private int FindNextWordBoundary(int from)
    {
        if (from >= Text.Length) return Text.Length;
        int pos = from;
        while (pos < Text.Length && !char.IsWhiteSpace(Text[pos])) pos++;
        while (pos < Text.Length && char.IsWhiteSpace(Text[pos])) pos++;
        return pos;
    }

    private void HandleBackspace(bool word)
    {
        if (_selectionLength != 0)
        {
            DeleteSelection();
        }
        else if (CaretPosition > 0)
        {
            int deleteFrom = word ? FindPreviousWordBoundary(CaretPosition) : CaretPosition - 1;
            RemoveFromDocument(deleteFrom, CaretPosition - deleteFrom);
            CaretPosition = deleteFrom;
            NotifyTextChanged();
        }
    }

    private void HandleDelete(bool word)
    {
        if (_selectionLength != 0)
        {
            DeleteSelection();
        }
        else if (CaretPosition < Text.Length)
        {
            int deleteTo = word ? FindNextWordBoundary(CaretPosition) : CaretPosition + 1;
            RemoveFromDocument(CaretPosition, deleteTo - CaretPosition);
            NotifyTextChanged();
        }
    }

    private void DeleteSelection()
    {
        if (_selectionLength == 0) return;

        int start = Math.Min(_selectionStart, _selectionStart + _selectionLength);
        int length = Math.Abs(_selectionLength);

        RemoveFromDocument(start, length);
        CaretPosition = start;
        _selectionStart = start;
        _selectionLength = 0;
        NotifyTextChanged();
    }

    protected override void PasteFromClipboardCore()
    {
        if (!TryClipboardGetText(out var text) || string.IsNullOrEmpty(text))
            return;

        // Single-line: preserve separation by converting newlines/tabs to spaces.
        text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        if (!AcceptTab)
            text = text.Replace("\t", " ");

        InsertTextAtCaretForEdit(text);
    }

    private void EnsureCaretVisible()
    {
        var contentBounds = Bounds.Deflate(Padding);
        using var measure = BeginTextMeasurement();

        var caretX = CaretPosition > 0
            ? measure.Context.MeasureText(Text[..CaretPosition], measure.Font).Width
            : 0;

        if (caretX - _scrollOffset > contentBounds.Width - 5)
        {
            _scrollOffset = caretX - contentBounds.Width + 10;
        }
        else if (caretX - _scrollOffset < 5)
        {
            _scrollOffset = Math.Max(0, caretX - 10);
        }

        ClampScrollOffset(measure.Context, measure.Font, contentBounds.Width);
    }

    private void ClampScrollOffset()
    {
        if (string.IsNullOrEmpty(Text))
        {
            _scrollOffset = 0;
            return;
        }

        var contentBounds = Bounds.Deflate(Padding);
        using var measure = BeginTextMeasurement();
        ClampScrollOffset(measure.Context, measure.Font, contentBounds.Width);
    }

    private void ClampScrollOffset(IGraphicsContext context, IFont font, double viewportWidthDip)
    {
        if (string.IsNullOrEmpty(Text))
        {
            _scrollOffset = 0;
            return;
        }

        double textWidth = context.MeasureText(Text, font).Width;
        double maxOffset = Math.Max(0, textWidth - Math.Max(0, viewportWidthDip));
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);
    }

    private static bool IsNavigationKey(Input.Key key) =>
        key is Key.Left or
               Key.Right or
               Key.Home or
               Key.End;
}
