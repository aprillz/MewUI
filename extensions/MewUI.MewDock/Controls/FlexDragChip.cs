using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>Builds the labelled rounded chip used as the cross-window drag preview for a tab or tabset.</summary>
internal static class FlexDragChip
{
    /// <summary>A chip for a whole group drag: the lead tab's name plus the count of the other tabs travelling with
    /// it (e.g. "Report +2 tabs"). A single-tab group shows just the name.</summary>
    internal static UIElement BuildGroup(string name, int totalTabs)
    {
        int extra = totalTabs - 1;
        string title = extra switch
        {
            <= 0 => name,
            1 => $"{name} {MewUIDockString.DragChipOneMore.Value}",
            _ => $"{name} {string.Format(MewUIDockString.DragChipManyMore.Value, extra)}",
        };
        return Build(title);
    }

    internal static UIElement Build(string title)
    {
        var label = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis };
        label.WithTheme((theme, l) => l.Foreground = theme.Palette.WindowText);

        var chip = new Border
        {
            CornerRadius = 6,
            BorderThickness = 1,
            Padding = new Thickness(10, 5, 10, 5),
            Child = label,
        };
        chip.WithTheme((theme, border) =>
        {
            border.Background = theme.Palette.WindowBackground;
            border.BorderBrush = theme.Palette.ControlBorder;
        });
        return chip;
    }
}
