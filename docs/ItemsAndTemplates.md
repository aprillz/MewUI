# Items and Templates

This document describes the currently implemented item and template system in MewUI.

## Overview

Templates are used to convert items into reusable `FrameworkElement` instances. The core flow is:

1. Build a view once for a container.
2. Bind data into the view when it is realized.
3. Reset tracked resources when the view is recycled.

This is the mechanism used by item controls such as `ListBox`, `ComboBox`, `TreeView`, and `GridView`.

## Items Overview

MewUI item controls are driven by an `ItemsView` abstraction. In practice:

1. `Items(...)` creates or wraps an `ItemsView`.
2. The control asks `ItemsView` for item count, text, and selection.
3. Templates create and bind views for visible items.

`ItemsView` is the data side, and templates are the view side. They are designed to be used together.

## Core Types

### IDataTemplate

`IDataTemplate` defines the contract for building and binding views.

```csharp
public interface IDataTemplate
{
    FrameworkElement Build(TemplateContext context);
    void Bind(FrameworkElement view, object? item, int index, TemplateContext context);
    void Unbind(FrameworkElement view, object? item, int index, TemplateContext context);
}
```

`Unbind` runs before the container takes its next item. The default implementation is empty, so it is only for what the context cannot undo for you, such as a subscription made with `+=`.

`IDataTemplate<TItem>` provides type-safe binding.

```csharp
public interface IDataTemplate<in TItem> : IDataTemplate
{
    void Bind(FrameworkElement view, TItem item, int index, TemplateContext context);
    void Unbind(FrameworkElement view, TItem item, int index, TemplateContext context);
}
```

### DelegateTemplate

`DelegateTemplate<TItem>` is the standard implementation. It is also where the default-template behavior is best understood: a simple build creates a `TextBlock`, and bind assigns text (from `GetText` or `ToString()`), using `TemplateContext` for fast lookup when needed.

```csharp
var template = new DelegateTemplate<Person>(
    build: ctx =>
    {
        // Default-template shape: a single TextBlock.
        // Use TemplateContext when you want named access and reuse.
        return new TextBlock().Register(ctx, "Text");
    },
    bind: (view, item, index, ctx) =>
    {
        ctx.Get<TextBlock>("Text").Text = item.Name;
    });
```

You can also return the view directly when you do not need `TemplateContext`:

```csharp
var template = new DelegateTemplate<Person>(
    build: _ => new TextBlock(),
    bind: (view, item, index, _) => ((TextBlock)view).Text = item.Name);
```

### TemplateContext

Each container gets one context that lives as long as the container does. It does two things: it names elements, and it undoes the bindings and event subscriptions you make each time an item is bound.

```csharp
public sealed class TemplateContext : IDisposable
{
    public void Register<T>(string name, T element) where T : UIElement;
    public T Get<T>(string name) where T : UIElement;

    public void Bind<T>(MewObject target, MewProperty<T> property, ObservableValue<T> source, BindingMode? mode = null);
    // Converter and BindingPath overloads also exist.

    public void Subscribe<TSource, THandler>(
        TSource source, Action<TSource, THandler> add, Action<TSource, THandler> remove, THandler handler)
        where TSource : class where THandler : Delegate;

    public void Reset();
}
```

Register names in `Build`, look them up in `Bind`:

```csharp
ctx.Get<TextBlock>("Name").Text = item.Name;
```

Make bindings and subscriptions in `Bind`. Doing so on every bind is correct; they do not accumulate.

```csharp
ctx.Subscribe(
    item,
    static (source, handler) => source.Changed += handler,
    static (source, handler) => source.Changed -= handler,
    () => Refresh());
```

## Template Lifecycle

1. `Build` is called once when a container is created.
2. `Bind` is called when a container is realized for an item. **`Unbind` for the previous item and context cleanup run first.**
3. The cycle repeats whenever the container is reused.

What cleanup (`Reset`) undoes, and what it does not:

| | Undone by cleanup |
|---|---|
| A binding made with `ctx.Bind` | Yes. `ClearBinding` runs |
| A subscription made with `ctx.Subscribe` | Yes. The `remove` you supplied runs |
| A name registered with `ctx.Register` | **No.** Names last as long as the container |
| A subscription made with `+=` | **No.** Detach it yourself in `Unbind` |
| A property value assigned directly | **No.** Assign it on every `Bind` |

Cleanup runs in reverse order of registration. If one entry throws, the rest still run and the first exception is rethrown at the end.

That last row matters most. Assigning a property **conditionally** leaves the previous item's value on a reused container.

```csharp
// Wrong: an item that fails the condition keeps the previous item's color
bind: (view, item, _, _) => { if (item.IsUrgent) ((TextBlock)view).Foreground = Colors.Red; }

// Right: assign in both cases
bind: (view, item, _, _) => ((TextBlock)view).Foreground = item.IsUrgent ? Colors.Red : Colors.Black;
```

## When Bind Runs Again

`Bind` runs every time a container is realized for an item. Scrolling an item out of view recycles its container, so scrolling back in binds again. Changes to the item collection, the template, `ItemPadding`, or the theme rebind every container currently on screen.

A selection change does not rebind. The control paints the selection itself and only updates the container's `IsSelected` (see the container hook below). If something inside the template must follow the selection, subscribe to that value instead of branching in `Bind`.

It is skipped in one case only: a container that is still on screen for the same item, when nothing has invalidated the bindings. A plain relayout does not rebind.

Treat `Bind` as something that runs often. Keep it cheap and free of allocations you could make in `Build`.

## The container hook: PrepareContainer

Sometimes behavior belongs to the **whole** row: a context menu that opens wherever the row is right-clicked, a row tooltip, a row cursor or drag source. Attaching it to the template root only covers the area the template occupies and repeats the same code in every template. `PrepareContainer` hands you the container the control keeps per item, so you attach once, in one place.

```csharp
list.PrepareContainer<ChatMessage>((container, message, index, ctx) => container.ContextMenu = messageMenu);
```

The callback receives `(container, item, index, context)` and runs after the template's `Bind`. `context` is that container's `TemplateContext`. Its counterpart `ClearContainer` runs before the container takes another item; most hooks do not need it.

| Control | Container | Without a hook |
|---|---|---|
| `ListBox`, `ItemsControl` | `ItemContainer`, wrapped around the template root only while a hook is registered | The template root is the container; no wrapper |
| `TreeView` | `ItemContainer` covering the whole row, indent and expander included, with the content padded past them | The template root sits in the content area only |
| `GridView` | `GridViewRow`, the row element the grid always has | Unchanged |

Applications that do not use the hook pay for no extra element. Registering or removing a hook rebuilds the containers.

### What the container tells you

`ItemContainer` and `GridViewRow` expose `Index`, `Item`, and `IsSelected` read-only. `IsSelected` is kept current as the selection changes, but the container does not paint it.

`Item` also feeds the command system. When a context menu attached to the container, or a button inside it, invokes a command, a typed handler receives that item as its argument. See the argument section of [CommandSystem.md](CommandSystem.md).

### What is reset

Containers are recycled between items. These properties, when a hook assigns them directly, return to their defaults before the next bind: `ContextMenu`, `ToolTip`, `IsEnabled`, `IsHitTestVisible`, `Cursor`, `Opacity`, `Tag`. Every other property follows the template rule: assign it on every bind or bind it through `ctx.Bind`.

Whatever you attach with `ctx.Bind` and `ctx.Subscribe` is undone by the context. Only a subscription made with `+=` needs a `ClearContainer` to detach it.

### The hook runs on bind only

The hook runs at the same moment as `Bind`. Selection changes and control state such as collapsed or expanded do not rebind, so a value that depends on them must follow through a subscription made inside the hook.

```csharp
list.PrepareContainer<TodoItem>((container, todo, _, ctx) =>
{
    container.Opacity = todo.IsDone.Value ? 0.6 : 1;
    ctx.Subscribe(todo.IsDone,
        static (source, handler) => source.Changed += handler,
        static (source, handler) => source.Changed -= handler,
        () => container.Opacity = todo.IsDone.Value ? 0.6 : 1);
});
```

### The TreeView container

A `TreeView` container covers the whole row, so menus and tooltips work over the indent and the expander as well. The content is pushed past the indent by the container's `Padding`, so a hook must leave `Padding` alone. Expander clicks and keyboard navigation keep working.

### ContextMenu and ToolTip

`ContextMenu` and `ToolTip` are `FrameworkElement` properties, so they attach to containers and to non-control elements such as shapes alike. A right-click travels from the element toward its ancestors, so pressing any element inside the template opens the menu attached to the container.

## TemplatedItemsHost and Virtualization

`TemplatedItemsHost` is the internal helper used by item controls.

Responsibilities:

1. Create containers using `IDataTemplate.Build`.
2. Bind items using `IDataTemplate.Bind`.
3. Reset `TemplateContext` when reusing containers.
4. Delegate actual virtualization and layout to `VirtualizedItemsPresenter`.

This is the common path used by `ListBox`, `ComboBox`, `TreeView`, and `GridView`.

## Control Usage

### ListBox

```csharp
new ListBox()
    .Items(people, p => p.Name)
    .ItemTemplate(template);
```

If you do not set `ItemTemplate`, the default template is used (`TextBlock` + `GetText`/`ToString()`).

```csharp
// Default template usage
new ListBox().Items(people, p => p.Name);
```

The second argument of `Items(...)` is a text selector. It tells the default template
what string to display for each item (used by `TextBlock`).

### ComboBox

```csharp
new ComboBox()
    .Items(people, p => p.Name)
    .ItemTemplate(template);
```

```csharp
// Default template usage
new ComboBox().Items(people, p => p.Name);
```

The second argument of `Items(...)` is a text selector used by the default template.

### TreeView

```csharp
new TreeView()
    .Items(treeItems)
    .ItemTemplate(template);
```

```csharp
// Default template usage
new TreeView().Items(treeItems);
```

You can also pass a hierarchical data source directly:

```csharp
new TreeView().Items(
    roots,
    childrenSelector: n => n.Children,
    textSelector: n => n.Name,
    keySelector: n => n.Id);
```

### GridView

GridView columns use templates for cells.

```csharp
var grid = new GridView();
grid.Columns(
    new GridViewColumn<Person>()
        .Header("Name")
        .Width(160)
        .Bind(
            build: ctx => new TextBlock().Register(ctx, "Text"),
            bind: (TextBlock t, Person p, int _, TemplateContext __) => t.Text = p.Name));
```

## Default Templates

If no template is provided, item controls behave like the example above: a `TextBlock` is created and populated using `GetText` or `ToString()`.

This keeps the behavior consistent while allowing users to override with templates when needed.

## Recommended Patterns

1. Use `TemplateContext.Register` and `Get` for named elements.
2. Subscribe with `TemplateContext.Subscribe` rather than `+=`, so removal is handled for you.
3. Avoid creating heavy objects during `Bind`; reuse in `Build`.
4. Always assume `Bind` can be called repeatedly on the same container, and assign every property unconditionally.

## Simplified Overloads (Single View)

When your template builds a single control and you do not need named lookup or tracked disposables, you can use overloads that ignore `TemplateContext` in the user code. The context is still created internally, but you do not need to use it.

```csharp
// Build a single view and bind only the item (no context usage).
listBox.ItemTemplate(
    build: _ => new TextBlock(),
    bind: (TextBlock view, Person item) => view.Text = item.Name);
```

This keeps the API simple for common cases while preserving the same template pipeline.
