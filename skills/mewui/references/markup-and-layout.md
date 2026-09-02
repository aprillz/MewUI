# Compose UI with C# Markup

Construct controls normally and chain public fluent methods. Common method shapes are:

- property: `.Text(value)`, `.Margin(value)`, `.IsEnabled(value)`
- event: `.OnClick(handler)`, `.OnSelectionChanged(handler)`
- binding: `.BindText(source)` or `.Bind(property, source)`
- children: `.Children(...)`, `.Content(...)`, `.Child(...)`
- reference: `.Ref(out control)`

## Linear layout

```csharp
var actions = new StackPanel()
    .Horizontal()
    .Spacing(8)
    .Children(
        new Button().Content("Add").OnClick(Add),
        new Button().Content("Remove").OnClick(Remove));
```

Use `.Vertical()` for forms and page sections. Prefer layout constraints over manual coordinates.

## Grid form

```csharp
var form = new Grid()
    .Columns("140,*")
    .Rows("Auto,Auto,Auto")
    .Spacing(8)
    .Children(
        new TextBlock().Text("Name").CenterVertical(),
        new TextBox().Column(1),
        new TextBlock().Text("Email").CenterVertical().Row(1),
        new TextBox().Row(1).Column(1),
        new Button().Content("Submit").Row(2).Column(1));
```

Grid definitions accept `Auto`, star sizing such as `*` or `2*`, and numeric fixed sizes. Place children with `.Row`, `.Column`, `.RowSpan`, and `.ColumnSpan`.

## Page shell

```csharp
var page = new DockPanel()
    .Spacing(12)
    .Children(
        new TextBlock()
            .Text("Application title")
            .FontSize(24)
            .Bold()
            .DockTop(),
        new TextBlock()
            .Text("Ready")
            .DockBottom(),
        new ScrollViewer()
            .Content(form));
```

Use `StackPanel`, `Grid`, `DockPanel`, `WrapPanel`, `UniformGrid`, `Canvas`, and `ScrollViewer` according to the geometry required. A `ScrollViewer` owns one content subtree.

Use `.Resizable(width, height, ...)`, `.Fixed(width, height)`, or a fit-content method on `Window`. Element `.Width` and `.Height` setters are not the window sizing API.

Sizes use device-independent pixels. `Margin` is outside spacing and `Padding` is inside spacing. Use `.WithTheme(...)` for theme-dependent colors rather than hard-coded light-only values.
