using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that contains a header element and a single content element.
/// </summary>
public class HeaderedContentControl : ContentControl
    , IVisualTreeHost
{
    public Element? Header
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

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

    public double HeaderSpacing
    {
        get;
        set { field = value; InvalidateMeasure(); }
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

    public override void Render(IGraphicsContext context)
    {
        base.Render(context);
        Header?.Render(context);
    }

    public override UIElement? HitTest(Point point)
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

        return base.HitTest(point);
    }

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        if (Header != null)
        {
            visitor(Header);
        }

        if (Content != null)
        {
            visitor(Content);
        }
    }
}
