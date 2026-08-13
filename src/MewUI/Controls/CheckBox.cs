using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A checkbox control with optional content label.
/// </summary>
public partial class CheckBox : CommandSourceControl
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<CheckBox>(DefaultStyles.CreateCheckBoxStyle);

    public static readonly MewProperty<bool?> IsCheckedProperty =
        MewProperty<bool?>.Register<CheckBox>(nameof(IsChecked), (bool?)false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, oldValue, newValue) => self.OnIsCheckedChanged(oldValue, newValue));

    public static readonly MewProperty<bool> IsThreeStateProperty =
        MewProperty<bool>.Register<CheckBox>(nameof(IsThreeState), false, MewPropertyOptions.None);

    private readonly PressCaptureHelper _pressCapture;

    static CheckBox()
    {
        FocusableProperty.OverrideDefaultValue<CheckBox>(true);
    }

    public CheckBox()
    {
        _pressCapture = new PressCaptureHelper(this, SetPressed);
    }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsChecked == true)
        {
            return state with { Flags = state.Flags | VisualStateFlags.Checked };
        }
        if (IsChecked == null)
        {
            return state with { Flags = state.Flags | VisualStateFlags.Indeterminate };
        }
        return state;
    }

    /// <summary>
    /// Gets or sets whether the checkbox supports indeterminate state.
    /// </summary>
    public bool IsThreeState
    {
        get => GetValue(IsThreeStateProperty);
        set => SetValue(IsThreeStateProperty, value);
    }

    /// <summary>
    /// Gets or sets the checked state.
    /// </summary>
    public bool? IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    internal override void OnAccessKey()
    {
        Focus(); 
        Toggle();
    }

    protected virtual void OnIsCheckedChanged(bool? oldValue, bool? newValue)
    {
        CheckedChanged?.Invoke(newValue);
    }

    /// <summary>
    /// Occurs when the checked state changes.
    /// </summary>
    public event Action<bool?>? CheckedChanged;

    /// <summary>
    /// Toggles the checked state for a user activation, respecting three-state mode, and invokes
    /// the command once. Setting <see cref="IsChecked"/> directly executes nothing.
    /// </summary>
    internal void Toggle()
    {
        if (IsThreeState)
        {
            CommitTargetValue(IsCheckedProperty, IsChecked switch
            {
                false => true,
                true => (bool?)null,
                _ => false
            });
        }
        else
        {
            CommitTargetValue(IsCheckedProperty, IsChecked != true);
        }

        InvokeCommand();
    }

    protected override bool ComputeIsEnabledSuggestion() => QueryCommandCanExecute();

    private const double BoxSize = 14;
    private const double Spacing = 6;

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

        double width = BoxSize + Spacing;
        double height = BoxSize;

        var displayed = EffectiveContent;
        if (displayed != null)
        {
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - width - Padding.HorizontalThickness),
                double.PositiveInfinity);
            displayed.Measure(contentAvailable);
            width += displayed.DesiredSize.Width;
            height = Math.Max(height, displayed.DesiredSize.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (HasTemplateInstance)
        {
            base.ArrangeContent(bounds);
            return;
        }

        var displayed = EffectiveContent;
        if (displayed == null)
        {
            return;
        }

        var contentBounds = bounds.Deflate(Padding);
        var textBounds = new Rect(
            contentBounds.X + BoxSize + Spacing,
            contentBounds.Y,
            Math.Max(0, contentBounds.Width - BoxSize - Spacing),
            contentBounds.Height);
        displayed.Arrange(textBounds);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // A template owns the control's entire visuals; the built-in box would double-render.
        if (HasTemplateInstance)
        {
            return;
        }

        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = CurrentVisualState;

        double boxY = contentBounds.Y + (contentBounds.Height - BoxSize) / 2;
        var boxRect = new Rect(contentBounds.X, boxY, BoxSize, BoxSize);

        var fill = GetValue(BackgroundProperty);
        var radius = Math.Max(0, CornerRadius * 0.5);

        var borderColor = GetValue(BorderBrushProperty);
        DrawBackgroundAndBorder(context, boxRect, fill, borderColor, BorderThickness, radius);

        var markColor = state.IsEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;

        if (IsChecked == true)
        {
            var center = new Point(boxRect.X + boxRect.Width / 2, boxRect.Y + boxRect.Height / 2);
            double glyphSize = (BoxSize - 6) / 2;
            Glyph.Draw(context, center, glyphSize, markColor, GlyphKind.CheckMark, 2);
        }
        else if (IsChecked == null)
        {
            var center = new Point(boxRect.X + boxRect.Width / 2, boxRect.Y + boxRect.Height / 2);
            double glyphSize = (BoxSize - 6) / 2;
            Glyph.Draw(context, center, glyphSize, markColor, GlyphKind.IndeterminateMark, 2);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        _pressCapture.BeginPress(() => Focus());

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        _pressCapture.EndPress();

        if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
        {
            Toggle();
        }

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            Toggle();
            e.Handled = true;
        }
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
    }
}
