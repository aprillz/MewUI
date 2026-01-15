using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that contains a single child element.
/// </summary>
public class ContentControl : Control
    , IVisualTreeHost
{
    /// <summary>
    /// Gets or sets the content element.
    /// </summary>
    public Element? Content
    {
        get;
        set
        {
            if (field != value)
            {
                if (field != null)
                {
                    field.Parent = null;
                }

                field = value;

                if (field != null)
                {
                    field.Parent = this;
                }

                InvalidateMeasure();
            }
        }
    }

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

    public override void Render(IGraphicsContext context)
    {
        base.Render(context);
        Content?.Render(context);
    }

    public override UIElement? HitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
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

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        if (Content != null)
        {
            visitor(Content);
        }
    }
}
