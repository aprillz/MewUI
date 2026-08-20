using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Controls;

public sealed partial class NumericUpDown : RangeBase
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<NumericUpDown>(DefaultStyles.CreateNumericUpDownStyle);

    /// <summary>Template part name for the editable text box; register a TextBox under this name to receive the edit pipeline.</summary>
    public const string PART_TEXT_BOX = "PART_TextBox";

    // Default-template-only part: the TextBlock shown while not editing. Not a public contract
    // because custom templates are free to omit it (see UpdateEditMode).
    internal const string PART_DISPLAY_TEXT = "PART_DisplayText";

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

    private static readonly MewPropertyKey<string> DisplayTextPropertyKey =
        MewProperty<string>.RegisterReadOnly<NumericUpDown>(nameof(DisplayText), "");

    public static readonly MewProperty<string> DisplayTextProperty = DisplayTextPropertyKey.Property;

    private void OnFormatChanged()
    {
        UpdateDisplayText();
        UpdateTextBoxFromValue();
    }

    private void OnIsIntegerChanged()
    {
        CoerceValue(ValueProperty);
        UpdateDisplayText();
        UpdateTextBoxFromValue();
    }

    protected override double OnCoerceValue(double value)
        => IsInteger ? Math.Round(value, MidpointRounding.AwayFromZero) : value;

    private double GetEffectiveStep()
        => IsInteger
            ? Math.Max(1, Math.Round(Step, MidpointRounding.AwayFromZero))
            : Step;

    /// <summary>Increases the value by one effective step.</summary>
    public void StepUp() => CommitValue(Value + GetEffectiveStep());

    /// <summary>Decreases the value by one effective step.</summary>
    public void StepDown() => CommitValue(Value - GetEffectiveStep());

    private TextBlock? _displayPart;
    private TextBox? _partTextBox;
    private bool _suppressTextBoxUpdate;
    private WheelNotchAccumulator _wheelAccumulator;

    static NumericUpDown()
    {
        FocusableProperty.OverrideDefaultValue<NumericUpDown>(true);
    }

    public NumericUpDown()
    {
        SetValue(DisplayTextPropertyKey, FormatValue(Value));
    }

    public static readonly MewProperty<bool> ChangeOnWheelProperty =
        MewProperty<bool>.Register<NumericUpDown>(nameof(ChangeOnWheel), true, MewPropertyOptions.None);

    public bool ChangeOnWheel
    {
        get => GetValue(ChangeOnWheelProperty);
        set => SetValue(ChangeOnWheelProperty, value);
    }

    /// <summary>
    /// True while the user (or programmatic call) is editing the value via the inline TextBox.
    /// Use <see cref="BeginEdit"/>, <see cref="CommitEdit"/>, <see cref="CancelEdit"/> to control.
    /// </summary>
    public bool IsEditing => GetValue(IsEditingProperty);

    /// <summary>Gets the formatted value text shown while not editing.</summary>
    public string DisplayText => GetValue(DisplayTextProperty);

    private void SetIsEditing(bool value) => SetValue(IsEditingPropertyKey, value);

    /// <summary>
    /// Enters edit mode: shows the TextBox, focuses it and selects all text.
    /// No-op if already editing, the control is disabled, or no editable TextBox part is attached.
    /// </summary>
    public void BeginEdit()
    {
        if (_partTextBox == null || IsEditing || !IsEffectivelyEnabled)
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
        if (_partTextBox == null || !IsEditing)
        {
            return;
        }

        if (TryParseTextBox(out var parsed))
        {
            _suppressTextBoxUpdate = true;
            try
            {
                CommitValue(parsed);
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

        _editEndedWhileFocused = IsFocusWithin;
        SetIsEditing(false);
    }

    /// <summary>
    /// Exits edit mode without committing the TextBox text. The displayed value is restored.
    /// </summary>
    public void CancelEdit()
    {
        if (_partTextBox == null || !IsEditing)
        {
            return;
        }

        UpdateTextBoxFromValue();
        _editEndedWhileFocused = IsFocusWithin;
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
        SyncTextBoxStyle();
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();

        if (_partTextBox != null)
        {
            _partTextBox.IsEnabled = IsEffectivelyEnabled;
        }
    }

    protected override void OnValueChanged(double value)
    {
        UpdateDisplayText();
        UpdateTextBoxFromValue();
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

        CommitValue(Value + notches * GetEffectiveStep());
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (!IsEditing)
        {
            Focus();
        }

        // Spinner presses never reach here: the RepeatButton parts take the hit and handle it.
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
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            StepDown();
            e.Handled = true;
        }
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();

        // Focus makes the value editable, as it does in every other numeric spin control. Not right after a
        // commit or a cancel though: those hand focus back here on their way out, and reopening the editor
        // would make ending an edit impossible without leaving the control.
        if (!_editEndedWhileFocused)
        {
            BeginEdit();
        }

        InvalidateVisual();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        if (!IsFocusWithin)
        {
            _editEndedWhileFocused = false;
            if (IsEditing)
            {
                CommitEdit();
            }
        }

        InvalidateVisual();
    }

    // Set while a commit or a cancel hands focus from the editor back to this control, so that focus does
    // not open the editor again. Cleared once focus leaves the control altogether.
    private bool _editEndedWhileFocused;

    private void UpdateDisplayText() => SetValue(DisplayTextPropertyKey, FormatValue(Value));

    private string FormatValue(double value) => value.ToString(Format);

    private void UpdateEditMode()
    {
        var textBox = _partTextBox;
        if (textBox == null)
        {
            return;
        }

        bool editing = IsEditing;

        // The default template's display TextBlock is the only part whose visibility this
        // control owns; a custom template without it keeps full author control (ctx.Bind, etc.).
        if (_displayPart != null)
        {
            // The display text stays visible (transparent) while editing so it keeps
            // participating in measure: the control's width follows the formatted value,
            // not the in-flight edit text, and clearing the editor cannot change it.
            _displayPart.Opacity = editing ? 0.0 : 1.0;
            textBox.IsVisible = editing;
            textBox.IsHitTestVisible = editing;
        }

        textBox.IsEnabled = IsEffectivelyEnabled;

        if (editing)
        {
            SyncTextBoxStyle();
            UpdateTextBoxFromValue();

            // The editor is this control's own part, so nothing has moved on screen: focusing it must not
            // ask the scroll hosts to bring it into view, which would jump the viewport off the click.
            if (FindVisualRoot() is Window window)
            {
                window.FocusManager.SetFocus(textBox, resolveDefault: false, bringIntoView: false);
            }
            else
            {
                textBox.Focus();
            }

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
    }

    private void SyncTextBoxStyle()
    {
        var textBox = _partTextBox;
        if (textBox == null)
        {
            return;
        }

        textBox.FontFamily = FontFamily;
        textBox.FontSize = FontSize;
        textBox.FontWeight = FontWeight;
        textBox.Foreground = Foreground;
    }

    private void UpdateTextBoxFromValue()
    {
        var textBox = _partTextBox;
        if (textBox == null || !IsEditing || _suppressTextBoxUpdate)
        {
            return;
        }

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
        var textBox = _partTextBox;
        if (textBox == null)
        {
            return false;
        }

        var text = textBox.Text;
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
                CommitValue(parsed);
            }
            finally
            {
                _suppressTextBoxUpdate = false;
            }
        }
    }

    /// <summary>
    /// Steps the value with the editor open, putting the stepped text in the editor first. Committing the
    /// value can reach the application, which may rebuild the tree this control sits in and take the focus
    /// away; the commit that focus loss triggers then parses text that already agrees with the new value
    /// instead of undoing the step.
    /// </summary>
    private void StepWhileEditing(double delta)
    {
        double next = Value + delta;
        var textBox = _partTextBox;
        if (textBox != null)
        {
            _suppressTextBoxUpdate = true;
            try
            {
                textBox.Text = FormatValue(next);
            }
            finally
            {
                _suppressTextBoxUpdate = false;
            }
        }

        CommitValue(next);
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
            StepWhileEditing(GetEffectiveStep());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            StepWhileEditing(-GetEffectiveStep());
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

        _displayPart = GetTemplateChild<TextBlock>(PART_DISPLAY_TEXT);

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

        _displayPart = null;

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

        base.OnDispose();
    }
}
