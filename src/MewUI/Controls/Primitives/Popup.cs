using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A lightweight popup that can display content in a window-managed overlay.
/// This is a MewUI-native counterpart to WPF's Popup.
/// </summary>
public sealed class Popup : FrameworkElement, IPopupOwner
{
    private bool _closingFromWindow;
    private Window? _window;

    public UIElement? Child
    {
        get;
        set
        {
            if ((field) == value)
            {
                return;
            }

            if (IsOpen)
            {
                CloseCore();
            }

            field = value;
        }
    }

    public bool IsOpen
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            if (field)
            {
                OpenCore();
            }
            else
            {
                CloseCore();
            }
        }
    }

    public bool StaysOpen { get; set; } = true;

    public UIElement? PlacementTarget { get; set; }

    /// <summary>
    /// Optional placement rectangle in window coordinates (DIPs).
    /// When set, this takes precedence over <see cref="PlacementTarget"/> bounds for positioning.
    /// This mirrors WPF's <c>PlacementRectangle</c> behavior and enables caret/anchor-point popups.
    /// </summary>
    public Rect? PlacementRectangle { get; set; }

    public PlacementMode Placement { get; set; } = PlacementMode.Bottom;

    public double HorizontalOffset { get; set; }

    public double VerticalOffset { get; set; }

    public event Action? Opened;

    public event Action? Closed;

    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void ArrangeContent(Rect bounds) { }

    public override void Render(IGraphicsContext context) { }

    protected override UIElement? OnHitTest(Point point) => null;

    public void UpdatePosition()
    {
        if (!IsOpen)
        {
            return;
        }

        if (Child == null)
        {
            return;
        }

        var window = ResolveWindow();
        if (window == null)
        {
            return;
        }

        var bounds = CalculatePopupBounds(window, Child);
        window.UpdatePopup(Child, bounds);
    }

    private void OpenCore()
    {
        if (Child == null)
        {
            IsOpen = false;
            return;
        }

        var window = ResolveWindow();
        if (window == null)
        {
            IsOpen = false;
            return;
        }

        _window = window;
        var bounds = CalculatePopupBounds(window, Child);
        window.ShowPopup(this, Child, bounds, staysOpen: StaysOpen);
        Opened?.Invoke();
    }

    private void CloseCore()
    {
        if (Child == null)
        {
            return;
        }

        var window = _window ?? ResolveWindow();
        if (window == null)
        {
            return;
        }

        if (_closingFromWindow)
        {
            return;
        }

        window.ClosePopup(Child);
        Closed?.Invoke();
    }

    private Window? ResolveWindow()
    {
        if (PlacementTarget != null)
        {
            if (PlacementTarget.FindVisualRoot() is Window w1)
            {
                return w1;
            }
        }

        if (FindVisualRoot() is Window w2)
        {
            return w2;
        }

        return _window;
    }

    private Rect CalculatePopupBounds(Window window, UIElement popup)
    {
        var client = window.ClientSize;

        Point anchor;
        if (Placement == PlacementMode.Mouse)
        {
            anchor = window.LastMousePositionDip;
        }
        else
        {
            Rect b;
            if (PlacementRectangle is Rect rect)
            {
                b = rect;
            }
            else
            {
                var target = PlacementTarget;
                if (target == null)
                {
                    anchor = window.LastMousePositionDip;
                    goto measure;
                }

                b = target.Bounds;
            }

            anchor = Placement switch
            {
                PlacementMode.Top => new Point(b.X, b.Y),
                PlacementMode.Bottom => new Point(b.X, b.Bottom),
                PlacementMode.Left => new Point(b.X, b.Y),
                PlacementMode.Right => new Point(b.Right, b.Y),
                _ => new Point(b.X, b.Bottom),
            };
        }

        measure:
        // Measure with a generous maximum to avoid infinite sizes.
        var max = new Size(Math.Max(0, client.Width), Math.Max(0, client.Height));
        if (max.Width <= 0) max = max.WithWidth(1_000_000);
        if (max.Height <= 0) max = max.WithHeight(1_000_000);

        popup.Measure(max);
        var desired = popup.DesiredSize;
        double w = Math.Max(0, desired.Width);
        double h = Math.Max(0, desired.Height);

        double x = anchor.X + HorizontalOffset;
        double y = anchor.Y + VerticalOffset;

        if (Placement != PlacementMode.Mouse)
        {
            Rect b;
            if (PlacementRectangle is Rect rect)
            {
                b = rect;
            }
            else if (PlacementTarget != null)
            {
                b = PlacementTarget.Bounds;
            }
            else
            {
                b = new Rect(anchor.X, anchor.Y, 0, 0);
            }

            switch (Placement)
            {
                case PlacementMode.Top:
                    y = b.Y - h + VerticalOffset;
                    break;
                case PlacementMode.Bottom:
                    y = b.Bottom + VerticalOffset;
                    break;
                case PlacementMode.Left:
                    x = b.X - w + HorizontalOffset;
                    break;
                case PlacementMode.Right:
                    x = b.Right + HorizontalOffset;
                    break;
            }
        }

        // Clamp to client bounds.
        if (x + w > client.Width) x = Math.Max(0, client.Width - w);
        if (y + h > client.Height) y = Math.Max(0, client.Height - h);
        if (x < 0) x = 0;
        if (y < 0) y = 0;

        return new Rect(x, y, w, h);
    }

    void IPopupOwner.OnPopupClosed(UIElement popup)
    {
        if (!ReferenceEquals(popup, Child))
        {
            return;
        }

        _closingFromWindow = true;
        try
        {
            IsOpen = false;
            Closed?.Invoke();
        }
        finally
        {
            _closingFromWindow = false;
        }
    }
}
