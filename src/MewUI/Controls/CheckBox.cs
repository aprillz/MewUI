using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Styling;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A checkbox control with optional text label.
/// </summary>
public partial class CheckBox : Control
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<CheckBox>(nameof(Text), string.Empty, MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnTextChanged(oldValue, newValue));

    public static readonly MewProperty<bool?> IsCheckedProperty =
        MewProperty<bool?>.Register<CheckBox>(nameof(IsChecked), (bool?)false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, oldValue, newValue) => self.OnIsCheckedChanged(oldValue, newValue));

    public static readonly MewProperty<bool> IsThreeStateProperty =
        MewProperty<bool>.Register<CheckBox>(nameof(IsThreeState), false, MewPropertyOptions.None);

    private TextMeasureCache _textMeasureCache;

    public override bool Focusable => true;

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
    /// Gets or sets the checkbox label text.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
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

    private void OnTextChanged(string oldValue, string newValue)
    {
        _textMeasureCache.Invalidate();
    }

    protected virtual void OnIsCheckedChanged(bool? oldValue, bool? newValue)
    {
        CheckedChanged?.Invoke(newValue);
    }

    /// <summary>
    /// Occurs when the checked state changes.
    /// </summary>
    public event Action<bool?>? CheckedChanged;

    protected override Size MeasureContent(Size availableSize)
    {
        const double boxSize = 14;
        const double spacing = 6;

        double width = boxSize + spacing;
        double height = boxSize;

        if (!string.IsNullOrEmpty(Text))
        {
            var factory = GetGraphicsFactory();
            var font = GetFont(factory);
            var size = _textMeasureCache.Measure(factory, GetDpi(), font, Text, TextWrapping.NoWrap, 0);
            width += size.Width;
            height = Math.Max(height, size.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = CurrentVisualState;

        const double boxSize = 14;
        const double spacing = 6;

        double boxY = contentBounds.Y + (contentBounds.Height - boxSize) / 2;
        var boxRect = new Rect(contentBounds.X, boxY, boxSize, boxSize);

        var fill = GetValue(BackgroundProperty);
        var radius = Math.Max(0, CornerRadius * 0.5);

        var borderColor = GetValue(BorderBrushProperty);
        DrawBackgroundAndBorder(context, boxRect, fill, borderColor, radius);

        var markColor = state.IsEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;

        if (IsChecked == true)
        {
            // Check mark
            var p1 = new Point(boxRect.X + 3, boxRect.Y + boxRect.Height * 0.55);
            var p2 = new Point(boxRect.X + boxRect.Width * 0.45, boxRect.Bottom - 4);
            var p3 = new Point(boxRect.Right - 3, boxRect.Y + 3);

            var g = new PathGeometry();
            g.MoveTo(p1);
            g.LineTo(p2);
            g.LineTo(p3);
            context.DrawPath(g, markColor, 2);
        }
        else if (IsChecked == null)
        {
            // Indeterminate mark (horizontal bar)
            var y = boxRect.Y + boxRect.Height / 2;
            var p1 = new Point(boxRect.X + 3, y);
            var p2 = new Point(boxRect.Right - 3, y);
            context.DrawLine(p1, p2, markColor, 2);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var textColor = state.IsEnabled ? Foreground : Theme.Palette.DisabledText;
            var textBounds = new Rect(contentBounds.X + boxSize + spacing, contentBounds.Y, contentBounds.Width - boxSize - spacing, contentBounds.Height);
            var font = GetFont();
            context.DrawText(Text, textBounds, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        SetPressed(true);
        Focus();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        SetPressed(false);

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
        {
            ToggleFromInput();
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
            ToggleFromInput();
            e.Handled = true;
        }
    }

    private void ToggleFromInput()
    {
        if (IsThreeState)
        {
            var next = IsChecked switch
            {
                false => (bool?)true,
                true => (bool?)null,
                _ => (bool?)false
            };
            IsChecked = next;
            return;
        }

        bool nextBool = IsChecked != true;
        IsChecked = nextBool;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textMeasureCache.Invalidate();
    }
}
