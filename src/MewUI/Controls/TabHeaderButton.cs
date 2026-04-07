using Aprillz.MewUI.Rendering;


namespace Aprillz.MewUI.Controls;

/// <summary>
/// Tab header presenter used by <see cref="TabControl"/> to render and interact with individual tab headers.
/// </summary>
internal sealed class TabHeaderButton : ContentControl
{
    public static readonly MewProperty<bool> IsSelectedProperty =
        MewProperty<bool>.Register<TabHeaderButton>(nameof(IsSelected), false, MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnIsSelectedChanged());

    /// <summary>
    /// Gets or sets the tab index this header represents.
    /// </summary>
    private void OnIsSelectedChanged() => RefreshVisualState();

    public int Index { get; set; }

    /// <summary>
    /// Gets or sets whether this tab is currently selected.
    /// </summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Called when the header is clicked (tab selection request).
    /// Single-owner callback — no multicast, no cleanup needed.
    /// </summary>
    internal Action<int>? ClickedCallback { get; set; }

    public TabHeaderButton()
    {
    }

    private void RefreshVisualState()
    {
        EnsureStyleResolved();
        ResolveVisualState();
        InvalidateVisual();
    }

    internal void RefreshOwnerState()
    {
        RefreshVisualState();
    }

    private TabControl? FindOwnerTabControl()
    {
        for (Element? current = Parent; current != null; current = current.Parent)
        {
            if (current is TabControl tabControl)
            {
                return tabControl;
            }
        }

        return null;
    }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (FindOwnerTabControl() is TabControl tabControl && tabControl.IsFocusWithin)
            state = state with { Flags = state.Flags | VisualStateFlags.Focused };
        if (IsSelected)
            state = state with { Flags = state.Flags | VisualStateFlags.Selected };
        return state;
    }

    internal override void OnAccessKey() => ClickedCallback?.Invoke(Index);

    // Keep header buttons out of the default Tab focus order.
    // Keyboard navigation is handled by TabControl itself (arrows / Ctrl+PgUp/PgDn).
    public override bool Focusable => false;

    protected override UIElement? OnHitTest(Point point)
    {
        // Match WPF semantics: disabled tabs should not participate in hit testing,
        // otherwise they keep receiving hover/mouse-over changes and triggering redraw.
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // Prefer inner button hit targets (e.g. close button), but keep the rest of the header
        // clickable as a tab-select surface.
        var hit = base.OnHitTest(point);
        if (hit == null)
        {
            return null;
        }

        if (hit != this)
        {
            return hit is Button ? hit : this;
        }

        return this;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bg = GetValue(BackgroundProperty);
        var borderBrush = GetValue(BorderBrushProperty);
        double r = CornerRadius;
        double bt = BorderThickness;

        // Top-only rounding + no bottom border for tab header
        DrawBackgroundAndBorder(context, GetSnappedBorderBounds(Bounds), bg, borderBrush,
            new Thickness(bt, bt, bt, 0), new CornerRadius(r, r, 0, 0));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        if (Content == null)
        {
            return;
        }

        // Keep the tab label vertically centered.
        var contentBounds = bounds.Deflate(Padding).Deflate(GetBorderVisualInset());
        var desired = Content.DesiredSize;
        if (desired.Height > 0 && contentBounds.Height > desired.Height + 0.5)
        {
            double y = contentBounds.Y + (contentBounds.Height - desired.Height) / 2;
            Content.Arrange(new Rect(contentBounds.X, y, contentBounds.Width, desired.Height));
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Content == null)
        {
            return Size.Empty;
        }

        // Keep measure/arrange symmetric: ArrangeContent deflates border inset (snapped to pixels),
        // so measurement must include it to avoid text clipping (GDI/OpenGL).
        var borderInset = GetBorderVisualInset();
        var contentSize = availableSize.Deflate(Padding).Deflate(borderInset);

        Content.Measure(contentSize);
        return Content.DesiredSize.Inflate(Padding).Inflate(borderInset);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Button == MouseButton.Left && IsEffectivelyEnabled)
        {
            SetPressed(true);

            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.CaptureMouse(this);
            }

            ClickedCallback?.Invoke(Index);
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButton.Left && IsPressed)
        {
            SetPressed(false);

            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.ReleaseMouseCapture();
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
        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key is Key.Space or Key.Enter)
        {
            SetPressed(true);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key is Key.Space or Key.Enter)
        {
            if (IsPressed)
            {
                SetPressed(false);
                ClickedCallback?.Invoke(Index);
            }

            e.Handled = true;
        }
    }
}
