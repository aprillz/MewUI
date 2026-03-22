using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button-like toggle control. When checked, its background is tinted with the theme accent (50%).
/// </summary>
public sealed partial class ToggleButton : ToggleBase, IVisualTreeHost
{
    public static readonly MewProperty<Element?> ContentProperty =
        MewProperty<Element?>.Register<ToggleButton>(nameof(Content), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnContentChanged(oldValue, newValue));

    /// <summary>
    /// Gets or sets the content element.
    /// </summary>
    public Element? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private void OnContentChanged(Element? oldValue, Element? newValue)
    {
        if (oldValue != null) oldValue.Parent = null;
        if (newValue != null) newValue.Parent = this;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        if (Content == null)
        {
            return new Size(Padding.HorizontalThickness + 20, Padding.VerticalThickness + 10).Inflate(border);
        }

        var contentSize = availableSize.Deflate(Padding).Deflate(border);
        Content.Measure(contentSize);
        return Content.DesiredSize.Inflate(Padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        if (Content == null)
        {
            return;
        }

        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;
        var contentBounds = bounds.Deflate(Padding).Deflate(border);
        Content.Arrange(contentBounds);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bgColor = GetValue(BackgroundProperty);
        var borderColor = GetValue(BorderBrushProperty);

        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = CornerRadius;
        DrawBackgroundAndBorder(context, bounds, bgColor, borderColor, radius);

        Content?.Render(context);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }

        SetPressed(true);
        Focus();

        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Handled || e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        SetPressed(false);

        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
        {
            IsChecked = !IsChecked;
        }

        e.Handled = true;
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        SetPressed(false);
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
                IsChecked = !IsChecked;
                e.Handled = true;
            }
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        if (Content is UIElement uiContent)
        {
            var result = uiContent.HitTest(point);
            if (result != null)
            {
                return result;
            }
        }

        if (Bounds.Contains(point))
        {
            return this;
        }

        return null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Content == null || visitor(Content);
}
