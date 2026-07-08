using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The segment container used by <see cref="SegmentedBase"/> (<see cref="SegmentedControl"/> /
/// <see cref="ButtonGroup"/>). A lightweight <see cref="ContentControl"/> that paints only its own
/// background (rounded at the strip ends); the owning control draws the outer border and the
/// dividers on top. State split:
/// <list type="bullet">
/// <item><see cref="IsChecked"/> = the active-fill visual (shared by selection and toggle).</item>
/// <item><see cref="IsCheckable"/> = whether a click flips <see cref="IsChecked"/>.</item>
/// </list>
/// A <see cref="SegmentedControl"/> drives <see cref="IsChecked"/> exclusively (selection); a
/// <see cref="ButtonGroup"/> segment flips it per-click when checkable, or just raises
/// <see cref="Click"/> when it is a plain command.
/// </summary>
public sealed class SegmentButton : ContentControl
{
    public static readonly MewProperty<bool> IsCheckedProperty =
        MewProperty<bool>.Register<SegmentButton>(nameof(IsChecked), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState,
            static (self, _, _) => self.RefreshVisualState());

    private readonly PressCaptureHelper _pressCapture;

    public SegmentButton()
    {
        // Height follows the owning control (segments stretch to the panel); carry no min-height so a
        // horizontal StackPanel (Auto sizing) does not over-tall the strip.
        MinHeight = 0;
        _pressCapture = new PressCaptureHelper(this, SetPressed);
    }

    /// <summary>Gets or sets the active-fill visual state (selected or toggled-on).</summary>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Gets or sets whether a mouse click flips <see cref="IsChecked"/> (independent toggle).</summary>
    public bool IsCheckable { get; set; }

    /// <summary>Occurs when the segment is activated by a mouse click (command channel).</summary>
    public event Action? Click;

    /// <summary>Occurs when <see cref="IsChecked"/> flips due to a click (checkable segments only).</summary>
    public event Action<bool>? CheckedChanged;

    /// <summary>Index of this segment within the owning control.</summary>
    internal int Index { get; set; }

    /// <summary>Whether this segment is the first in the strip (rounds the leading corners).</summary>
    internal bool IsFirst { get; set; } = true;

    /// <summary>Whether this segment is the last in the strip (rounds the trailing corners).</summary>
    internal bool IsLast { get; set; } = true;

    /// <summary>Corner radius (in DIPs) applied to the rounded ends. Set by the owner.</summary>
    internal double Radius { get; set; }

    // Snapped outer edge of the strip, set by the owner on the first/last segment. Aligns the leading
    // (StripLeft) and trailing (StripRight) background edge to the container's inner border edge, so the
    // strip fills the frame exactly - independent per-child pixel snapping can otherwise drift 1px.
    internal double? StripLeft { get; set; }
    internal double? StripRight { get; set; }

    /// <summary>
    /// Single-owner callback raised on click, after <see cref="Click"/>. Used by
    /// <see cref="SegmentedControl"/> to drive exclusive selection by index. Keyboard navigation is
    /// handled by the owning control.
    /// </summary>
    internal Action<int>? ClickedCallback { get; set; }

    private void RefreshVisualState()
    {
        EnsureStyleResolved();
        ResolveVisualState(snap: false);
        InvalidateVisual();
    }

    internal void RefreshOwnerState() => RefreshVisualState();

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsChecked)
        {
            // Reuse the Selected visual (accent fill) for both selection and toggle-on so the active
            // look is one render path across SegmentedControl and ButtonGroup.
            state = state with { Flags = state.Flags | VisualStateFlags.Selected };
        }
        return state;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bg = GetValue(BackgroundProperty);
        if (bg.A == 0)
        {
            return;
        }

        double r = Radius;
        var cornerRadius = (IsFirst, IsLast) switch
        {
            (true, true) => new CornerRadius(r, r, r, r),
            (true, false) => new CornerRadius(r, 0, 0, r),
            (false, true) => new CornerRadius(0, r, r, 0),
            _ => new CornerRadius(0),
        };

        var bgBounds = GetSnappedBorderBounds(Bounds);
        double left = StripLeft ?? bgBounds.Left;
        double right = StripRight ?? bgBounds.Right;
        bgBounds = new Rect(left, bgBounds.Y, Math.Max(0, right - left), bgBounds.Height);

        // The container owns the border/divider chrome; segments paint background only.
        DrawBackgroundAndBorder(context, bgBounds, bg, Color.Transparent,
            Thickness.Zero, cornerRadius);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        if (Content == null)
        {
            return;
        }

        // Keep the label vertically centered within the segment.
        var contentBounds = bounds.Deflate(Padding);
        var desired = Content.DesiredSize;
        if (desired.Height > 0 && contentBounds.Height > desired.Height + 0.5)
        {
            double y = contentBounds.Y + (contentBounds.Height - desired.Height) / 2;
            Content.Arrange(new Rect(contentBounds.X, y, contentBounds.Width, desired.Height));
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        // Disabled segments should not participate in hit testing (no hover/mouse-over churn).
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // Return the actual hit (Label / Glyph / self). Collapsing non-focusable descendants to
        // `this` would drop their ToolTip / Cursor / hover. The segment still owns the click via
        // OnMouseDown bubbling, and focusable descendants handle their own clicks first.
        return base.OnHitTest(point);
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
            _pressCapture.BeginPress();
            Activate();
            e.Handled = true;
        }
    }

    private void Activate()
    {
        if (IsCheckable)
        {
            IsChecked = !IsChecked;
            CheckedChanged?.Invoke(IsChecked);
        }

        Click?.Invoke();
        ClickedCallback?.Invoke(Index);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButton.Left && IsPressed)
        {
            _pressCapture.EndPress();

            e.Handled = true;
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _pressCapture.CancelPress();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

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
                Activate();
            }
            e.Handled = true;
        }
    }
}
