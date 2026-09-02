namespace Aprillz.MewUI.Controls;

/// <summary>
/// The element an items control places around one item's templated content, so an application can
/// attach behavior to the whole item rather than to the template it wrote.
/// </summary>
/// <remarks>
/// A control creates these only while a <c>PrepareContainer</c> or <c>ClearContainer</c> hook is
/// registered; without one the templated content is the container and no wrapper exists. The
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
        MewProperty<bool>.RegisterReadOnly<ItemContainer>(nameof(IsSelected), false);

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

    /// <summary>
    /// Gets whether the item this container holds is selected. The control keeps this current; the
    /// container does not draw the selection itself.
    /// </summary>
    public bool IsSelected => GetValue(IsSelectedProperty);

    internal void SetIndex(int index) => SetValue(IndexPropertyKey, index);

    internal void SetIsSelected(bool isSelected) => SetValue(IsSelectedPropertyKey, isSelected);

    internal void SetItem(object? item) => SetValue(ItemPropertyKey, item);

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
    }
}
