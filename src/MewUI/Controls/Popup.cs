using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Which side of the anchor a popup opens on. The placement flips to the opposite side when the
/// preferred one cannot fit the popup.
/// </summary>
public enum PopupAnchorSide
{
    Below,
    Above,
    Right,
    Left,
}

/// <summary>Carries why a popup closed.</summary>
public sealed class PopupClosedEventArgs : EventArgs
{
    public PopupClosedEventArgs(PopupCloseKind kind) => Kind = kind;

    public PopupCloseKind Kind { get; }
}

/// <summary>
/// Hosts arbitrary content in a popup surface anchored to a rectangle in the owner window. The popup
/// draws nothing of its own, so the content carries its own frame, and it never moves the keyboard
/// focus, so the owner keeps driving the input. Use <see cref="PopupOwnerBase"/> instead for a
/// control that is itself the trigger and wants the focus to follow into the popup.
/// </summary>
public class Popup : FrameworkElement, IVisualTreeHost, ILogicalTreeHost
{
    private PopupCloseKind _closeKind = PopupCloseKind.UserInitiated;

    public static readonly MewProperty<UIElement?> ContentProperty =
        MewProperty<UIElement?>.Register<Popup>(nameof(Content), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnContentChanged(oldValue, newValue),
            validate: static (self, value) => self.ValidateLogicalChild(value, allowTransfer: true));

    public static readonly MewProperty<bool> StaysOpenProperty =
        MewProperty<bool>.Register<Popup>(nameof(StaysOpen), false);

    private static readonly MewPropertyKey<bool> IsOpenPropertyKey =
        MewProperty<bool>.RegisterReadOnly<Popup>(nameof(IsOpen), false);

    public static readonly MewProperty<bool> IsOpenProperty = IsOpenPropertyKey.Property;

    /// <summary>The element shown in the popup surface.</summary>
    public UIElement? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// When true the popup survives an outside press and a focus change; only its owner leaving the
    /// visual tree or an explicit <see cref="Close"/> takes it down. Read when the popup opens.
    /// </summary>
    public bool StaysOpen
    {
        get => GetValue(StaysOpenProperty);
        set => SetValue(StaysOpenProperty, value);
    }

    /// <summary>
    /// Whether the popup is currently on screen. A read-only property so a trigger can bind its own
    /// state to it or a style can key off it, while opening stays a call.
    /// </summary>
    public bool IsOpen => GetValue(IsOpenProperty);

    public event EventHandler? Opened;

    public event EventHandler<PopupClosedEventArgs>? Closed;

    /// <summary>
    /// Opens the popup against <paramref name="anchorInWindow"/>, expressed in the owner window's
    /// coordinates, and returns the bounds it was placed at. Returns an empty rectangle when
    /// <paramref name="owner"/> is not in a window or the popup has no content.
    /// </summary>
    public Rect ShowAt(UIElement owner, Rect anchorInWindow, PopupAnchorSide side = PopupAnchorSide.Below)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (Content == null || owner.FindVisualRoot() is not Window window)
        {
            return default;
        }

        var bounds = window.ShowPopup(owner, this, w => Place(w, anchorInWindow, side), staysOpen: StaysOpen);
        if (!IsOpen)
        {
            SetValue(IsOpenPropertyKey, true);
            _closeKind = PopupCloseKind.UserInitiated;
            Opened?.Invoke(this, EventArgs.Empty);
        }
        return bounds;
    }

    /// <summary>
    /// Moves an open popup to a new anchor and returns its new bounds. Does nothing while closed,
    /// which is what lets a caller track a moving anchor without testing <see cref="IsOpen"/> first.
    /// </summary>
    public Rect MoveTo(Rect anchorInWindow, PopupAnchorSide side = PopupAnchorSide.Below)
    {
        if (!IsOpen || FindVisualRoot() is not Window window)
        {
            return default;
        }

        var bounds = Place(window, anchorInWindow, side);
        window.UpdatePopup(this, bounds);
        return bounds;
    }

    /// <summary>Closes the popup. Closing a closed popup is harmless.</summary>
    public void Close()
    {
        if (!IsOpen || FindVisualRoot() is not Window window)
        {
            return;
        }

        window.ClosePopup(this);
    }

    /// <summary>
    /// Resolves the placement rectangle. The popup measures against the region it may occupy, then
    /// takes the preferred side unless the opposite one has room the preferred one lacks.
    /// </summary>
    private Rect Place(Window window, Rect anchorInWindow, PopupAnchorSide side)
    {
        var region = window.GetPopupPlacementRegion(anchorInWindow);
        Measure(new Size(Math.Max(0, region.Width), Math.Max(0, region.Height)));
        double width = Math.Max(0, DesiredSize.Width);
        double height = Math.Max(0, DesiredSize.Height);

        if (side == PopupAnchorSide.Below || side == PopupAnchorSide.Above)
        {
            double x = PopupPlacement.ClampHorizontal(anchorInWindow.X, width, region, floorToLeftEdge: false);
            if (side == PopupAnchorSide.Below)
            {
                var (y, placedHeight) = PopupPlacement.ResolveVerticalPreferBelowIfFits(
                    anchorInWindow.Y, anchorInWindow.Bottom, region, height);
                return new Rect(x, y, width, placedHeight);
            }
            else
            {
                var (y, placedHeight) = ResolveVerticalPreferAbove(anchorInWindow, region, height);
                return new Rect(x, y, width, placedHeight);
            }
        }
        else
        {
            double y = Math.Min(anchorInWindow.Y, Math.Max(region.Y, region.Bottom - height));
            double placedHeight = Math.Min(height, Math.Max(0, region.Bottom - y));
            double x = ResolveHorizontalSide(anchorInWindow, region, width, side == PopupAnchorSide.Right);
            return new Rect(x, y, width, placedHeight);
        }
    }

    private static (double y, double height) ResolveVerticalPreferAbove(Rect anchor, Rect region, double desiredHeight)
    {
        double availableAbove = Math.Max(0, anchor.Y - region.Y);
        double availableBelow = Math.Max(0, region.Bottom - anchor.Bottom);
        if (availableAbove >= desiredHeight || availableAbove >= availableBelow)
        {
            double height = Math.Min(desiredHeight, availableAbove);
            return (anchor.Y - height, height);
        }
        return (anchor.Bottom, Math.Min(desiredHeight, availableBelow));
    }

    private static double ResolveHorizontalSide(Rect anchor, Rect region, double width, bool preferRight)
    {
        double availableRight = Math.Max(0, region.Right - anchor.Right);
        double availableLeft = Math.Max(0, anchor.X - region.X);
        bool right = preferRight ? availableRight >= width || availableRight >= availableLeft
                                 : !(availableLeft >= width || availableLeft >= availableRight);
        return right
            ? Math.Max(region.X, Math.Min(anchor.Right, region.Right - width))
            : Math.Max(region.X, anchor.X - width);
    }

    private void OnContentChanged(UIElement? oldValue, UIElement? newValue)
        => ChangeLogicalChild(oldValue, newValue);

    protected override void OnLogicalChildTaken(Element child)
    {
        base.OnLogicalChildTaken(child);

        if (ReferenceEquals(Content, child))
        {
            Content = null;
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        // The popup manager takes a closing popup out of the tree; that detach is the only signal a
        // popup element gets, because the close notification goes to the owner rather than here.
        if (IsOpen && newRoot == null)
        {
            SetValue(IsOpenPropertyKey, false);
            Closed?.Invoke(this, new PopupClosedEventArgs(_closeKind));
        }
    }

    internal void NotifyClosing(PopupCloseKind kind) => _closeKind = kind;

    protected override Size MeasureContent(Size availableSize)
    {
        if (Content == null)
        {
            return default;
        }

        Content.Measure(availableSize);
        return Content.DesiredSize;
    }

    protected override void ArrangeContent(Rect bounds) => Content?.Arrange(bounds);

    protected override void RenderSubtree(IGraphicsContext context) => Content?.Render(context);

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        builder.Content(0);
        builder.Child(Content);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        return Content?.HitTest(point);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Content == null || visitor(Content);

    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor)
        => Content == null || visitor(Content);
}
