namespace Aprillz.MewUI.Controls;

/// <summary>
/// A horizontal cluster of segments joined into a single rounded frame (toolbar / split action).
/// Shares the segment model and chrome of <see cref="SegmentedBase"/> but carries no selection: each
/// segment is independent. Populate it with <c>Items</c> + <c>PrepareContainer</c>, wiring each
/// segment's <see cref="SegmentButton.Click"/> (command) and/or <see cref="SegmentButton.IsCheckable"/>
/// (independent toggle). For a single mutually exclusive choice use <see cref="SegmentedControl"/>.
/// </summary>
public sealed class ButtonGroup : SegmentedBase
{
    public ButtonGroup() : base(SegmentSizing.Auto)
    {
        // Segments take their own content width (toolbar-like); a slightly larger padding gives a
        // button-like feel versus the compact segmented strip default.
        ItemPadding = new Thickness(12, 6);
    }

    protected override void OnSegmentCreated(SegmentButton button)
    {
        // Independent command buttons: each segment is its own Tab stop (toolbar), unlike
        // SegmentedControl where the container owns focus and selection.
        button.Focusable = true;
    }
}
