using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A checkbox control with optional content label.
/// </summary>
public class CheckBox : ContentControl
{
    public static readonly MewProperty<bool?> IsCheckedProperty =
        MewProperty<bool?>.Register<CheckBox>(nameof(IsChecked), (bool?)false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, oldValue, newValue) => self.OnIsCheckedChanged(oldValue, newValue));

    public static readonly MewProperty<bool> IsThreeStateProperty =
        MewProperty<bool>.Register<CheckBox>(nameof(IsThreeState), false, MewPropertyOptions.None);

    public CheckBox()
    {
    }

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
    /// Toggles the checked state, respecting three-state mode.
    /// </summary>
    internal void Toggle()
    {
        if (IsThreeState)
        {
            IsChecked = IsChecked switch
            {
                false => true,
                true => (bool?)null,
                _ => false
            };
        }
        else
        {
            IsChecked = IsChecked != true;
        }
    }

    private const double BoxSize = 14;
    private const double Spacing = 6;

    protected override Size MeasureContent(Size availableSize)
    {
        double width = BoxSize + Spacing;
        double height = BoxSize;

        if (Content != null)
        {
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - width - Padding.HorizontalThickness),
                double.PositiveInfinity);
            Content.Measure(contentAvailable);
            width += Content.DesiredSize.Width;
            height = Math.Max(height, Content.DesiredSize.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Content == null)
        {
            return;
        }

        var contentBounds = bounds.Deflate(Padding);
        var textBounds = new Rect(
            contentBounds.X + BoxSize + Spacing,
            contentBounds.Y,
            Math.Max(0, contentBounds.Width - BoxSize - Spacing),
            contentBounds.Height);
        Content.Arrange(textBounds);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = CurrentVisualState;

        double boxY = contentBounds.Y + (contentBounds.Height - BoxSize) / 2;
        var boxRect = new Rect(contentBounds.X, boxY, BoxSize, BoxSize);

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
