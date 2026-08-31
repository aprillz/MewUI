namespace Aprillz.MewUI.Controls;

/// <summary>
/// Configures an item container once its content is bound, or releases what such a configuration
/// took. The context owns the lifetime of anything registered through it.
/// </summary>
/// <param name="container">The container holding the item.</param>
/// <param name="item">The item bound to the container.</param>
/// <param name="index">The item's index.</param>
/// <param name="context">
/// The container's template context. Bindings and event subscriptions registered on it are undone
/// before the next item is bound, so registering on every call is correct.
/// </param>
public delegate void PrepareContainerHandler<in TContainer, in TItem>(
    TContainer container,
    TItem item,
    int index,
    TemplateContext context);
