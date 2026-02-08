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
}
```

`IDataTemplate<TItem>` provides type-safe binding.

```csharp
public interface IDataTemplate<in TItem> : IDataTemplate
{
    void Bind(FrameworkElement view, TItem item, int index, TemplateContext context);
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

`TemplateContext` is used for:

1. Registering named elements for fast lookup.

```csharp
public sealed class TemplateContext : IDisposable
{
    public void Register<T>(string name, T element) where T : UIElement;
    public T Get<T>(string name) where T : UIElement;
    // Internal lifecycle management (not part of the public contract).
}
```

Common usage:

```csharp
ctx.Get<TextBlock>("Name").Text = item.Name;
```

## Template Lifecycle

1. `Build` is called once when a container is created.
2. `Bind` is called when a container is realized for an item.
3. Context cleanup is handled internally before each `Bind` and during recycle.

This allows containers to be reused safely without leaking subscriptions or state.

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
2. Use `TemplateContext.Track` to register subscriptions and unmanaged resources.
3. Avoid creating heavy objects during `Bind`; reuse in `Build`.
4. Always assume `Bind` can be called repeatedly on the same container.

## Simplified Overloads (Single View)

When your template builds a single control and you do not need named lookup or tracked disposables, you can use overloads that ignore `TemplateContext` in the user code. The context is still created internally, but you do not need to use it.

```csharp
// Build a single view and bind only the item (no context usage).
listBox.ItemTemplate(
    build: _ => new TextBlock(),
    bind: (TextBlock view, Person item) => view.Text = item.Name);
```

This keeps the API simple for common cases while preserving the same template pipeline.
