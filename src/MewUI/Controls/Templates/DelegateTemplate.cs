namespace Aprillz.MewUI.Controls;

/// <summary>
/// An <see cref="IDataTemplate{TItem}"/> made of a build delegate, a bind delegate and an optional
/// unbind delegate.
/// </summary>
public sealed class DelegateTemplate<TItem> : IDataTemplate<TItem>
{
    private readonly Func<TemplateContext, FrameworkElement> _build;
    private readonly Action<FrameworkElement, TItem, int, TemplateContext> _bind;
    private readonly Action<FrameworkElement, TItem, int, TemplateContext>? _unbind;

    public DelegateTemplate(
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(bind);

        _build = build;
        _bind = bind;
        _unbind = unbind;
    }

    public FrameworkElement Build(TemplateContext context) => _build(context);

    public void Bind(FrameworkElement view, TItem item, int index, TemplateContext context)
        => _bind(view, item, index, context);

    public void Unbind(FrameworkElement view, TItem item, int index, TemplateContext context)
        => _unbind?.Invoke(view, item, index, context);
}
