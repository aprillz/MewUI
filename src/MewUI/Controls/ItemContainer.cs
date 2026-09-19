using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The element an items control places around one item's templated content, so an application can
/// attach behavior to the whole item rather than to the template it wrote.
/// </summary>
/// <remarks>
/// An items control places one around every realized item, and the container owns that item's
/// visual state: it draws its own selection, hover and alternating backgrounds. The
/// container is recycled across items, so the properties listed in <see cref="ResetForItem"/> are
/// returned to their defaults before each prepare. It also supplies <see cref="Item"/> as the
/// operand of commands invoked from within it, so a typed handler registered on an ancestor
/// receives the item a context menu or shortcut acted on.
/// </remarks>
public class ItemContainer : ContentControl, ICommandArgumentSource
{
    // The wrapper must be layout-transparent: an empty style blocks the Control base style, whose
    // themed border thickness would otherwise inset the content by a pixel on every side.
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ItemContainer>(static () => new Style(typeof(ItemContainer)));

    private static readonly MewPropertyKey<int> IndexPropertyKey =
        MewProperty<int>.RegisterReadOnly<ItemContainer>(nameof(Index), -1);

    /// <summary>The index of the item this container currently holds, or -1 when it holds none.</summary>
    public static readonly MewProperty<int> IndexProperty = IndexPropertyKey.Property;

    private static readonly MewPropertyKey<bool> IsSelectedPropertyKey =
        MewProperty<bool>.RegisterReadOnly<ItemContainer>(nameof(IsSelected), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>Whether the item this container holds is selected.</summary>
    public static readonly MewProperty<bool> IsSelectedProperty = IsSelectedPropertyKey.Property;

    private static readonly MewPropertyKey<bool> IsHoveredPropertyKey =
        MewProperty<bool>.RegisterReadOnly<ItemContainer>(nameof(IsHovered), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>Whether the pointer is over the item this container holds.</summary>
    public static readonly MewProperty<bool> IsHoveredProperty = IsHoveredPropertyKey.Property;

    private static readonly MewPropertyKey<bool> IsAlternatePropertyKey =
        MewProperty<bool>.RegisterReadOnly<ItemContainer>(nameof(IsAlternate), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>Whether this container holds an item on an alternating row.</summary>
    public static readonly MewProperty<bool> IsAlternateProperty = IsAlternatePropertyKey.Property;

    /// <summary>Background drawn while the item is selected.</summary>
    public static readonly MewProperty<Color> SelectionBackgroundProperty =
        MewProperty<Color>.Register<ItemContainer>(nameof(SelectionBackground), Color.Transparent,
            MewPropertyOptions.AffectsRender);

    /// <summary>Background drawn while the pointer is over the item and it is not selected.</summary>
    public static readonly MewProperty<Color> HoverBackgroundProperty =
        MewProperty<Color>.Register<ItemContainer>(nameof(HoverBackground), Color.Transparent,
            MewPropertyOptions.AffectsRender);

    /// <summary>Background drawn on an alternating row that is neither selected nor hovered.</summary>
    public static readonly MewProperty<Color> AlternateBackgroundProperty =
        MewProperty<Color>.Register<ItemContainer>(nameof(AlternateBackground), Color.Transparent,
            MewPropertyOptions.AffectsRender);

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

    /// <summary>Gets whether the pointer is over the item this container holds.</summary>
    public bool IsHovered => GetValue(IsHoveredProperty);

    /// <summary>Gets whether this container holds an item on an alternating row.</summary>
    public bool IsAlternate => GetValue(IsAlternateProperty);

    /// <summary>Gets or sets the background drawn while the item is selected.</summary>
    public Color SelectionBackground
    {
        get => GetValue(SelectionBackgroundProperty);
        set => SetValue(SelectionBackgroundProperty, value);
    }

    /// <summary>Gets or sets the background drawn while the pointer is over an unselected item.</summary>
    public Color HoverBackground
    {
        get => GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }

    /// <summary>Gets or sets the background drawn on an alternating row.</summary>
    public Color AlternateBackground
    {
        get => GetValue(AlternateBackgroundProperty);
        set => SetValue(AlternateBackgroundProperty, value);
    }

    internal void SetIndex(int index) => SetValue(IndexPropertyKey, index);

    internal void SetIsSelected(bool isSelected) => SetValue(IsSelectedPropertyKey, isSelected);

    internal void SetIsHovered(bool isHovered) => SetValue(IsHoveredPropertyKey, isHovered);

    internal void SetIsAlternate(bool isAlternate) => SetValue(IsAlternatePropertyKey, isAlternate);

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
        // The selection, hover and alternating backgrounds sit under the item's content, which the
        // base implementation and the templated child draw afterwards.
        if (IsSelected)
        {
            FillItemBackground(context, SelectionBackground, CornerRadius);
        }
        else if (IsHovered)
        {
            FillItemBackground(context, HoverBackground, CornerRadius);
        }
        else if (IsAlternate)
        {
            FillItemBackground(context, AlternateBackground, 0);
        }

        base.OnRender(context);
    }

    private void FillItemBackground(IGraphicsContext context, Color background, double cornerRadius)
    {
        if (background.A == 0)
        {
            return;
        }

        // The owner arranges a container inside its row, inset by the item padding. The backgrounds
        // belong to the row, so they reach back out over that padding.
        var row = Bounds.Inflate(RowPadding);

        if (cornerRadius > 0)
        {
            context.FillRoundedRectangle(row, cornerRadius, cornerRadius, background);
        }
        else
        {
            context.FillRectangle(row, background);
        }
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
        ClearLocalValue(SelectionBackgroundProperty);
        ClearLocalValue(HoverBackgroundProperty);
        ClearLocalValue(AlternateBackgroundProperty);
        ClearLocalValue(CornerRadiusProperty);
        SetIsHovered(false);
        SetIsAlternate(false);
    }
}
