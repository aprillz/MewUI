using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that render a header with a right-side drop-down button
/// and show a popup when opened (e.g. ComboBox, DatePicker, ColorPicker). The popup lifecycle
/// (open state, placement, focus, dismissal) comes from <see cref="PopupOwnerBase"/>; this class
/// adds the header-and-arrow chrome.
/// </summary>
public abstract partial class DropDownBase : PopupOwnerBase
{
    static DropDownBase() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<DropDownBase>(DefaultStyles.CreateDropDownBaseStyle);

    /// <summary>
    /// Gets or sets the arrow (chevron) color for the current frame.
    /// Derived controls can update this inside <see cref="RenderHeaderContent"/>.
    /// </summary>
    protected Color ArrowForeground { get; set; }

    /// <summary>
    /// Gets the width (in DIP) reserved for the arrow button area.
    /// </summary>
    protected virtual double ArrowAreaWidth => 22;

    /// <summary>
    /// Gets the corner radius used for the header border.
    /// </summary>
    protected virtual double CornerRadiusDip => CornerRadius;

    /// <summary>
    /// The header height, used both for header layout and as the popup anchor height.
    /// </summary>
    protected double ResolveHeaderHeight()
    {
        if (!double.IsNaN(Height) && Height > 0)
        {
            return Height;
        }

        var min = MinHeight > 0 ? MinHeight : 0;
        return Math.Max(Math.Max(24, FontSize + Padding.VerticalThickness + 8), min);
    }

    protected override double ResolveAnchorHeight() => ResolveHeaderHeight();

    /// <summary>
    /// Measures the header (excluding margin).
    /// </summary>
    protected abstract Size MeasureHeader(Size availableSize);

    /// <summary>
    /// Renders the header content (text/content area). The arrow is rendered by the base.
    /// </summary>
    protected abstract void RenderHeaderContent(IGraphicsContext context, Rect headerRect, Rect innerHeaderRect);

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

        var borderInset = GetBorderVisualInset();
        var hInset = borderInset * 2 + Padding.HorizontalThickness;
        var innerWidth = Math.Max(0, availableSize.Width - hInset);
        var header = MeasureHeader(new Size(innerWidth, availableSize.Height));
        return new Size(header.Width + hInset, header.Height);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (!HasTemplateInstance)
        {
            var bounds = GetSnappedBorderBounds(Bounds);
            var borderInset = GetBorderVisualInset();
            double radius = CornerRadiusDip;

            DrawBackgroundAndBorder(
                context,
                bounds,
                Background,
                BorderBrush,
                BorderThickness,
                radius);

            var headerHeight = ResolveAnchorHeight();
            var headerRect = new Rect(bounds.X, bounds.Y, bounds.Width, headerHeight);
            var innerHeaderRect = headerRect.Deflate(new Thickness(borderInset));

            ArrowForeground = Foreground;
            var profiler = PerformanceProfiler.Instance;
            using (DevToolsGate.IsSupported && profiler.IsEnabled ? profiler.SampleElement(typeof(DropDownBase), ProfilerSampleCategory.Render, this) : default)
            {
                RenderHeaderContent(context, headerRect, innerHeaderRect);
            }

            DrawArrow(context, innerHeaderRect, ArrowForeground, IsDropDownOpen);
        }

        // Popup bounds update lives on PopupOwnerBase.
        base.OnRender(context);
    }

    private void DrawArrow(IGraphicsContext context, Rect headerRect, Color color, bool isUp)
    {
        double centerX = headerRect.Right - ArrowAreaWidth / 2;
        double centerY = headerRect.Y + headerRect.Height / 2;

        Glyph.Draw(
            context,
            new Point(centerX, centerY),
            size: 4,
            color,
            isUp ? GlyphKind.ChevronUp : GlyphKind.ChevronDown);
    }
}
