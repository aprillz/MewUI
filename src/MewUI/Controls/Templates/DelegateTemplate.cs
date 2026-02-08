namespace Aprillz.MewUI.Controls;

public sealed class DelegateTemplate<TItem> : IDataTemplate<TItem>
{
    private readonly Func<TemplateContext, FrameworkElement> _build;
    private readonly Action<FrameworkElement, TItem, int, TemplateContext> _bind;

    public DelegateTemplate(
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(bind);

        _build = build;
        _bind = bind;
    }

    public FrameworkElement Build(TemplateContext context) => _build(context);

    public void Bind(FrameworkElement view, TItem item, int index, TemplateContext context)
        => _bind(view, item, index, context);

    void IDataTemplate.Bind(FrameworkElement view, object? item, int index, TemplateContext context)
    {
        if (item is null)
        {
            _bind(view, default!, index, context);
            return;
        }

        if (item is not TItem typed)
        {
            throw new InvalidCastException($"Template expected item type '{typeof(TItem).Name}', but got '{item.GetType().Name}'.");
        }

        _bind(view, typed, index, context);
    }
}

