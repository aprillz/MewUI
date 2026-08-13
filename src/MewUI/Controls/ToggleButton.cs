using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button-like toggle control. When checked, its background is tinted with the theme accent (50%).
/// </summary>
public partial class ToggleButton : ToggleBase
{
    static ToggleButton() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ToggleButton>(DefaultStyles.CreateToggleButtonStyle);

    private readonly PressCaptureHelper _pressCapture;

    public ToggleButton()
    {
        _pressCapture = new PressCaptureHelper(this, SetPressed);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance || EffectiveContent != null)
        {
            return base.MeasureContent(availableSize);
        }

        return new Size(Padding.HorizontalThickness + 20, Padding.VerticalThickness + 10)
            .Inflate(GetBorderVisualInset());
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // A template owns the control's entire visuals; the built-in chrome would double-render.
        if (HasTemplateInstance)
        {
            return;
        }

        var bgColor = GetValue(BackgroundProperty);
        var borderColor = GetValue(BorderBrushProperty);

        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = CornerRadius;
        DrawBackgroundAndBorder(context, bounds, bgColor, borderColor, BorderThickness, radius);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }

        _pressCapture.BeginPress(() => Focus());

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Handled || e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        _pressCapture.EndPress();

        if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
        {
            CommitIsCheckedFromUser(!IsChecked);
        }

        e.Handled = true;
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _pressCapture.CancelPress();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            SetPressed(true);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if ((e.Key == Key.Space || e.Key == Key.Enter) && IsPressed)
        {
            SetPressed(false);

            if (e.Key == Key.Enter)
            {
                CommitIsCheckedFromUser(!IsChecked);
                e.Handled = true;
            }
        }
    }

}
