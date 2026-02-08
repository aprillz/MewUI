using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A container control that draws a border with a header (WinForms-style GroupBox).
/// </summary>
public sealed class GroupBox : HeaderedContentControl
{
    protected override Color DefaultBackground => Theme.Palette.ContainerBackground;
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;
    /// <summary>
    /// Gets whether to invalidate visual on mouse over changes.
    /// </summary>
    protected override bool InvalidateOnMouseOverChanged => false;

    /// <summary>
    /// Gets or sets the horizontal inset for the header.
    /// </summary>
    public double HeaderInset
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    } = 0;

    /// <summary>
    /// Initializes a new instance of the GroupBox class.
    /// </summary>
    public GroupBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(8);
        HeaderSpacing = 4;
    }

    /// <summary>
    /// Gets whether the groupbox can receive keyboard focus.
    /// </summary>
    public override bool Focusable => false;

    /// <summary>
    /// Measures the content size.
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The desired size.</returns>
    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();

        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;
        var padding = Padding;
        double headerHeight = 0;
        double headerWidth = 0;

        double availableW = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - padding.HorizontalThickness - border.HorizontalThickness);

        double availableH = double.IsPositiveInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Height - padding.VerticalThickness - border.VerticalThickness);

        if (Header != null)
        {
            Header.Measure(new Size(availableW, double.PositiveInfinity));
            headerHeight = Header.DesiredSize.Height;
            headerWidth = Header.DesiredSize.Width;
        }

        double spacing = (Header != null && Content != null) ? Math.Max(0, HeaderSpacing) : 0;
        double contentSlotH = double.IsPositiveInfinity(availableH)
            ? double.PositiveInfinity
            : Math.Max(0, availableH - headerHeight - spacing);

        double contentW = 0;
        double contentH = 0;
        if (Content != null)
        {
            Content.Measure(new Size(availableW, contentSlotH));
            contentW = Content.DesiredSize.Width;
            contentH = Content.DesiredSize.Height;
        }

        double desiredW = Math.Max(headerWidth + HeaderInset, contentW);
        double desiredH = headerHeight + spacing + contentH;

        return new Size(desiredW, desiredH).Inflate(padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var outer = bounds;
        double boxTop = outer.Y;

        double headerHeight = 0;
        if (Header != null)
        {
            headerHeight = Header.DesiredSize.Height;
            double headerW = Math.Min(Math.Max(0, outer.Width - HeaderInset), Header.DesiredSize.Width);
            headerW = Math.Max(0, headerW);

            Header.Arrange(new Rect(
                outer.X + HeaderInset,
                outer.Y,
                headerW,
                headerHeight));

            boxTop = Header.Bounds.Bottom;
        }

        double spacing = (Header != null && Content != null) ? Math.Max(0, HeaderSpacing) : 0;
        double boxY = boxTop + spacing;
        var boxRect = new Rect(outer.X, boxY, outer.Width, Math.Max(0, outer.Bottom - boxY));
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        var innerBox = boxRect.Deflate(border).Deflate(Padding);

        if (Content != null)
        {
            Content.Arrange(innerBox);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        
        var bounds = GetBorderRenderMetrics(Bounds, 0).Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double headerBottom = Header?.Bounds.Bottom ?? bounds.Y;
        double boxY = headerBottom + (Header != null && Content != null ? Math.Max(0, HeaderSpacing) : 0);

        var boxRect = new Rect(bounds.X, boxY, bounds.Width, Math.Max(0, bounds.Bottom - boxY));
        if (boxRect.Height <= 0)
        {
            return;
        }

        double radius = Theme.Metrics.ControlCornerRadius;
        DrawBackgroundAndBorder(context, boxRect, Background, BorderBrush, radius);
    }
}
