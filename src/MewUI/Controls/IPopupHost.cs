using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// One open popup tracked by <see cref="PopupManager"/>. Carries the host-agnostic
/// registration (element, owner, close policy) plus the in-surface visual handle.
/// </summary>
internal sealed class PopupEntry
{
    public required UIElement Element { get; init; }

    public required UIElement Owner { get; set; }

    public Rect Bounds { get; set; }

    public bool StaysOpen { get; set; }

    public PopupChrome? Chrome { get; set; }

    /// <summary>The OS surface hosting this popup when it is native-hosted; null for in-surface popups.</summary>
    public PopupWindow? NativeWindow { get; set; }

    /// <summary>The host that owns this popup's visuals (in-surface or native).</summary>
    public IPopupHost? Host { get; set; }
}

/// <summary>
/// Hosts the visual lifetime of a window's popups. <see cref="PopupManager"/> owns the
/// open-popup list and close policy and delegates visual hosting (attach, layout, render,
/// hit-test, environment forwarding) to the resolved host so alternative hosting strategies
/// (in-surface vs. a real OS window) plug in behind the same policy layer.
/// </summary>
internal interface IPopupHost
{
    /// <summary>Hosts a newly registered popup's visuals before it is added to the list.</summary>
    void Attach(PopupEntry entry, bool sizeToContent);

    /// <summary>Measures and arranges the popup, reconciling <see cref="PopupEntry.Bounds"/>.</summary>
    void Layout(PopupEntry entry);

    /// <summary>Repositions/resizes the popup to <paramref name="bounds"/>.</summary>
    void UpdateBounds(PopupEntry entry, Rect bounds);

    /// <summary>Re-points the popup's resolution context after its owner changes.</summary>
    void OnOwnerChanged(PopupEntry entry);

    /// <summary>Tears down the popup's visuals during close/dispose.</summary>
    void Detach(PopupEntry entry);

    void Render(IGraphicsContext context);

    UIElement? HitTest(Point point);

    bool HasLayoutDirty();

    void LayoutDirty();

    void NotifyThemeChanged(Theme oldTheme, Theme newTheme);

    void NotifyDpiChanged(uint oldDpi, uint newDpi);
}
