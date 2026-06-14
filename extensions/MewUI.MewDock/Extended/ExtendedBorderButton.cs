using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Extended;

/// <summary>
/// A tab in the Extended auto-hide edge strip. Auto-hide tools have no per-tab close (the caption owns it), are not
/// drag sources (clicking just reveals/toggles), and keep the closed rounded chrome while expanded because the
/// reveal is a floating overlay that does not connect to the strip.
/// </summary>
internal sealed class ExtendedBorderButton : FlexBorderButton
{
    public ExtendedBorderButton(TabNode tab, BorderNode border, FlexViewContext context)
        : base(tab, border, context)
    {
    }

    protected override bool ShowsCloseButton => false;

    protected override bool IsTabDragSource => false;

    protected override (CornerRadius Corner, Thickness Thickness) ButtonChrome() =>
        (new CornerRadius(CornerRadius), new Thickness(BorderThickness));
}
