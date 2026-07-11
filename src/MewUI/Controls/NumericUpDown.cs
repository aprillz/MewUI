using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class NumericUpDown : RangeBase, IVisualTreeHost
{
    /// <summary>Template part name for the editable text box; register a TextBox under this name to receive the edit pipeline.</summary>
    public const string PART_TEXT_BOX = "PART_TextBox";

    public static readonly MewProperty<string> FormatProperty =
        MewProperty<string>.Register<NumericUpDown>(nameof(Format), "0.##", MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnFormatChanged());

    public static readonly MewProperty<double> StepProperty =
        MewProperty<double>.Register<NumericUpDown>(nameof(Step), 1.0, MewPropertyOptions.None);

    public static readonly MewProperty<bool> IsIntegerProperty =
        MewProperty<bool>.Register<NumericUpDown>(nameof(IsInteger), false,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnIsIntegerChanged());

    private static readonly MewPropertyKey<bool> IsEditingPropertyKey =
        MewProperty<bool>.RegisterReadOnly<NumericUpDown>(nameof(IsEditing), false,
            MewPropertyOptions.None,
            static (self, _, _) => self.UpdateEditMode());

    public static readonly MewProperty<bool> IsEditingProperty = IsEditingPropertyKey.Property;

    private void OnFormatChanged()
    {
        _measureCache.Invalidate();
        UpdateTextBoxFromValue();
    }

    private void OnIsIntegerChanged()
    {
        CoerceValue(ValueProperty);
        _measureCache.Invalidate();
        UpdateTextBoxFromValue();
    }

    protected override double OnCoerceValue(double value)
        => IsInteger ? Math.Round(value, MidpointRounding.AwayFromZero) : value;

    private double GetEffectiveStep()
        => IsInteger
            ? Math.Max(1, Math.Round(Step, MidpointRounding.AwayFromZero))
            : Step;

    /// <summary>Increases the value by one effective step.</summary>
    public void StepUp() => Value += GetEffectiveStep();

    /// <summary>Decreases the value by one effective step.</summary>
    public void StepDown() => Value -= GetEffectiveStep();

    private TextMeasureCache _measureCache;
    private string? _cachedDisplayText;
    private double _cachedDisplayValue = double.NaN;
    private string? _cachedDisplayFormat;
    private readonly TextBox _textBox;
    private readonly RepeatButton _decrementButton;
    private readonly RepeatButton _incrementButton;
    private TextBox? _partTextBox;
    private bool _suppressTextBoxUpdate;
    private WheelNotchAccumulator _wheelAccumulator;

    private TextBox ActiveTextBox
    {
        get
        {
            // The template's PART_TextBox overrides the built-in text box when attached.
            return _partTextBox ?? _textBox;
        }
    }

    static NumericUpDown()
    {
        FocusableProperty.OverrideDefaultValue<NumericUpDown>(true);
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
            // Focus enters via SetIsEditing, not Tab; keeps the control a single tab stop while editing.
            IsTabStop = false,
            ImeMode = Input.ImeMode.Disabled
        };
        _textBox.TextChanged += OnTextBoxTextChanged;
        _textBox.KeyDown += OnTextBoxKeyDown;
        _textBox.LostFocus += OnTextBoxLostFocus;

        _decrementButton = CreateSpinnerButton(GlyphKind.ChevronDown);
        _incrementButton = CreateSpinnerButton(GlyphKind.ChevronUp);
        _decrementButton.Click += OnDecrementClick;
        _incrementButton.Click += OnIncrementClick;

        AttachChild(_textBox);
        AttachChild(_decrementButton);
        AttachChild(_incrementButton);
    }

    private static RepeatButton CreateSpinnerButton(GlyphKind glyphKind)
        => new()
        {
            // Spinner parts must not join the tab order or steal focus from the control.
            Focusable = false,
            IsTabStop = false,
            BorderThickness = 0,
            CornerRadius = 0,
            Padding = new Thickness(0),
            MinHeight = 0,
            Content = new GlyphElement { Kind = glyphKind },
        };

    private void OnIncrementClick()
    {
        if (!IsEditing)
        {
            Focus();
        }

        StepUp();
    }

    private void OnDecrementClick()
    {
        if (!IsEditing)
        {
            Focus();
        }

        StepDown();
    }

    public static readonly MewProperty<bool> ChangeOnWheelProperty =
        MewProperty<bool>.Register<NumericUpDown>(nameof(ChangeOnWheel), true, MewPropertyOptions.None);

    public bool ChangeOnWheel
    {
        get => GetValue(ChangeOnWheelProperty);
        set => SetValue(ChangeOnWheelProperty, value);
    }

    internal override void OnAccessKey() => Focus();

    /// <summary>
    /// True while the user (or programmatic call) is editing the value via the inline TextBox.
    /// Use <see cref="BeginEdit"/>, <see cref="CommitEdit"/>, <see cref="CancelEdit"/> to control.
    /// </summary>
    public bool IsEditing => GetValue(IsEditingProperty);

    private void SetIsEditing(bool value) => SetValue(IsEditingPropertyKey, value);

    /// <summary>
    /// Enters edit mode: shows the TextBox, focuses it and selects all text.
    /// No-op if already editing or the control is disabled.
    /// </summary>
    public void BeginEdit()
    {
        if (IsEditing || !IsEffectivelyEnabled)
        {
            return;
        }

        SetIsEditing(true);
    }

    /// <summary>
    /// Parses the TextBox text into <see cref="RangeBase.Value"/> and exits edit mode.
    /// If the text cannot be parsed, the displayed value is restored.
    /// </summary>
    public void CommitEdit()
    {
        if (!IsEditing)
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

        SetIsEditing(false);
    }

    /// <summary>
    /// Exits edit mode without committing the TextBox text. The displayed value is restored.
    /// </summary>
    public void CancelEdit()
    {
        if (!IsEditing)
        {
            return;
        }

        UpdateTextBoxFromValue();
        SetIsEditing(false);
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

    /// <summary>
    /// When true, <see cref="RangeBase.Value"/> is rounded to the nearest whole number
    /// on every assignment and the effective step is at least 1. Default is false.
    /// </summary>
    public bool IsInteger
    {
        get => GetValue(IsIntegerProperty);
        set => SetValue(IsIntegerProperty, value);
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
        ActiveTextBox.IsEnabled = IsEffectivelyEnabled;
        _decrementButton.IsEnabled = IsEffectivelyEnabled;
        _incrementButton.IsEnabled = IsEffectivelyEnabled;
    }

    protected override void OnValueChanged(double value)
    {
        _measureCache.Invalidate();
        InvalidateMeasure();
        UpdateTextBoxFromValue();
    }

    protected override Size MeasureContent(Size available)
    {
        if (HasTemplateInstance)
        {
            return base.MeasureContent(available);
        }

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

        if (HasTemplateInstance)
        {
            return;
        }

        var inner = GetSnappedBorderBounds(bounds).Deflate(new Thickness(GetBorderVisualInset()));
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var textRect = new Rect(inner.X + Padding.Left, inner.Y + Padding.Top,
            Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
            Math.Max(0, inner.Height - Padding.VerticalThickness));

        textRect = LayoutRounding.SnapBoundsRectToPixels(textRect, GetDpi() / 96.0);
        _textBox.Arrange(textRect);

        var (decRect, incRect) = GetButtonRects();
        _decrementButton.Arrange(decRect);
        _incrementButton.Arrange(incRect);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (HasTemplateInstance)
        {
            return;
        }

        double radius = CornerRadius;

        var state = CurrentVisualState;
        bool isEnabled = state.IsEnabled;
        Color bg = GetValue(BackgroundProperty);
        Color border = GetValue(BorderBrushProperty);

        var metrics = GetBorderRenderMetrics(Bounds, BorderThickness, radius);
        var bounds = metrics.Bounds;
        var borderInset = metrics.UniformThickness;

        DrawBackgroundAndBorder(context, bounds, bg, border, BorderThickness, radius);

        var inner = bounds.Deflate(new Thickness(borderInset));

        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var textRect = new Rect(inner.X + Padding.Left, inner.Y + Padding.Top,
            Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
            Math.Max(0, inner.Height - Padding.VerticalThickness));

        textRect = LayoutRounding.SnapBoundsRectToPixels(textRect, context.DpiScale);

        var font = GetFont();
        var textColor = isEnabled ? Foreground : Theme.Palette.DisabledText;
        if (!IsEditing)
        {
            context.DrawText(GetDisplayText(), textRect, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (HasTemplateInstance)
        {
            base.RenderSubtree(context);
            return;
        }

        if (_incrementButton.Bounds.Width > 0)
        {
            var metrics = GetBorderRenderMetrics(Bounds, BorderThickness, CornerRadius);
            var inner = metrics.Bounds.Deflate(new Thickness(metrics.UniformThickness));

            // Clip the square spinner buttons to the rounded inner chrome so their corners stay inside.
            context.Save();
            context.SetClipRoundedRect(inner, metrics.UniformInnerRadius, metrics.UniformInnerRadius);

            _decrementButton.Render(context);
            _incrementButton.Render(context);

            if (BorderThickness > 0)
            {
                double separatorX = _incrementButton.Bounds.Left;
                context.DrawLine(new Point(separatorX, inner.Y), new Point(separatorX, inner.Bottom), Theme.Palette.ControlBorder, BorderThickness, pixelSnap: true);
            }

            context.Restore();
        }

        if (IsEditing)
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

        int notches = _wheelAccumulator.TakeY(e.Delta.Y);
        if (notches == 0)
        {
            e.Handled = true;
            return;
        }

        Value += notches * GetEffectiveStep();
        e.Handled = true;
        UpdateTextBoxFromValue();
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (HasTemplateInstance)
        {
            return;
        }

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (!IsEditing)
        {
            Focus();
        }

        // Spinner presses never reach here: the RepeatButton children take the hit and handle it.
        BeginEdit();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (IsEditing)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            StepUp();
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            StepDown();
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
        if (!IsFocusWithin && IsEditing)
        {
            CommitEdit();
        }
        InvalidateVisual();
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (HasTemplateInstance)
        {
            return base.OnHitTest(point);
        }

        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (_incrementButton.HitTest(point) is UIElement incrementHit)
        {
            return incrementHit;
        }
        if (_decrementButton.HitTest(point) is UIElement decrementHit)
        {
            return decrementHit;
        }

        if (IsEditing)
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

    private string GetDisplayText()
    {
        if (IsEditing)
        {
            var text = ActiveTextBox.Text;
            return string.IsNullOrEmpty(text) ? FormatValue(Value) : text;
        }

        var v = Value;
        var fmt = Format;
        if (_cachedDisplayText != null && _cachedDisplayValue == v && _cachedDisplayFormat == fmt)
            return _cachedDisplayText;

        _cachedDisplayText = FormatValue(v);
        _cachedDisplayValue = v;
        _cachedDisplayFormat = fmt;
        return _cachedDisplayText;
    }

    private string FormatValue(double value) => value.ToString(Format);

    private void UpdateEditMode()
    {
        bool editing = IsEditing;
        var textBox = ActiveTextBox;

        // The part's IsVisible/IsHitTestVisible belong to the template author (e.g. ctx.Bind to
        // IsEditingProperty); only the built-in text box toggles them itself.
        if (_partTextBox == null)
        {
            textBox.IsVisible = editing;
            textBox.IsHitTestVisible = editing;
        }
        textBox.IsEnabled = IsEffectivelyEnabled;

        if (editing)
        {
            SyncTextBoxStyle();
            UpdateTextBoxFromValue();
            textBox.Focus();
            textBox.SelectAll();
        }
        else
        {
            var root = FindVisualRoot();
            if (root is Window window && window.FocusManager.FocusedElement == textBox)
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
        var textBox = ActiveTextBox;
        textBox.FontFamily = FontFamily;
        textBox.FontSize = FontSize;
        textBox.FontWeight = FontWeight;
        textBox.Foreground = Foreground;
    }

    private void UpdateTextBoxFromValue()
    {
        if (!IsEditing || _suppressTextBoxUpdate)
        {
            return;
        }

        var textBox = ActiveTextBox;
        var formatted = FormatValue(Value);
        if (textBox.Text == formatted)
        {
            return;
        }

        _suppressTextBoxUpdate = true;
        try
        {
            textBox.Text = formatted;
        }
        finally
        {
            _suppressTextBoxUpdate = false;
        }
    }

    private bool TryParseTextBox(out double value)
    {
        value = 0;
        var text = ActiveTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return double.TryParse(text, out value);
    }

    private void OnTextBoxTextChanged(string _)
    {
        if (_suppressTextBoxUpdate || !IsEditing)
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
        if (!IsEditing)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitEdit();
            Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelEdit();
            Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            Value += GetEffectiveStep();
            UpdateTextBoxFromValue();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            Value -= GetEffectiveStep();
            UpdateTextBoxFromValue();
            e.Handled = true;
        }
    }

    private void OnTextBoxLostFocus()
    {
        if (!IsFocusWithin)
        {
            CommitEdit();
        }
    }

    private void OnPartTextBoxGotFocus() => BeginEdit();

    private protected override void OnTemplateInstanceAttached()
    {
        base.OnTemplateInstanceAttached();

        var part = GetTemplateChild<TextBox>(PART_TEXT_BOX);
        if (part == null)
        {
            return;
        }

        _partTextBox = part;
        part.TextChanged += OnTextBoxTextChanged;
        part.KeyDown += OnTextBoxKeyDown;
        part.LostFocus += OnTextBoxLostFocus;
        part.GotFocus += OnPartTextBoxGotFocus;

        part.IsEnabled = IsEffectivelyEnabled;
        SyncTextBoxStyle();
        UpdateTextBoxFromValue();
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        var part = _partTextBox;
        if (part == null)
        {
            return;
        }

        if (IsEditing)
        {
            CommitEdit();
        }

        part.TextChanged -= OnTextBoxTextChanged;
        part.KeyDown -= OnTextBoxKeyDown;
        part.LostFocus -= OnTextBoxLostFocus;
        part.GotFocus -= OnPartTextBoxGotFocus;
        _partTextBox = null;

        // Restore the built-in text box's visibility/enabled state now that it is active again.
        UpdateEditMode();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var templateRoot = TemplateVisualRoot;
        if (templateRoot != null)
        {
            return visitor(templateRoot);
        }

        return visitor(_textBox) && visitor(_decrementButton) && visitor(_incrementButton);
    }

    protected override void OnDispose()
    {
        var part = _partTextBox;
        if (part != null)
        {
            part.TextChanged -= OnTextBoxTextChanged;
            part.KeyDown -= OnTextBoxKeyDown;
            part.LostFocus -= OnTextBoxLostFocus;
            part.GotFocus -= OnPartTextBoxGotFocus;
            _partTextBox = null;
        }

        _textBox.TextChanged -= OnTextBoxTextChanged;
        _textBox.KeyDown -= OnTextBoxKeyDown;
        _textBox.LostFocus -= OnTextBoxLostFocus;
        DetachChild(_textBox);
        _textBox.Dispose();

        _decrementButton.Click -= OnDecrementClick;
        _incrementButton.Click -= OnIncrementClick;
        DetachChild(_decrementButton);
        DetachChild(_incrementButton);
        _decrementButton.Dispose();
        _incrementButton.Dispose();

        base.OnDispose();
    }
}

