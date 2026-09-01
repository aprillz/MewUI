using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Hosts a modal dialog inside its owner's own surface, for a platform that cannot give the dialog
/// a window of its own. The dialog's content is centred over a scrim that swallows every pointer
/// that does not land on it, which is what makes the dialog modal without an owner window to disable.
/// </summary>
internal sealed class InSurfaceDialogHost : UIElement, IVisualTreeHost
{
    // Dimming behind a modal is not a themed colour anywhere else, so it is stated here.
    private static readonly Color _scrim = Color.FromArgb(96, 0, 0, 0);

    private readonly Window _dialog;
    private readonly PopupChrome _chrome;

    internal InSurfaceDialogHost(Window dialog, UIElement content, Window owner)
    {
        _dialog = dialog;

        // The chrome draws a shadow and expects its child to paint itself, which a dialog's content
        // never had to do while its window painted behind it.
        var surface = new Border { Child = content, Background = dialog.EffectiveOpaqueBackground };
        _chrome = new PopupChrome(surface);
        Parent = owner;
        _chrome.Parent = this;
    }

    internal Window Dialog => _dialog;

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_chrome);

    protected override Size MeasureOverride(Size availableSize)
    {
        _chrome.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var desired = _chrome.DesiredSize;
        double width = Math.Min(desired.Width, finalSize.Width);
        double height = Math.Min(desired.Height, finalSize.Height);
        _chrome.Arrange(new Rect(
            (finalSize.Width - width) / 2,
            (finalSize.Height - height) / 2,
            width,
            height));
        return finalSize;
    }

    // A pointer that misses the dialog is absorbed rather than passed through, so the owner stays
    // untouchable for as long as the dialog is up.
    protected override UIElement? OnHitTest(Point point)
        => _chrome.HitTest(point) ?? (Bounds.Contains(point) ? this : null);

    protected override void OnRender(IGraphicsContext context)
    {
        context.FillRectangle(Bounds, _scrim);
        _chrome.Render(context);
    }
}
