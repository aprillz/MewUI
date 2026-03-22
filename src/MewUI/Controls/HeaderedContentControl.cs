using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that contains a header element and a single content element.
/// </summary>
public class HeaderedContentControl : ContentControl
    , IVisualTreeHost
{
    public static readonly MewProperty<Element?> HeaderProperty =
        MewProperty<Element?>.Register<HeaderedContentControl>(nameof(Header), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnHeaderChanged(oldValue, newValue));

    /// <summary>
    /// Gets or sets the header element.
    /// </summary>
    public Element? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    protected virtual void OnHeaderChanged(Element? oldValue, Element? newValue)
    {
        if (oldValue != null) oldValue.Parent = null;
        if (newValue != null) newValue.Parent = this;
    }

    public static readonly MewProperty<double> HeaderSpacingProperty =
        MewProperty<double>.Register<HeaderedContentControl>(nameof(HeaderSpacing), 0.0,
            MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Gets or sets the spacing between header and content.
    /// </summary>
    public double HeaderSpacing
    {
        get => GetValue(HeaderSpacingProperty);
        set => SetValue(HeaderSpacingProperty, value);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(Padding);

        double headerHeight = 0;
        double desiredW = 0;

        if (Header != null)
        {
            Header.Measure(new Size(inner.Width, double.PositiveInfinity));
            headerHeight = Header.DesiredSize.Height;
            desiredW = Math.Max(desiredW, Header.DesiredSize.Width);
        }

        double spacing = (Header != null && Content != null) ? Math.Max(0, HeaderSpacing) : 0;

        if (Content != null)
        {
            double contentH = double.IsPositiveInfinity(inner.Height)
                ? double.PositiveInfinity
                : Math.Max(0, inner.Height - headerHeight - spacing);

            Content.Measure(new Size(inner.Width, contentH));
            desiredW = Math.Max(desiredW, Content.DesiredSize.Width);
            return new Size(desiredW, headerHeight + spacing + Content.DesiredSize.Height).Inflate(Padding);
        }

        return new Size(desiredW, headerHeight).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);

        double y = inner.Y;

        if (Header != null)
        {
            double headerH = Header.DesiredSize.Height;
            Header.Arrange(new Rect(inner.X, y, inner.Width, headerH));
            y += headerH;
        }

        if (Header != null && Content != null)
        {
            y += Math.Max(0, HeaderSpacing);
        }

        if (Content != null)
        {
            Content.Arrange(new Rect(inner.X, y, inner.Width, Math.Max(0, inner.Bottom - y)));
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        base.RenderSubtree(context);
        Header?.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        if (Header is UIElement headerUi)
        {
            var hit = headerUi.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return base.OnHitTest(point);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (Header != null && !visitor(Header)) return false;
        if (Content != null && !visitor(Content)) return false;
        return true;
    }
}
