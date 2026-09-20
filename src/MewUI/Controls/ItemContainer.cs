using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The element an items control places around one item's templated content, so an application can
/// attach behavior to the whole item rather than to the template it wrote.
/// </summary>
/// <remarks>
/// An items control places one around every realized item, and the container draws that item's
/// background over its row. Which background follows from its visual state through the triggers of
/// its style, as for any control: selected, or under the pointer when its owner shows a row hover. The
/// container is recycled across items, so the properties listed in <see cref="ResetForItem"/> are
/// returned to their defaults before each prepare. It also supplies <see cref="Item"/> as the
/// operand of commands invoked from within it, so a typed handler registered on an ancestor
/// receives the item a context menu or shortcut acted on.
/// </remarks>
public class ItemContainer : ContentControl, ICommandArgumentSource
{
    // The wrapper must be layout-transparent: a style without setters blocks the Control base style,
    // whose themed border thickness would otherwise inset the content by a pixel on every side.
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ItemContainer>(static () => CreateRowStyle(HOVER_ACCENT_SHARE));

    private const double HOVER_ACCENT_SHARE = 0.15;

    /// <summary>The style of a row: a hover tint of the given strength, and the selection over it.</summary>
    internal static Style CreateRowStyle(double hoverAccentShare)
        => new(typeof(ItemContainer))
        {
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground.Lerp(theme.Palette.Accent, hoverAccentShare))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.SelectionBackground)],
                },
            ],
        };

    private static readonly MewPropertyKey<int> IndexPropertyKey =
        MewProperty<int>.RegisterReadOnly<ItemContainer>(nameof(Index), -1);

    /// <summary>The index of the item this container currently holds, or -1 when it holds none.</summary>
    public static readonly MewProperty<int> IndexProperty = IndexPropertyKey.Property;

    private static readonly MewPropertyKey<bool> IsSelectedPropertyKey =
        MewProperty<bool>.RegisterReadOnly<ItemContainer>(nameof(IsSelected), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>Whether the item this container holds is selected.</summary>
    public static readonly MewProperty<bool> IsSelectedProperty = IsSelectedPropertyKey.Property;

    private static readonly MewPropertyKey<object?> ItemPropertyKey =
        MewProperty<object?>.RegisterReadOnly<ItemContainer>(nameof(Item), null);

    /// <summary>The item this container currently holds.</summary>
    public static readonly MewProperty<object?> ItemProperty = ItemPropertyKey.Property;

    /// <summary>
    /// Gets the index of the item this container currently holds, or -1 when it holds none.
    /// </summary>
    public int Index => GetValue(IndexProperty);

    /// <summary>
    /// Gets the item this container currently holds, or null when it holds none.
    /// </summary>
    public object? Item => GetValue(ItemProperty);

    object? ICommandArgumentSource.CommandArgument => Item;

    /// <summary>Gets whether the item this container holds is selected.</summary>
    public bool IsSelected => GetValue(IsSelectedProperty);

    internal void SetIndex(int index) => SetValue(IndexPropertyKey, index);

    internal void SetIsSelected(bool isSelected)
    {
        if (IsSelected != isSelected)
        {
            SetValue(IsSelectedPropertyKey, isSelected);
            InvalidateVisualState();
        }
    }

    private bool _isHovered;
    private bool _showsRowState = true;
    private bool _isAlternate;
    private Color _alternateBackground;

    /// <summary>Whether the owner shows this row as the one under the pointer.</summary>
    internal bool IsHovered => _isHovered;

    internal void SetIsHovered(bool isHovered)
    {
        if (_isHovered != isHovered)
        {
            _isHovered = isHovered;
            InvalidateVisualState();
        }
    }

    /// <summary>False for a row its owner keeps flat whatever happens to it: a header, a separator, a list without a row hover.</summary>
    internal bool ShowsRowState
    {
        get => _showsRowState;
        set
        {
            if (_showsRowState != value)
            {
                _showsRowState = value;
                InvalidateVisualState();
            }
        }
    }

    /// <summary>Whether this row is striped. The visual states have none for it, so the stripe is the owner's colour.</summary>
    internal bool IsAlternate => _isAlternate;

    internal void SetAlternate(bool isAlternate, Color background)
    {
        if (_isAlternate != isAlternate || _alternateBackground != background)
        {
            _isAlternate = isAlternate;
            _alternateBackground = background;
            InvalidateVisual();
        }
    }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();

        // The owner decides which row is under the pointer: it may show no row hover at all, and during a
        // keyboard move or a touch scroll the pointer is over a row the owner does not mean.
        var flags = state.Flags & ~VisualStateFlags.Hot;
        if (_isHovered && _showsRowState)
        {
            flags |= VisualStateFlags.Hot;
        }

        if (IsSelected && _showsRowState)
        {
            flags |= VisualStateFlags.Selected;
        }

        return state with { Flags = flags };
    }

    internal void SetItem(object? item) => SetValue(ItemPropertyKey, item);

    // The padding the owner leaves between a row and the container inside it.
    private Thickness _rowPadding;

    /// <summary>The padding between the row the owner lays out and this container, which the row backgrounds cover.</summary>
    internal Thickness RowPadding
    {
        get => _rowPadding;
        set
        {
            if (_rowPadding != value)
            {
                _rowPadding = value;
                InvalidateVisual();
            }
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // The owner arranges a container inside its row, inset by the item padding. The background
        // belongs to the row, so it reaches back out over that padding.
        var row = Bounds.Inflate(RowPadding);
        var background = Background;
        if (background.A == 0 && _isAlternate)
        {
            // A stripe runs the width of the list, square, under whatever the states put over it.
            context.FillRectangle(row, _alternateBackground);
        }

        DrawBackgroundAndBorder(context, row, background, BorderBrush, BorderThickness, CornerRadius);
    }

    /// <summary>
    /// Clears the local values a prepare hook may have assigned, so a recycled container does not
    /// carry the previous item's state. Bindings survive: the template context clears those.
    /// </summary>
    internal void ResetForItem()
    {
        ClearLocalValue(ContextMenuProperty);
        ClearLocalValue(ToolTipProperty);
        ClearLocalValue(IsEnabledProperty);
        ClearLocalValue(IsHitTestVisibleProperty);
        ClearLocalValue(CursorProperty);
        ClearLocalValue(OpacityProperty);
        ClearLocalValue(TagProperty);
        ClearLocalValue(CornerRadiusProperty);
        SetIsHovered(false);
        SetAlternate(false, default);
        ShowsRowState = true;
    }
}
