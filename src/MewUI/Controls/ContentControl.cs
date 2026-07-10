using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that contains a single child element.
/// </summary>
public class ContentControl : Control
    , IVisualTreeHost
    , ILogicalTreeHost
{
    public static readonly MewProperty<Element?> ContentProperty =
        MewProperty<Element?>.Register<ContentControl>(nameof(Content), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnContentChanged(oldValue, newValue),
            validate: static (self, value) => self.ValidateContent(value));

    /// <summary>
    /// Gets or sets the content element.
    /// </summary>
    public Element? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Rejects an invalid Content candidate before the value is committed.
    /// Derived classes add their own slot rules (e.g. an element cannot occupy two slots).
    /// </summary>
    /// <param name="candidate">The proposed content; null is always valid.</param>
    protected virtual void ValidateContent(Element? candidate)
        => ValidateLogicalChild(candidate);

    protected virtual void OnContentChanged(Element? oldValue, Element? newValue)
        => ChangeLogicalChild(oldValue, newValue);

    protected override Size MeasureContent(Size availableSize)
    {
        if (Content == null)
        {
            return Size.Empty;
        }

        // Subtract padding
        var contentSize = availableSize.Deflate(Padding);

        Content.Measure(contentSize);
        return Content.DesiredSize.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Content == null)
        {
            return;
        }

        // Arrange within padding
        var contentBounds = bounds.Deflate(Padding);
        Content.Arrange(contentBounds);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        Content?.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // First check children
        if (Content is UIElement uiContent)
        {
            var result = uiContent.HitTest(point);
            if (result != null)
            {
                return result;
            }
        }

        // Then check self
        if (Bounds.Contains(point))
        {
            return this;
        }

        return null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Content == null || visitor(Content);

    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor)
        => Content == null || visitor(Content);
}
