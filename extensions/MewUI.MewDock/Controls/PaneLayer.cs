using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>A view that gives a tab's content its place: the size and the rectangle inside the view's chrome.</summary>
internal interface IPaneContentOwner
{
    /// <summary>The size the content gets, known after the owner was measured.</summary>
    Size ContentSize { get; }

    /// <summary>Where the content goes, known after the owner was arranged.</summary>
    Rect ContentArea { get; }
}

/// <summary>
/// The layer of a layout view that shows the visible tabs' content over their groups. Each content keeps its place in
/// this layer while the groups around it are split, moved, renamed or closed, so it keeps its keyboard focus and what
/// it has drawn; only a tab that becomes hidden leaves the layer.
/// </summary>
/// <remarks>
/// A panel so each content is recorded as its own part of the scene. The hosts of one layer do not overlap, so the
/// order they are drawn in does not matter and a host is never moved within the layer.
/// </remarks>
internal sealed class PaneLayer : Panel
{
    /// <summary>Shows exactly <paramref name="visible"/>, keeping every host that stays where it is.</summary>
    public void Show(IReadOnlyList<PaneHost> visible)
    {
        foreach (var child in Children.ToList())
        {
            if (child is PaneHost host && !Contains(visible, host))
            {
                Detach(host);
            }
        }

        foreach (var host in visible)
        {
            if (ReferenceEquals(host.Parent, this))
            {
                continue;
            }
            // A host another layer still shows (another window, or the other layer of this view) leaves it first.
            if (host.Parent is PaneLayer other)
            {
                other.Detach(host);
            }
            Add(host);
            // Content that had the keyboard focus when it left its place gets it back, unless something else took it.
            if (host.HadFocusWhenDetached)
            {
                host.HadFocusWhenDetached = false;
                if (FindVisualRoot() is Window window && window.FocusManager.FocusedElement is null)
                {
                    host.RestoreFocus();
                }
            }
        }
    }

    private void Detach(PaneHost host)
    {
        // Leaving the window drops the keyboard focus; note where it was so it can come back with the content.
        if (host.IsFocusWithin)
        {
            host.RememberFocus();
            host.HadFocusWhenDetached = true;
        }
        Remove(host);
    }

    private static bool Contains(IReadOnlyList<PaneHost> hosts, PaneHost host)
    {
        foreach (var candidate in hosts)
        {
            if (ReferenceEquals(candidate, host))
            {
                return true;
            }
        }
        return false;
    }

    private static IPaneContentOwner? OwnerOf(PaneHost host) => host.Tab.Parent?.View as IPaneContentOwner;

    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var child in Children)
        {
            if (child is PaneHost host)
            {
                host.Measure(OwnerOf(host)?.ContentSize ?? Size.Empty);
            }
        }
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        foreach (var child in Children)
        {
            if (child is PaneHost host)
            {
                host.Arrange(OwnerOf(host)?.ContentArea ?? Rect.Empty);
            }
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        // The layer is not a target itself: where no content is, the pointer reaches the groups underneath.
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }
        var children = Children;
        for (int index = children.Count - 1; index >= 0; index--)
        {
            if (children[index] is UIElement host && host.HitTest(point) is UIElement hit)
            {
                return hit;
            }
        }
        return null;
    }
}
