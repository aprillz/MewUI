using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Holds one tab's content for the tab's whole life. A <see cref="PaneLayer"/> shows it over the tab's group while the
/// tab is the visible one; the group's own view only draws the chrome around it, so moving or restructuring groups
/// never takes the content out of the window. It also remembers which element inside it had the keyboard focus, so
/// the focus can go back there when the pane is activated again.
/// </summary>
/// <remarks>A panel so the content is recorded as its own part of the scene, clipped to the host.</remarks>
internal sealed class PaneHost : Panel
{
    private WeakReference<UIElement>? _lastFocused;

    public PaneHost(TabNode tab, UIElement? content)
    {
        Tab = tab;
        ClipToBounds = true;
        if (content is not null)
        {
            Add(content);
        }
    }

    /// <summary>The tab whose content this is; a reloaded layout hands the host to the tab with the same id.</summary>
    public TabNode Tab { get; internal set; }

    /// <summary>The content given when the host was made, or null when the tab has none.</summary>
    public UIElement? Content => Children.Count > 0 ? Children[0] as UIElement : null;

    /// <summary>Whether the host had the keyboard focus when a layer last took it out of the window.</summary>
    internal bool HadFocusWhenDetached { get; set; }

    /// <summary>Raised when a mouse button goes down anywhere in the content and nothing inside handled it.</summary>
    public event Action<PaneHost>? Pressed;

    /// <summary>Raised when the keyboard focus moves into the content from outside it.</summary>
    public event Action<PaneHost>? FocusEntered;

    /// <summary>Takes the content out so it can live elsewhere once the tab is gone.</summary>
    public void ReleaseContent() => Clear();

    /// <summary>Notes the element inside that has the keyboard focus now, if any.</summary>
    internal void RememberFocus()
    {
        if (IsFocusWithin && FindVisualRoot() is Window window && window.FocusManager.FocusedElement is UIElement focused)
        {
            _lastFocused = new WeakReference<UIElement>(focused);
        }
    }

    /// <summary>
    /// Gives the keyboard focus to the element that last had it inside, or else to the first element inside that can
    /// take it. Does nothing while the focus is already inside or the host is not in a window.
    /// </summary>
    internal bool RestoreFocus()
    {
        if (IsFocusWithin)
        {
            return true;
        }
        if (FindVisualRoot() is not Window)
        {
            return false;
        }
        if (_lastFocused is not null && _lastFocused.TryGetTarget(out var remembered) && IsInside(remembered) && remembered.Focus())
        {
            return true;
        }
        return VisualTree.Find(this, element => element is UIElement candidate && candidate.Focusable
            && candidate.IsEffectivelyEnabled && candidate.IsVisible) is UIElement first && first.Focus();
    }

    private bool IsInside(Element element)
    {
        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }
        return false;
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == IsFocusWithinProperty && IsFocusWithin)
        {
            RememberFocus();
            FocusEntered?.Invoke(this);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Pressed?.Invoke(this);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        Content?.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeContent(Rect bounds) => Content?.Arrange(bounds);
}
