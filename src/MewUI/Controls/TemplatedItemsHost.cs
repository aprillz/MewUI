namespace Aprillz.MewUI.Controls;

internal sealed class TemplatedItemsHost : IDisposable
{
    internal struct ItemsRangeLayout
    {
        public Rect ContentBounds;
        public int First;
        public int LastExclusive;
        public double ItemHeight;
        public double YStart;
        public double ItemRadius;
        internal uint ItemBindingGeneration;
    }

    internal struct ItemsRangeOptions
    {
        public Func<int, Rect, Rect>? GetContainerRect;
    }

    private readonly VirtualizedItemsPresenter _presenter;
    private readonly Func<int, object?> _getItem;
    private readonly Action _invalidateMeasureAndVisual;

    private IDataTemplate _itemTemplate;
    private readonly TemplateContextTable _contexts = new();
    private readonly Action<FrameworkElement>? _recycle;

    public ItemsRangeLayout Layout;
    public ItemsRangeOptions Options;

    public TemplatedItemsHost(
        FrameworkElement owner,
        Func<int, object?> getItem,
        Action invalidateMeasureAndVisual,
        IDataTemplate template,
        Action<FrameworkElement>? recycle = null,
        Func<int, object?>? getKey = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(getItem);
        ArgumentNullException.ThrowIfNull(invalidateMeasureAndVisual);
        ArgumentNullException.ThrowIfNull(template);

        _getItem = getItem;
        _invalidateMeasureAndVisual = invalidateMeasureAndVisual;

        _itemTemplate = template;
        _recycle = recycle;

        _presenter = new VirtualizedItemsPresenter(
            owner,
            createContainer: CreateItemContainer,
            bind: BindItemContainer,
            unbind: UnbindItemContainer,
            keyAt: getKey);
    }

    public IDataTemplate ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_itemTemplate, value))
            {
                return;
            }

            _itemTemplate = value;
            _presenter.SetTemplate(CreateItemContainer, BindItemContainer, UnbindItemContainer, clearPool: true);
            _invalidateMeasureAndVisual();
        }
    }

    public void RecycleAll() => _presenter.RecycleAll();

    /// <inheritdoc cref="VirtualizedItemsPresenter.HoldRealizedForReset"/>
    public void HoldRealizedForReset() => _presenter.HoldRealizedForReset();

    public void Dispose() => _presenter.Dispose();

    public void VisitRealized(Action<Element> visitor) => _presenter.VisitRealized(visitor);

    public bool VisitRealized(Func<Element, bool> visitor) => _presenter.VisitRealized(visitor);

    /// <inheritdoc cref="VirtualizedItemsPresenter.VisitChildren"/>
    public bool VisitChildren(Func<Element, bool> visitor) => _presenter.VisitChildren(visitor);

    public void VisitRealized(Action<int, FrameworkElement> visitor) => _presenter.VisitRealized(visitor);

    public void Arrange()
    {
        _presenter.ArrangeRange(
            Layout.ContentBounds,
            Layout.First,
            Layout.LastExclusive,
            Layout.ItemHeight,
            Layout.YStart,
            Options.GetContainerRect,
            Layout.ItemBindingGeneration);
    }

    private FrameworkElement CreateItemContainer() => _contexts.Build(_itemTemplate);

    private void BindItemContainer(FrameworkElement element, int index)
        => _contexts.Bind(element, _itemTemplate, _getItem(index), index);

    private void UnbindItemContainer(FrameworkElement element)
    {
        _contexts.Unbind(element);
        _recycle?.Invoke(element);
    }
}
