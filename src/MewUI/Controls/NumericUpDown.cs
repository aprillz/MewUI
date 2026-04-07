using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class NumericUpDown : RangeBase, IVisualTreeHost
{
    private enum ButtonPart
    {
        None,
        Decrement,
        Increment
    }

    public static readonly MewProperty<string> FormatProperty =
        MewProperty<string>.Register<NumericUpDown>(nameof(Format), "0.##", MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnFormatChanged());

    public static readonly MewProperty<double> StepProperty =
        MewProperty<double>.Register<NumericUpDown>(nameof(Step), 1.0, MewPropertyOptions.None);

    public static readonly MewProperty<bool> EditModeProperty =
        MewProperty<bool>.Register<NumericUpDown>(nameof(EditMode), false, MewPropertyOptions.None,
            static (self, _, _) => self.OnEditModeChanged());

    private void OnFormatChanged()
    {
        _measureCache.Invalidate();
        UpdateTextBoxFromValue();
    }

    private void OnEditModeChanged() => UpdateEditMode();

    private TextMeasureCache _measureCache;
    private ButtonPart _hoverPart;
    private ButtonPart _pressedPart;
    private readonly TextBox _textBox;
    private bool _suppressTextBoxUpdate;

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();

        if (_pressedPart != ButtonPart.None)
            return state with { Flags = state.Flags | VisualStateFlags.Pressed };
        return state;
    }

    public NumericUpDown()
    {
        _textBox = new TextBox
        {
            BorderThickness = 0,
            Padding = new Thickness(0),
            Background = Color.Transparent,
            MinHeight = 0,
            IsVisible = false,
            IsHitTestVisible = false,
            ImeMode = Input.ImeMode.Disabled
        };
        _textBox.TextChanged += OnTextBoxTextChanged;
        _textBox.KeyDown += OnTextBoxKeyDown;
        _textBox.LostFocus += OnTextBoxLostFocus;

        AttachChild(_textBox);
    }

    public static readonly MewProperty<bool> ChangeOnWheelProperty =
        MewProperty<bool>.Register<NumericUpDown>(nameof(ChangeOnWheel), true, MewPropertyOptions.None);

    public bool ChangeOnWheel
    {
        get => GetValue(ChangeOnWheelProperty);
        set => SetValue(ChangeOnWheelProperty, value);
    }

    public override bool Focusable => true;

    internal override void OnAccessKey() => Focus();

    public bool EditMode
    {
        get => GetValue(EditModeProperty);
        set => SetValue(EditModeProperty, value);
    }

    public string Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _measureCache.Invalidate();
        SyncTextBoxStyle();
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        _textBox.IsEnabled = IsEffectivelyEnabled;
    }

    protected override void OnValueChanged(double value)
    {
        _measureCache.Invalidate();
        InvalidateMeasure();
        UpdateTextBoxFromValue();
    }

    protected override Size MeasureContent(Size available)
    {
        var factory = GetGraphicsFactory();
        var font = GetFont(factory);
        string text = GetDisplayText();
        var textSize = _measureCache.Measure(factory, GetDpi(), font, text, TextWrapping.NoWrap, 0);

        double buttonAreaWidth = GetButtonAreaWidth();
        double width = textSize.Width + Padding.HorizontalThickness + buttonAreaWidth;
        double height = textSize.Height + Padding.VerticalThickness;
        return new Size(width, height).Inflate(new Thickness(GetBorderVisualInset()));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        var inner = GetSnappedBorderBounds(bounds).Deflate(new Thickness(GetBorderVisualInset()));
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var textRect = new Rect(inner.X + Padding.Left, inner.Y + Padding.Top,
            Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
            Math.Max(0, inner.Height - Padding.VerticalThickness));

        textRect = LayoutRounding.SnapBoundsRectToPixels(textRect, GetDpi() / 96.0);
        _textBox.Arrange(textRect);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        _textBox.IsEnabled = IsEffectivelyEnabled;

        double radius = CornerRadius;

        var state = CurrentVisualState;
        bool isEnabled = state.IsEnabled;
        Color bg = GetValue(BackgroundProperty);
        Color border = GetValue(BorderBrushProperty);

        var metrics = GetBorderRenderMetrics(Bounds, BorderThickness, radius);
        var bounds = metrics.Bounds;
        var borderInset = metrics.UniformThickness;
        var cornerRadius = metrics.UniformRadius;

        DrawBackgroundAndBorder(context, bounds, bg, border, BorderThickness, radius);

        var inner = bounds.Deflate(new Thickness(borderInset));

        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height);
        var textRect = new Rect(inner.X + Padding.Left, inner.Y + Padding.Top,
            Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
            Math.Max(0, inner.Height - Padding.VerticalThickness));

        textRect = LayoutRounding.SnapBoundsRectToPixels(textRect, context.DpiScale);
        buttonRect = LayoutRounding.SnapBoundsRectToPixels(buttonRect, context.DpiScale);

        (var decRect, var incRect) = GetButtonRects();

        Color baseButton = Theme.Palette.ButtonFace;
        Color hoverButton = Color.Composite(baseButton, Theme.Palette.AccentHoverOverlay);
        Color pressedButton = Color.Composite(baseButton, Theme.Palette.AccentPressedOverlay);
        Color disabledButton = Theme.Palette.ButtonDisabledBackground;

        Color decBg = !isEnabled
            ? disabledButton
            : _pressedPart == ButtonPart.Decrement ? pressedButton
            : _hoverPart == ButtonPart.Decrement ? hoverButton
            : baseButton;

        Color incBg = !isEnabled
            ? disabledButton
            : _pressedPart == ButtonPart.Increment ? pressedButton
            : _hoverPart == ButtonPart.Increment ? hoverButton
            : baseButton;

        if (buttonRect.Width > 0)
        {
            var innerRadius = metrics.UniformInnerRadius;
            context.Save();
            context.SetClipRoundedRect(
                inner,
                innerRadius,
                innerRadius);

            context.FillRectangle(decRect, decBg);
            context.FillRectangle(incRect, incBg);

            if (BorderThickness > 0)
            {
                context.DrawLine(new Point(buttonRect.Left, buttonRect.Y), new Point(buttonRect.Left, buttonRect.Bottom), Theme.Palette.ControlBorder, BorderThickness, pixelSnap: true);
            }

            context.Restore();
        }

        var font = GetFont();
        var textColor = isEnabled ? Foreground : Theme.Palette.DisabledText;
        if (!EditMode)
        {
            context.DrawText(GetDisplayText(), textRect, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }

        if (buttonRect.Width > 0)
        {
            var chevronSize = Theme.Metrics.BaseControlHeight / 8;
            Glyph.Draw(context, decRect.Center, chevronSize, textColor, GlyphKind.ChevronDown);
            Glyph.Draw(context, incRect.Center, chevronSize, textColor, GlyphKind.ChevronUp);
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (EditMode)
        {
            _textBox.Render(context);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEffectivelyEnabled || !ChangeOnWheel)
        {
            return;
        }


        double delta = e.Delta > 0 ? Step : -Step;
        Value += delta;
        e.Handled = true;
        UpdateTextBoxFromValue();
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (!EditMode)
        {
            Focus();
        }

        var part = HitTestButtonPart(e.Position);
        if (part == ButtonPart.None)
        {
            if (!EditMode)
            {
                EditMode = true;
            }
            return;
        }

        _pressedPart = part;
        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var part = HitTestButtonPart(e.Position);
        if (_hoverPart != part)
        {
            _hoverPart = part;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (_hoverPart != ButtonPart.None && !IsMouseCaptured)
        {
            _hoverPart = ButtonPart.None;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButton.Left || _pressedPart == ButtonPart.None)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        var releasedPart = HitTestButtonPart(e.Position);
        if (releasedPart == _pressedPart && IsEffectivelyEnabled)
        {
            Value += _pressedPart == ButtonPart.Increment ? Step : -Step;
            UpdateTextBoxFromValue();
        }

        _pressedPart = ButtonPart.None;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (EditMode)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            Value += Step;
            UpdateTextBoxFromValue();
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Value -= Step;
            UpdateTextBoxFromValue();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        InvalidateVisual();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        if (!IsFocusWithin && EditMode)
        {
            CommitEdit();
            EditMode = false;
        }
        InvalidateVisual();
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        if (EditMode)
        {
            var hit = _textBox.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return base.OnHitTest(point);
    }

    private double GetButtonAreaWidth() => (Theme.Metrics.BaseControlHeight - Theme.Metrics.ControlBorderThickness * 2);

    private (Rect decRect, Rect incRect) GetButtonRects()
    {
        var inner = GetSnappedBorderBounds(Bounds).Deflate(new Thickness(GetBorderVisualInset()));
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height);
        var incRect = new Rect(buttonRect.X, buttonRect.Y, buttonRect.Width, buttonRect.Height / 2);
        var decRect = new Rect(buttonRect.X, buttonRect.Y + buttonRect.Height / 2, buttonRect.Width, buttonRect.Height / 2);
        return (decRect, incRect);
    }

    private ButtonPart HitTestButtonPart(Point position)
    {
        var (decRect, incRect) = GetButtonRects();
        if (decRect.Contains(position))
        {
            return ButtonPart.Decrement;
        }
        if (incRect.Contains(position))
        {
            return ButtonPart.Increment;
        }
        return ButtonPart.None;
    }

    private string GetDisplayText()
    {
        if (EditMode)
        {
            var text = _textBox.Text;
            return string.IsNullOrEmpty(text) ? FormatValue(Value) : text;
        }

        return FormatValue(Value);
    }

    private string FormatValue(double value) => value.ToString(Format);

    private void CommitEdit()
    {
        if (!EditMode)
        {
            return;
        }

        if (TryParseTextBox(out var parsed))
        {
            _suppressTextBoxUpdate = true;
            try
            {
                Value = parsed;
            }
            finally
            {
                _suppressTextBoxUpdate = false;
            }
        }
        else
        {
            UpdateTextBoxFromValue();
        }
    }

    private void UpdateEditMode()
    {
        _textBox.IsVisible = EditMode;
        _textBox.IsHitTestVisible = EditMode;
        _textBox.IsEnabled = IsEffectivelyEnabled;

        if (EditMode)
        {
            SyncTextBoxStyle();
            UpdateTextBoxFromValue();
            _textBox.Focus();
            _textBox.SelectAll();
        }
        else
        {
            var root = FindVisualRoot();
            if (root is Window window && window.FocusManager.FocusedElement == _textBox)
            {
                window.FocusManager.SetFocus(this);
            }
        }

        _measureCache.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SyncTextBoxStyle()
    {
        _textBox.FontFamily = FontFamily;
        _textBox.FontSize = FontSize;
        _textBox.FontWeight = FontWeight;
        _textBox.Foreground = Foreground;
    }

    private void UpdateTextBoxFromValue()
    {
        if (!EditMode || _suppressTextBoxUpdate)
        {
            return;
        }

        var formatted = FormatValue(Value);
        if (_textBox.Text == formatted)
        {
            return;
        }

        _suppressTextBoxUpdate = true;
        try
        {
            _textBox.Text = formatted;
        }
        finally
        {
            _suppressTextBoxUpdate = false;
        }
    }

    private bool TryParseTextBox(out double value)
    {
        value = 0;
        var text = _textBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return double.TryParse(text, out value);
    }

    private void OnTextBoxTextChanged(string _)
    {
        if (_suppressTextBoxUpdate || !EditMode)
        {
            return;
        }

        if (TryParseTextBox(out var parsed))
        {
            _suppressTextBoxUpdate = true;
            try
            {
                Value = parsed;
            }
            finally
            {
                _suppressTextBoxUpdate = false;
            }
        }

        _measureCache.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnTextBoxKeyDown(KeyEventArgs e)
    {
        if (!EditMode)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitEdit();
            EditMode = false;
            Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            UpdateTextBoxFromValue();
            EditMode = false;
            Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            Value += Step;
            UpdateTextBoxFromValue();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            Value -= Step;
            UpdateTextBoxFromValue();
            e.Handled = true;
        }
    }

    private void OnTextBoxLostFocus()
    {
        if (!IsFocusWithin)
        {
            CommitEdit();
            EditMode = false;
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_textBox);

    protected override void OnDispose()
    {
        _textBox.TextChanged -= OnTextBoxTextChanged;
        _textBox.KeyDown -= OnTextBoxKeyDown;
        _textBox.LostFocus -= OnTextBoxLostFocus;
        DetachChild(_textBox);
        _textBox.Dispose();
        base.OnDispose();
    }
}
