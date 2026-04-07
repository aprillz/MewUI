using Aprillz.MewUI.Rendering;


namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control with a clickable header that expands/collapses its content.
/// </summary>
public class Expander : HeaderedContentControl
{
    public static readonly MewProperty<bool> IsExpandedProperty =
        MewProperty<bool>.Register<Expander>(nameof(IsExpanded), true,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, oldValue, newValue) => self.OnIsExpandedChanged(oldValue, newValue));

    public static readonly MewProperty<double> GlyphSizeProperty =
        MewProperty<double>.Register<Expander>(nameof(GlyphSize), 4.0, MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Gets or sets whether the content is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    internal override void OnAccessKey() { Focus(); IsExpanded = !IsExpanded; }

    protected virtual void OnIsExpandedChanged(bool oldValue, bool newValue)
    {
        ExpandedChanged?.Invoke(newValue);
    }

    /// <summary>
    /// Gets or sets the chevron glyph size.
    /// </summary>
    public double GlyphSize
    {
        get => GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    /// <summary>
    /// Occurs when the expanded state changes.
    /// </summary>
    public event Action<bool>? ExpandedChanged;

    public override bool Focusable => true;

    private const double GlyphAreaWidth = 20;

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsExpanded)
            return state with { Flags = state.Flags | VisualStateFlags.Checked };
        return state;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(Padding);

        double headerHeight = 0;
        double desiredW = 0;

        if (Header != null)
        {
            // Reserve space for glyph
            Header.Measure(new Size(Math.Max(0, inner.Width - GlyphAreaWidth), double.PositiveInfinity));
            headerHeight = Math.Max(Header.DesiredSize.Height, GlyphAreaWidth);
            desiredW = Math.Max(desiredW, Header.DesiredSize.Width + GlyphAreaWidth);
        }
        else
        {
            headerHeight = GlyphAreaWidth;
            desiredW = GlyphAreaWidth;
        }

        if (!IsExpanded || Content == null)
        {
            return new Size(desiredW, headerHeight).Inflate(Padding);
        }

        double spacing = Math.Max(0, HeaderSpacing);
        double contentH = double.IsPositiveInfinity(inner.Height)
            ? double.PositiveInfinity
            : Math.Max(0, inner.Height - headerHeight - spacing);

        Content.Measure(new Size(inner.Width, contentH));
        desiredW = Math.Max(desiredW, Content.DesiredSize.Width);
        return new Size(desiredW, headerHeight + spacing + Content.DesiredSize.Height).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        double y = inner.Y;

        if (Header != null)
        {
            double headerH = Math.Max(Header.DesiredSize.Height, GlyphAreaWidth);
            // Offset header right to make room for glyph on the left
            Header.Arrange(new Rect(inner.X + GlyphAreaWidth, y, Math.Max(0, inner.Width - GlyphAreaWidth), headerH));
            y += headerH;
        }
        else
        {
            y += GlyphAreaWidth;
        }

        if (!IsExpanded || Content == null)
        {
            return;
        }

        y += Math.Max(0, HeaderSpacing);
        Content.Arrange(new Rect(inner.X, y, inner.Width, Math.Max(0, inner.Bottom - y)));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        Header?.Render(context);
        if (IsExpanded)
        {
            Content?.Render(context);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, CornerRadius);

        // Draw chevron glyph
        var inner = bounds.Deflate(Padding);
        double headerH = Header?.DesiredSize.Height ?? GlyphAreaWidth;
        headerH = Math.Max(headerH, GlyphAreaWidth);

        var glyphCenter = new Point(
            inner.X + GlyphAreaWidth / 2,
            inner.Y + headerH / 2);

        var fg = IsEffectivelyEnabled ? Foreground : Theme.Palette.DisabledText;
        Glyph.Draw(context, glyphCenter, GlyphSize, fg,
            IsExpanded ? GlyphKind.ChevronDown : GlyphKind.ChevronRight);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        // Check content if expanded (content may contain interactive elements)
        if (IsExpanded && Content is UIElement contentUi)
        {
            var hit = contentUi.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        // Header area (including glyph) → return self so OnMouseDown can toggle
        if (Bounds.Contains(point))
        {
            return this;
        }

        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }

        // Toggle if clicked in the header row (glyph area + header)
        var inner = Bounds.Deflate(Padding);
        double headerH = Header?.DesiredSize.Height ?? GlyphAreaWidth;
        headerH = Math.Max(headerH, GlyphAreaWidth);
        var headerRow = new Rect(inner.X, inner.Y, inner.Width, headerH);

        if (headerRow.Contains(e.Position))
        {
            IsExpanded = !IsExpanded;
            Focus();
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

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
    }
}
