namespace Aprillz.MewUI.Controls;

/// <summary>
/// Wraps an item template so each item gets an <see cref="ItemContainer"/> around its content, and
/// runs the control's prepare and clear hooks against that container.
/// </summary>
/// <remarks>
/// Every realization engine builds and binds through <see cref="IDataTemplate"/>, so decorating the
/// template reaches all of them without touching container pooling.
/// </remarks>
internal sealed class ItemContainerTemplate : IDataTemplate
{
    private readonly IDataTemplate _inner;
    private readonly Func<int, bool> _isSelected;
    private readonly PrepareContainerHandler<ItemContainer, object?>? _prepare;
    private readonly PrepareContainerHandler<ItemContainer, object?>? _clear;

    public ItemContainerTemplate(
        IDataTemplate inner,
        Func<int, bool> isSelected,
        PrepareContainerHandler<ItemContainer, object?>? prepare,
        PrepareContainerHandler<ItemContainer, object?>? clear)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(isSelected);

        _inner = inner;
        _isSelected = isSelected;
        _prepare = prepare;
        _clear = clear;
    }

    public IDataTemplate Inner => _inner;

    public FrameworkElement Build(TemplateContext context)
        => new ItemContainer { Content = _inner.Build(context) };

    public void Bind(FrameworkElement view, object? item, int index, TemplateContext context)
    {
        var container = (ItemContainer)view;
        container.ResetForItem();
        container.SetIndex(index);
        container.SetIsSelected(_isSelected(index));

        _inner.Bind(ContentOf(container), item, index, context);
        _prepare?.Invoke(container, item, index, context);
    }

    public void Unbind(FrameworkElement view, object? item, int index, TemplateContext context)
    {
        var container = (ItemContainer)view;
        _clear?.Invoke(container, item, index, context);
        _inner.Unbind(ContentOf(container), item, index, context);
        container.SetIndex(-1);
        container.SetIsSelected(false);
    }

    private static FrameworkElement ContentOf(ItemContainer container)
        => (FrameworkElement)container.Content!;
}
