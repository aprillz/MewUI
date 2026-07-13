using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Hosts popups inside the owner window's own surface: each popup is wrapped in a
/// <see cref="PopupChrome"/>, parented to the window, laid out in client coordinates,
/// and drawn/hit-tested as an extra top-level layer of the one backend surface.
/// </summary>
internal sealed class InSurfacePopupHost : IPopupHost
{
    private readonly Window _window;
    private readonly List<PopupEntry> _popups;

    internal InSurfacePopupHost(Window window, List<PopupEntry> popups)
    {
        _window = window;
        _popups = popups;
    }

    public void Attach(PopupEntry entry, bool sizeToContent)
    {
        // Chrome is already attached (and the popup style-resolved) by PopupManager before placement
        // was measured; the re-derivation below only clamps a content-sized popup's width to the
        // owner surface.
        if (sizeToContent)
        {
            entry.Bounds = PopupHostSupport.ResizeToContentWidth(entry.Element, entry.Bounds, _window.ClientSize.Width);
        }
    }

    public void Layout(PopupEntry entry)
    {
        if (entry.Chrome != null)
        {
            // Chrome bounds include shadow padding around the content area.
            var chromeBounds = entry.Bounds.Inflate(PopupChrome.ShadowPadding);
            entry.Chrome.Measure(new Size(chromeBounds.Width, chromeBounds.Height));
            entry.Chrome.Arrange(chromeBounds);

            // Keep the stored bounds consistent with the child's actually arranged (layout-rounded) bounds,
            // otherwise hit-testing (e.g. mouse wheel on popup content) can miss by sub-pixel rounding.
            entry.Bounds = entry.Chrome.Child.Bounds;
        }
        else
        {
            entry.Element.Measure(new Size(entry.Bounds.Width, entry.Bounds.Height));
            entry.Element.Arrange(entry.Bounds);
            entry.Bounds = entry.Element.Bounds;
        }
    }

    public void UpdateBounds(PopupEntry entry, Rect bounds)
    {
        entry.Bounds = bounds;
        Layout(entry);
    }

    public void OnOwnerChanged(PopupEntry entry)
    {
        if (entry.Chrome is PopupChrome chrome)
        {
            chrome.ContextParentOverride = entry.Owner;
        }
    }

    public void Detach(PopupEntry entry) => PopupHostSupport.DetachChrome(entry);

    public void Render(IGraphicsContext context)
    {
        // Popups render last (on top).
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this))
            {
                continue;
            }

            if (entry.Chrome != null)
            {
                entry.Chrome.Render(context);
            }
            else
            {
                entry.Element.Render(context);
            }
        }
    }

    public UIElement? HitTest(Point point)
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this) || !entry.Bounds.Contains(point))
            {
                continue;
            }

            var hit = entry.Element.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return null;
    }

    public bool HasLayoutDirty()
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this))
            {
                continue;
            }

            var element = entry.Element;
            if (element.IsMeasureDirty || element.IsArrangeDirty)
            {
                return true;
            }
        }

        return false;
    }

    public void LayoutDirty()
    {
        if (_popups.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this) || !entry.Element.IsVisible)
            {
                continue;
            }

            if (!entry.Element.IsMeasureDirty && !entry.Element.IsArrangeDirty)
            {
                continue;
            }

            Layout(entry);
        }
    }

    public void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this))
            {
                continue;
            }

            if (entry.Chrome != null)
            {
                entry.Chrome.NotifyThemeChanged(oldTheme, newTheme);
            }

            if (entry.Element is FrameworkElement fe)
            {
                fe.NotifyThemeChanged(oldTheme, newTheme);
            }
        }
    }

    public void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this))
            {
                continue;
            }

            var root = (UIElement?)entry.Chrome ?? entry.Element;
            PopupHostSupport.ApplyDpiChange(root, oldDpi, newDpi);
        }
    }
}
