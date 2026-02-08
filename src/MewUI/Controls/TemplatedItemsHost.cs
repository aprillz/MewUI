using Aprillz.MewUI.Rendering;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Controls;

internal sealed class TemplatedItemsHost
{
    internal struct ItemsRangeLayout
    {
        public Rect ContentBounds;
        public int First;
        public int LastExclusive;
        public double ItemHeight;
        public double YStart;
        public double ItemRadius;
        public bool RebindExisting;
    }

    internal struct ItemsRangeOptions
    {
        public Action<IGraphicsContext, int, Rect>? BeforeItemRender;
        public Func<int, Rect, Rect>? GetContainerRect;
    }

    private readonly VirtualizedItemsPresenter _presenter;
    private readonly Func<int, object?> _getItem;
    private readonly Action _invalidateMeasureAndVisual;

    private IDataTemplate _itemTemplate;
    private readonly ConditionalWeakTable<FrameworkElement, TemplateContext> _contexts = new();
    private readonly Action<FrameworkElement>? _recycle;

    public ItemsRangeLayout Layout;
    public ItemsRangeOptions Options;

    public TemplatedItemsHost(
        FrameworkElement owner,
        Func<int, object?> getItem,
        Action invalidateMeasureAndVisual,
        IDataTemplate template,
        Action<FrameworkElement>? recycle = null)
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
            unbind: UnbindItemContainer);
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

    public void VisitRealized(Action<Element> visitor) => _presenter.VisitRealized(visitor);

    public void Render(IGraphicsContext context)
    {
        _presenter.RenderRange(
            context,
            Layout.ContentBounds,
            Layout.First,
            Layout.LastExclusive,
            Layout.ItemHeight,
            Layout.YStart,
            Options.BeforeItemRender,
            Options.GetContainerRect,
            Layout.RebindExisting);
    }

    public void Arrange()
    {
        _presenter.ArrangeRange(
            Layout.ContentBounds,
            Layout.First,
            Layout.LastExclusive,
            Layout.ItemHeight,
            Layout.YStart,
            Options.GetContainerRect,
            Layout.RebindExisting);
    }

    private FrameworkElement CreateItemContainer()
    {
        var ctx = new TemplateContext();
        var view = _itemTemplate.Build(ctx);
        _contexts.Add(view, ctx);
        return view;
    }

    private void BindItemContainer(FrameworkElement element, int index)
    {
        var item = _getItem(index);

        if (!_contexts.TryGetValue(element, out var ctx))
        {
            ctx = new TemplateContext();
            _contexts.Add(element, ctx);
        }

        ctx.Reset();
        _itemTemplate.Bind(element, item, index, ctx);
    }

    private void UnbindItemContainer(FrameworkElement element)
    {
        if (_contexts.TryGetValue(element, out var ctx))
        {
            ctx.Reset();
        }

        _recycle?.Invoke(element);
    }
}
