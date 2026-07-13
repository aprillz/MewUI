using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Chrome-lifecycle helpers shared by the popup hosts. Both the in-surface and native hosts keep the
/// popup subtree rooted in the owner window (Parent = owner window, ContextParentOverride = owner), so
/// resolution, DPI, theme, and visual root are identical regardless of where the pixels are drawn.
/// </summary>
internal static class PopupHostSupport
{
    private static readonly MewProperty FontFamilyProperty = TextElement.FontFamilyProperty;

    /// <summary>
    /// Wraps the popup in a <see cref="PopupChrome"/>, roots it in the owner window, and refreshes its
    /// DPI/theme/style so a cached or cross-window popup measures against the owner context.
    /// </summary>
    internal static PopupChrome AttachChrome(Window window, PopupEntry entry)
    {
        var popup = entry.Element;

        // Popups can be cached/reused (e.g. ComboBox keeps a ListBox instance even while closed).
        // If a popup is moved between windows (or the window DPI differs), ensure the popup updates its
        // DPI-sensitive caches (fonts, layout) before measuring/arranging.
        uint oldDpi = popup.GetDpiCached();
        var oldTheme = popup is FrameworkElement popupElement
            ? popupElement.ThemeInternal
            : window.ThemeInternal;

        // Wrap in PopupChrome so the drop shadow renders within the chrome's layout bounds,
        // avoiding clipping by ancestor clip regions.
        var chrome = new PopupChrome(popup);

        // Before attach so attach-time style/inherited resolution already sees the owner context.
        chrome.ContextParentOverride = entry.Owner;
        chrome.Parent = window;
        chrome.AttachChild();
        entry.Chrome = chrome;

        ApplyDpiChange(chrome, oldDpi, window.Dpi);
        ApplyThemeChange(chrome, oldTheme, window.ThemeInternal);

        // Now that the popup is in the visual tree, inherited properties (e.g. FontFamily)
        // are resolvable. Force style re-resolution and measure invalidation so that any
        // measurement done before attachment (e.g. MeasureToolTip) is corrected.
        ForceStyleAndMeasureRefresh(popup);

        return chrome;
    }

    internal static void DetachChrome(PopupEntry entry)
    {
        if (entry.Chrome != null)
        {
            entry.Chrome.DetachChild();
            entry.Chrome.Parent = null;
            entry.Chrome.ContextParentOverride = null;
        }
        else
        {
            entry.Element.Parent = null;
        }
    }

    internal static void ApplyDpiChange(UIElement popup, uint oldDpi, uint newDpi)
    {
        if (oldDpi == 0 || newDpi == 0 || oldDpi == newDpi)
        {
            return;
        }

        // Clear DPI caches again (Parent assignment already does this, but be defensive for future changes),
        // and notify controls so they can recreate DPI-dependent resources (fonts, etc.).
        popup.ClearDpiCacheDeep();
        VisualTree.Visit(popup, e =>
        {
            e.ClearDpiCache();
            if (e is FrameworkElement fe)
            {
                fe.NotifyDpiChanged(oldDpi, newDpi);
            }
        });
    }

    internal static void ApplyThemeChange(UIElement popup, Theme oldTheme, Theme newTheme)
    {
        if (oldTheme == newTheme)
        {
            return;
        }

        VisualTree.Visit(popup, e =>
        {
            if (e is FrameworkElement element)
            {
                element.NotifyThemeChanged(oldTheme, newTheme);
            }
        });
    }

    internal static void ForceStyleAndMeasureRefresh(UIElement popup)
    {
        VisualTree.Visit(popup, e =>
        {
            if (e is Control c)
            {
                c.ResolveAndApplyStyle();
                c.InvalidateFontCache(FontFamilyProperty);
            }

            e.InvalidateMeasure();
        });
    }

    /// <summary>
    /// Re-derives a content-sized popup's width from a connected measure (post-attach fonts), clamped to
    /// <paramref name="availableWidth"/>, keeping the caller's height constraint and vertical placement.
    /// </summary>
    internal static Rect ResizeToContentWidth(UIElement popup, Rect bounds, double availableWidth)
    {
        popup.Measure(new Size(double.PositiveInfinity, bounds.Height));
        double width = Math.Min(popup.DesiredSize.Width, availableWidth);
        if (width.Equals(bounds.Width))
        {
            return bounds;
        }
        double x = bounds.X;
        if (x + width > availableWidth)
        {
            x = Math.Max(0, availableWidth - width);
        }
        return new Rect(x, bounds.Y, width, bounds.Height);
    }
}
