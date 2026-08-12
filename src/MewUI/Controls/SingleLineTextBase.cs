using System.Globalization;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for single-line text input controls (TextBox, PasswordBox) on the managed text engine.
/// </summary>
public abstract class SingleLineTextBase : TextBase
{
    // Keeps the caret fully visible with a little travel room at the right edge.
    private const double CARET_SLACK = 2;
    private const double DRAG_EDGE_DIP = 8;
    private const int MEASURE_SAMPLE_LIMIT = 64;

    // View pipeline kept private protected so PasswordBox can register its mask transformer.
    private protected readonly TextViewExtensionPipeline _extensions = new();
    private TextViewLayout? _view;
    private IGraphicsFactory? _viewFactory;
    private Rect _contentBounds;
    private double _horizontalOffset;
    private bool _dragSelecting;

    protected SingleLineTextBase()
    {
        _document.Changed += OnDocumentViewChanged;
    }

    public double HorizontalOffset => _horizontalOffset;

    private protected override string NormalizeTypedText(string text)
        => RemoveDisallowedChars(text, replaceWithSpace: false);

    private protected override string NormalizePastedText(string text)
        => RemoveDisallowedChars(text, replaceWithSpace: true);

    private protected override string NormalizeExternalText(string text)
        => RemoveDisallowedChars(text, replaceWithSpace: true);

    private string RemoveDisallowedChars(string text, bool replaceWithSpace)
    {
        bool rejectTab = !AcceptTab;
        bool hasDisallowed = false;
        foreach (char current in text)
        {
            if (current is '\r' or '\n' || (rejectTab && current == '\t'))
            {
                hasDisallowed = true;
                break;
            }
        }
        if (!hasDisallowed)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (char current in text)
        {
            if (current is '\r' or '\n' || (rejectTab && current == '\t'))
            {
                // Pasted/external text preserves word separation; typed control chars are dropped.
                if (replaceWithSpace && (builder.Length == 0 || builder[^1] != ' '))
                {
                    builder.Append(' ');
                }
                continue;
            }
            builder.Append(current);
        }
        return builder.ToString();
    }

    /// <summary>
    /// The text used for width measurement. Masking controls override to measure the masked shape.
    /// </summary>
    private protected virtual string GetMeasureSample() => GetTextSnapshot();

    protected override Size MeasureContent(Size availableSize)
    {
        double borderInset = GetBorderVisualInset();
        var engine = GetGraphicsFactory().TextEngine;
        var probe = engine.CreateLayout(CreateMeasureRequest("Mg"));
        double lineHeight = Math.Max(FontSize, probe.MeasuredSize.Height);

        string sample = GetMeasureSample();
        if (sample.Length == 0)
        {
            sample = Placeholder;
        }
        if (sample.Length > MEASURE_SAMPLE_LIMIT)
        {
            sample = sample[..MEASURE_SAMPLE_LIMIT];
        }
        if (sample.Length == 0)
        {
            sample = "MMMMMMMMMM";
        }
        double sampleWidth = engine.CreateLayout(CreateMeasureRequest(sample)).MeasuredSize.Width;

        return new Size(
            sampleWidth + Padding.HorizontalThickness + borderInset * 2,
            lineHeight + Padding.VerticalThickness + borderInset * 2);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        _contentBounds = GetEditorContentBounds();
        UpdateViewport();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, CornerRadius);
        _contentBounds = GetEditorContentBounds();

        context.Save();
        try
        {
            context.SetClip(LayoutRounding.MakeClipRect(_contentBounds, GetDpi() / 96.0));
            if (_document.TextLength == 0 && !string.IsNullOrEmpty(Placeholder) && !IsFocused)
            {
                DrawPlaceholder(context);
            }
            else
            {
                DrawDocument(context);
            }
        }
        finally
        {
            context.Restore();
        }
    }

    public override Rect GetCharRectInWindow(int charIndex)
    {
        EnsureView();
        if (_view is null)
        {
            return Rect.Empty;
        }
        var caret = _view.GetCaretBounds(Math.Clamp(charIndex, 0, _document.TextLength));
        double top = _contentBounds.Y + Math.Max(0, (_contentBounds.Height - caret.Height) / 2);
        return new Rect(_contentBounds.X + caret.X - _horizontalOffset, top, caret.Width, caret.Height);
    }

    private protected override void EnsureCaretVisible()
    {
        if (_contentBounds.IsEmpty)
        {
            return;
        }
        EnsureView();
        if (_view is null)
        {
            return;
        }
        var caret = _view.GetCaretBounds(_editor.CaretPosition);
        double horizontal = _horizontalOffset;
        if (caret.X < horizontal)
        {
            horizontal = caret.X;
        }
        else if (caret.X > horizontal + _contentBounds.Width - CARET_SLACK)
        {
            horizontal = caret.X - _contentBounds.Width + CARET_SLACK;
        }
        SetHorizontalOffset(horizontal);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }
        if (e.PrimaryKey && HandlePrimaryKey(e))
        {
            e.Handled = true;
            EnsureCaretVisible();
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                _editor.MoveLogical(LogicalDirection.Backward, e.ShiftKey, e.ControlKey);
                break;
            case Key.Right:
                _editor.MoveLogical(LogicalDirection.Forward, e.ShiftKey, e.ControlKey);
                break;
            case Key.Home:
                _editor.SetCaret(0, e.ShiftKey);
                break;
            case Key.End:
                _editor.SetCaret(_document.TextLength, e.ShiftKey);
                break;
            case Key.Backspace when !IsReadOnly:
                _editor.Backspace(e.ControlKey);
                break;
            case Key.Delete when !IsReadOnly:
                _editor.Delete(e.ControlKey);
                break;
            case Key.Tab when !IsReadOnly && AcceptTab:
                InsertText("\t");
                _suppressTabInput = true;
                break;
            default:
                return;
        }
        e.Handled = true;
        EnsureCaretVisible();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }
        Focus();
        SetCaretFromPoint(e.Position, e.ShiftKey);
        _dragSelecting = true;
        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }
        e.Handled = true;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }
        SetCaretFromPoint(e.Position, false);
        _editor.SelectWordAt(_editor.CaretPosition);
        EnsureCaretVisible();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragSelecting || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }
        AutoScroll(e.Position);
        SetCaretFromPoint(e.Position, true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left)
        {
            _dragSelecting = false;
            if (FindVisualRoot() is Window window)
            {
                window.ReleaseMouseCapture();
            }
        }
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id ||
            property.Id == FontSizeProperty.Id ||
            property.Id == FontWeightProperty.Id)
        {
            ResetView();
        }
        base.OnMewPropertyChanged(property);
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ResetView();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // Classifier paint spans are cached per materialized line; theme-dependent
        // classifiers must re-run against the new theme without losing scroll position.
        _extensions.Revision++;
        RebuildView();
    }

    protected override void OnDispose()
    {
        _view?.Dispose();
        _document.Changed -= OnDocumentViewChanged;
        base.OnDispose();
    }

    private void OnDocumentViewChanged(TextChange change) => _view?.Invalidate(change);

    private void EnsureView()
    {
        var factory = GetGraphicsFactory();
        if (_view is not null && ReferenceEquals(_viewFactory, factory))
        {
            return;
        }
        _view?.Dispose();
        _viewFactory = factory;
        _view = new TextViewLayout(
            factory.TextEngine,
            _document,
            new TextRunStyle(FontFamily, FontSize, FontWeight),
            new TextParagraphStyle
            {
                Wrapping = TextWrapping.NoWrap,
                Culture = CultureInfo.CurrentUICulture
            },
            _extensions,
            dpi: GetDpi());
    }

    private void UpdateViewport()
    {
        EnsureView();
        if (_view is null || _contentBounds.Width <= 0 || _contentBounds.Height <= 0)
        {
            return;
        }
        _view.SetViewport(new TextViewport(_contentBounds.Width, _contentBounds.Height, _horizontalOffset, 0));
        // The document may have shrunk under the offset, and the clamp lives in the setter. The
        // nested UpdateViewport call settles immediately because the re-set value no longer moves.
        SetHorizontalOffset(_horizontalOffset);
    }

    private Rect GetEditorContentBounds()
    {
        var snapped = GetSnappedBorderBounds(Bounds);
        double border = GetBorderVisualInset();
        return LayoutRounding.SnapViewportRectToPixels(
            snapped.Deflate(new Thickness(border)).Deflate(Padding),
            GetDpi() / 96.0);
    }

    private void DrawPlaceholder(IGraphicsContext context)
    {
        var layout = GetGraphicsFactory().TextEngine.GetOrCreateLayout(
            CreateMeasureRequest(Placeholder), TextLayoutCachePolicy.Owner, this);
        double top = _contentBounds.Y + Math.Max(0, (_contentBounds.Height - layout.MeasuredSize.Height) / 2);
        var options = new TextDrawOptions(Theme.Palette.PlaceholderText, Owner: this);
        context.Text.Draw(layout, new Point(_contentBounds.X, top), in options);
    }

    private void DrawDocument(IGraphicsContext context)
    {
        if (_view is null)
        {
            return;
        }
        var selection = _editor.Selection;
        foreach (var line in _view.MaterializedLines)
        {
            TextPaintSpan[] paint = CreatePaintSpans(line, selection);
            double lineHeight = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Height;
            double top = _contentBounds.Y + Math.Max(0, (_contentBounds.Height - lineHeight) / 2);
            var origin = new Point(_contentBounds.X - _horizontalOffset, top);
            var options = new TextDrawOptions(
                Foreground,
                paint,
                Owner: line);
            line.Draw(context.Text, origin, in options);
        }

        DrawCompositionUnderlines(context, _contentBounds.Right);

        if (IsFocused && CaretVisible)
        {
            var caret = GetCharRectInWindow(_editor.CaretPosition);
            context.FillRectangle(new Rect(caret.X, caret.Y, 1, Math.Max(1, caret.Height)), Foreground);
        }
    }

    private TextPaintSpan[] CreatePaintSpans(TextLineLayout line, TextRange selection)
    {
        var spans = new List<TextPaintSpan>(2);
        int lineStart = line.LogicalLine.Offset;
        int lineEnd = lineStart + line.LogicalLine.Length;
        if (TextSelectionPresentation.TryCreateSpan(
                line,
                selection,
                Theme.Palette.SelectionText,
                Theme.Palette.SelectionBackground,
                out var selectionSpan))
        {
            // Recoloring the glyphs re-segments the runs on every drag frame, so the default keeps
            // their colors and only SelectionForeground opts into the cost.
            spans.Add(selectionSpan with { Foreground = SelectionForeground });
        }

        if (_editor.IsComposing)
        {
            int compositionEnd = _compositionStart + _compositionLength;
            int start = Math.Max(_compositionStart, lineStart);
            int end = Math.Min(compositionEnd, lineEnd);
            if (end > start)
            {
                spans.Add(new TextPaintSpan(
                    new TextRange(start - lineStart, end - start),
                    Decoration: TextDecoration.Underline));
            }
        }
        return spans.ToArray();
    }

    private TextLayoutRequest CreateMeasureRequest(string text)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = GetDpi(),
            DefaultStyle = new TextRunStyle(FontFamily, FontSize, FontWeight),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap,
                Culture = CultureInfo.CurrentUICulture
            }
        };

    private void SetCaretFromPoint(Point point, bool extend)
    {
        EnsureView();
        if (_view is null)
        {
            return;
        }
        var hit = _view.HitTest(new Point(point.X - _contentBounds.X, Math.Max(0, point.Y - _contentBounds.Y)));
        _editor.SetCaret(hit.DocumentOffset, extend);
        EnsureCaretVisible();
    }

    private void AutoScroll(Point point)
    {
        if (point.X < _contentBounds.X + DRAG_EDGE_DIP)
        {
            SetHorizontalOffset(_horizontalOffset + point.X - (_contentBounds.X + DRAG_EDGE_DIP));
        }
        else if (point.X > _contentBounds.Right - DRAG_EDGE_DIP)
        {
            SetHorizontalOffset(_horizontalOffset + point.X - (_contentBounds.Right - DRAG_EDGE_DIP));
        }
    }

    private void SetHorizontalOffset(double value)
    {
        EnsureView();
        double extent = _view?.ExtentWidth ?? 0;
        double maximum = Math.Max(0, extent - _contentBounds.Width + CARET_SLACK);
        value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_horizontalOffset - value) < 0.001)
        {
            return;
        }
        _horizontalOffset = value;
        UpdateViewport();
        InvalidateVisual();
    }

    private void ResetView()
    {
        _horizontalOffset = 0;
        RebuildView();
    }

    private void RebuildView()
    {
        _view?.Dispose();
        _view = null;
        _viewFactory = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Re-runs the extension pipeline after a registration or its inputs change.</summary>
    private protected void InvalidateTextPipeline()
    {
        _extensions.Revision++;
        ResetView();
    }
}
