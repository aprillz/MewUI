using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button control that responds to clicks.
/// </summary>
public partial class Button : Control, IVisualTreeHost
{
    public static readonly MewProperty<Element?> ContentProperty =
        MewProperty<Element?>.Register<Button>(nameof(Content), null,
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

    protected virtual void OnContentChanged(Element? oldValue, Element? newValue)
    {
        if (oldValue != null) oldValue.Parent = null;
        if (newValue != null) newValue.Parent = this;
    }

    /// <summary>
    /// Click event handler (AOT-compatible).
    /// </summary>
    public event Action? Click;

    public static readonly MewProperty<Func<bool>?> CanClickProperty =
        MewProperty<Func<bool>?>.Register<Button>(nameof(CanClick), null,
            MewPropertyOptions.None,
            static (self, oldValue, newValue) => self.OnCanClickChanged(oldValue, newValue));

    public Func<bool>? CanClick
    {
        get => GetValue(CanClickProperty);
        set => SetValue(CanClickProperty, value);
    }

    protected virtual void OnCanClickChanged(Func<bool>? oldValue, Func<bool>? newValue)
    {
        ReevaluateSuggestedIsEnabled();
    }

    public override bool Focusable => true;

    protected override bool ComputeIsEnabledSuggestion() => CanClick?.Invoke() ?? true;

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

        if (e.Button == MouseButton.Left && IsEffectivelyEnabled)
        {
            SetPressed(true);
            Focus();

            // Capture mouse
            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.CaptureMouse(this);
            }

            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButton.Left && IsPressed)
        {
            SetPressed(false);

            // Release capture
            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.ReleaseMouseCapture();
            }

            // Fire click if still over button
            if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
            {
                OnClick();
            }

            e.Handled = true;
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        SetPressed(false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Space or Enter triggers click
        if ((e.Key == Key.Space || e.Key == Key.Enter) && IsEffectivelyEnabled)
        {
            SetPressed(true);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if ((e.Key == Key.Space || e.Key == Key.Enter) && IsPressed)
        {
            SetPressed(false);
            if (IsEffectivelyEnabled)
            {
                OnClick();
            }

            e.Handled = true;
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

    protected virtual void OnClick() => Click?.Invoke();
}
