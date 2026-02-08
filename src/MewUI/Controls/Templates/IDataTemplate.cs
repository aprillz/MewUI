namespace Aprillz.MewUI.Controls;

public interface IDataTemplate
{
    FrameworkElement Build(TemplateContext context);

    void Bind(FrameworkElement view, object? item, int index, TemplateContext context);
}

public interface IDataTemplate<in TItem> : IDataTemplate
{
    void Bind(FrameworkElement view, TItem item, int index, TemplateContext context);
}

