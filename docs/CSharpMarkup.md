# C# Markup Guide

MewUI's C# Markup is a Fluent API that allows you to declaratively build UI with pure C# code without XAML.
It is compatible with Native AOT compilation and does not use Reflection.
This guide covers the concepts and naming rules. The per-receiver method tables are generated from the public extension methods in `src/MewUI/Markup` and the extension packages into `docs/markup`; see [Reference](#reference).

## Concept

### Why C# Markup?

- **Native AOT Compatible**: Everything is determined at compile time without Reflection
- **Type Safety**: Compiler catches errors
- **IntelliSense**: IDE auto-completion support
- **Code Reuse**: Extract UI components as regular C# methods

### Basic Pattern

<!-- snippet:basic-pattern -->
```csharp
var button = new Button()
    .Content("Click Me")
    .Width(100)
    .OnClick(() => Console.WriteLine("Clicked!"));
```
<!-- /snippet -->

All extension methods return `this` to enable method chaining.

## Naming Policy

### Property Setting
| Pattern | Description | Example |
|---------|-------------|---------|
| `PropertyName(value)` | Set property directly | `.Width(100)`, `.Text("Hello")` |
| `PropertyName()` | Set bool property to true | `.Bold()`, `.IsChecked()` |

### Event Handlers
| Pattern | Description | Example |
|---------|-------------|---------|
| `OnEventName(handler)` | Register event handler | `.OnClick(...)`, `.OnTextChanged(...)` |

Use `Command(...)`, rather than event extensions, for conditional execution and reusable actions. `BindCommand(...)` is a data binding for a dynamically supplied semantic Command.

Do not overload the same `On*` method solely by changing the handler delegate's parameter type. Untyped lambdas can match both overloads, including when one extension targets a base class and another targets a derived class. Use a distinct semantic name instead, such as `OnCheckStateChanged` or `OnLayoutSizeChanged`.

### Data Binding
| Pattern | Description | Example |
|---------|-------------|---------|
| `BindPropertyName(source)` | ObservableValue binding | `.BindText(vm.Name)` |
| `BindPropertyName(source, convert)` | One-way conversion binding | `.BindText(vm.Count, c => $"{c} items")` |
| `BindPropertyName(source, convert, convertBack)` | Two-way conversion binding | `.BindValue(vm.Level, x => (double)x, x => (int)x)` |

Converter overloads are available for the `Bind*` convenience methods. For properties that bind two-way by default, omitting `convertBack` intentionally makes the converted binding one-way.

`.BindText(source)` and `.BindIcon(source)` follow the same rule when their receiver is a `Command`: they create real one-way bindings to `Command.Presentation.AccessTextProperty` and `IconProperty`. An underscore in the text source is an access-key marker; consumers without mnemonic support can use normalized `Command.Text`.

<!-- snippet:binding -->
```csharp
var nameBox = new TextBox()
    .BindText(viewModel.Name);
var countLabel = new Label()
    .BindText(viewModel.Count, count => $"{count} items");
var levelSlider = new Slider()
    .BindValue(viewModel.Level, level => (double)level, value => (int)value);
```
<!-- /snippet -->

### Property Name Aliases

Fluent names follow the public concept rather than internal storage details. Composite values use overloads of the same public method:

<!-- snippet:naming-aliases -->
```csharp
var border = new Border()
    .BorderThickness(new Thickness(1, 2, 3, 4))
    .CornerRadius(new CornerRadius(4, 8, 12, 16));
```
<!-- /snippet -->

### Shortcut Methods
Frequently used properties have concise shortcut methods:
- `.Bold()` → `.FontWeight(FontWeight.Bold)`
- `.Horizontal()` → `.Orientation(Orientation.Horizontal)`
- `.Center()` → `.HorizontalAlignment(Center).VerticalAlignment(Center)`

<!-- snippet:shortcuts -->
```csharp
var title = new Label().Text("Title").Bold();
var row = new StackPanel().Horizontal();
var okButton = new Button().Content("OK").Center();
```
<!-- /snippet -->

### Capturing a Reference

`Ref(out field)` stores the receiver in a variable and returns it, so the chain continues while the element stays reachable for later updates:

<!-- snippet:ref -->
```csharp
var textBox = new TextBox()
    .Ref(out var nameBox)
    .Text("Hello");
```
<!-- /snippet -->

### Delegate-Typed Properties

A property whose type is a delegate (`Func<...>`, `Action<...>`) is itself invocable, so `element.Factory(value)` binds to the property and never reaches a same-name extension method. Such properties alone use the `With` prefix (`WithFactory(value)`), and the parameter declares `[FluentSetsMember(nameof(Owner.Factory))]` so the analyzers and the markup audit still connect the extension to the property. No other extension uses a `With` or `Set` prefix.

```csharp
public static T WithCodeBlockFactory<T>(this T control, [FluentSetsMember(nameof(MarkdownPresenter.CodeBlockFactory))] Func<string, string?, FrameworkElement?>? factory) where T : MarkdownPresenter
```

### Hints for Extension Authors

Most extensions need no attribute: a same-name single-value setter (`Width(double)` sets `Width`), an `On` + event name subscription (`OnClick` subscribes `Click`), and `Bind*` methods are recognized by name and signature. Four attributes in `src/MewUI/Markup/Hints/FluentHintAttributes.cs` declare what a name cannot express. They are `internal` and read by metadata name, so an extension package compiles the same file into its own assembly.

| Attribute | Placed on | Declares |
|---|---|---|
| `FluentSetsMember(memberName)` | Parameter | The argument replaces the assignment of that property, or subscribes that event when the parameter is a handler; parameter order is assignment order |
| `FluentSetsConstant(memberName, value, order)` | Method | A parameterless shorthand assigns the constant to the property; `order` is contiguous from 0 across the constants of one method |
| `FluentReplacesMember(memberName)` | Method | A `params` extension replaces calls to that instance method |
| `FluentExcluded(reason)` | Property or event | The member intentionally has no fluent extension, so the audit does not count it as missing |

```csharp
// FluentSetsMember: a differently named property, or one attribute per parameter of a composite setter
public static Border BorderThickness(this Border border, [FluentSetsMember(nameof(Border.NonUniformBorderThickness))] Thickness thickness)

// FluentSetsConstant: a shorthand that assigns fixed values
[FluentSetsConstant(nameof(FrameworkElement.HorizontalAlignment), MewUI.HorizontalAlignment.Center, 0)]
[FluentSetsConstant(nameof(FrameworkElement.VerticalAlignment), MewUI.VerticalAlignment.Center, 1)]
public static T Center<T>(this T element) where T : FrameworkElement

// FluentReplacesMember: a params extension standing in for instance calls
[FluentReplacesMember(nameof(Panel.Add))]
[FluentReplacesMember(nameof(Panel.AddRange))]
public static T Children<T>(this T panel, params Element[] children) where T : Panel

// FluentExcluded: a member that deliberately has no extension
[FluentExcluded("Low-level platform interop event")]
public event Action<NativeMessageEventArgs>? NativeMessage;
```

The markup audit (`tools/MewUI.MarkupAudit`, run by `tests/MewUI.Test/Markup/FluentExtensionAuditTests.cs`) checks that every public settable property and event of each concrete control is covered by a convention, a hint, or an exclusion, and that each hint names an existing member of a compatible type.

### Generated Extensions

The same-name setters and the `On*` event subscriptions of the core controls are not written by hand: the `Aprillz.MewUI.Generators` source generator, which ships inside the `Aprillz.MewUI.Core` package, emits them at build time into one `<Owner>MarkupExtensions` class per control (`ButtonMarkupExtensions`, `UIElementMarkupExtensions`), placed in the control's own namespace. Every public settable property gets `Prop(value)` (`bool` properties default the argument to `true`), every public event gets `OnEvent(handler)`, and a delegate-typed property gets `WithProp(value)` with the `FluentSetsMember` hint. Members marked `[FluentExcluded]` or `[Obsolete]` are left out. The hand-written files keep only what a convention cannot express: composites, constant shorthands, aliases, collection builders, `Bind*` methods, helpers, and setters whose body does more than assign the value.

A project that declares its own controls enables the generator for them with the `MewUIGenerateFluentExtensions` build property; it needs Roslyn 4.12 or later (.NET SDK 9 or later), and the generator emits the hint attributes into the project when they are not visible to it:

```xml
<PropertyGroup>
  <MewUIGenerateFluentExtensions>true</MewUIGenerateFluentExtensions>
</PropertyGroup>
```

Hand-written extensions in the project take precedence: the generator skips a member when an extension with the same name and value type already applies to the receiver, when a `FluentSetsMember` hint already names the member, when a same-shaped `On*` delegate overload would make lambdas ambiguous, or when an instance method has that name. Each skip is reported as an informational `MEWG101` to `MEWG104` diagnostic and listed at the top of the generated file. `MewUIFluentExtensionsNamespace` moves every generated class into one namespace instead of the owner's namespace.

### IDE Conversion Tools

The `Aprillz.MewUI.Analyzers` package converts object initializers and configuration statements into fluent chains and back. The rules (`MEW1101` to `MEW1106`) and the hint contract are documented in [src/MewUI.Analyzers/README.md](../src/MewUI.Analyzers/README.md).

## Apply Pattern

`Apply(action)` runs an action on the receiver and returns it, for initialization that has no dedicated extension:

<!-- snippet:apply -->
```csharp
var textBox = new TextBox()
    .OnTextChanged(text => Console.WriteLine(text))
    .Apply(box => box.MaxLength = 100);
```
<!-- /snippet -->

## Reference

The method tables below are generated from the compiled assemblies into `docs/markup` and checked against the committed files by `MarkupReferenceDocsTests`. After changing an extension, regenerate with `dotnet run --project tools/MewUI.MarkupAudit/MewUI.MarkupAudit -- src/MewUI/bin/Debug/net10.0/Aprillz.MewUI.dll <the Debug build output of every extension package listed below> --docs docs/markup` and commit the result. Each row lists the method signature without the receiver, the members it sets (`event X` for a subscription, `bind X` for a binding, `X = value` for a shorthand), and the XML summary.

| File | Covers |
|---|---|
| [Element.md](markup/Element.md) | `Element`, `UIElement`, `FrameworkElement`, `Control`: size, margin, alignment, visibility, focus, mouse, keyboard, and drag events, Grid/Canvas/DockPanel attached properties, colors and fonts |
| [Content.md](markup/Content.md) | Content controls, `Border`, `Expander`, `GroupBox`, `TabControl`, `ScrollViewer`, `Popup`, and decorators |
| [Window.md](markup/Window.md) | `Window` sizing modes, placement, lifecycle events, and services |
| [Button.md](markup/Button.md) | `Button`, `DropDownButton`, `SplitButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `ButtonGroup`, `SegmentedControl` |
| [TextBox.md](markup/TextBox.md) | `TextBox`, `MultiLineTextBox`, `PasswordBox` |
| [Input.md](markup/Input.md) | `Slider`, `ProgressBar`, `ProgressRing`, `NumericUpDown`, `Calendar`, `DatePicker`, `ColorPicker` |
| [ListBox.md](markup/ListBox.md) | `ItemsControl`, `ListBox`, `ComboBox`, `NavigationList`, `NavigationView` |
| [GridView.md](markup/GridView.md) | `GridView` and `GridViewColumn<TItem>` |
| [TreeView.md](markup/TreeView.md) | `TreeView` |
| [Menu.md](markup/Menu.md) | `Menu`, `MenuBar`, `MenuItem`, `ContextMenu` |
| [ToolBar.md](markup/ToolBar.md) | `ToolBar` bands, groups, and items |
| [Panel.md](markup/Panel.md) | `StackPanel`, `Grid`, `DockPanel`, `WrapPanel`, `UniformGrid`, `SplitPanel` |
| [Shape.md](markup/Shape.md) | `Shape`, `Line`, `Rectangle`, `PathShape`, `GlyphElement` |
| [Image.md](markup/Image.md) | `Image` |
| [Text.md](markup/Text.md) | `TextBlock`, `Run`, `MarkupTextBlock`, `Label` |
| [Binding.md](markup/Binding.md) | Generic `Bind` overloads and `BindingPath` builders |
| [Misc.md](markup/Misc.md) | `DispatcherTimer`, `StyleSheet`, `Command`, `ApplicationBuilder`, design-time size, `Ref`, `Apply` |
| [MewDock.md](markup/MewDock.md) | Extensions of the `Aprillz.MewUI.MewDock` package |
| [Markdown.md](markup/Markdown.md) | Extensions of the `Aprillz.MewUI.Markdown` package |
| [Terminal.md](markup/Terminal.md) | Extensions of the `Aprillz.MewUI.Terminal` package |
| [MewvalonEdit.md](markup/MewvalonEdit.md) | Extensions of the `Aprillz.MewUI.MewvalonEdit` package |
| [Skia.md](markup/Skia.md) | Extensions of the `Aprillz.MewUI.Skia` package |
| [WebView2.Win32.md](markup/WebView2.Win32.md) | Extensions of the `Aprillz.MewUI.WebView2.Win32` package |
| [MewCharts.md](markup/MewCharts.md) | Extensions of the `Aprillz.MewUI.MewCharts` package |

### Notes

- `Window`: `Width`, `Height`, `Size`, `MinWidth`, `MinHeight`, `MaxWidth`, and `MaxHeight` are intentionally unavailable. Use `Resizable`, `Fixed`, or a `FitContent*` method so the window sizing mode is explicit.
- Drop-down buttons: `DropDownButton` always opens its menu and has no primary Command. `SplitButton` executes its inherited Button Command from the primary face; a false `CanExecute` disables only that face and keeps the menu available.
- `SegmentedControl` is a selection control rather than a Command consumer. Assign Commands to the individual `SegmentButton` containers in a `ButtonGroup` for independent actions; bind a single selected value through the selection API of `SegmentedControl`.
- `ProgressRing` is a ring-shaped `ProgressBar`: the arc fills clockwise from the top, and `IsIndeterminate` shows a spinning arc instead of the value.
- `Grid.Rows(string)` and `Grid.Columns(string)` take a comma-separated `GridLength` list: `Auto` fits the content, `*` is one star share, `2*` is two shares, and `100` is 100 pixels.
- Menus: `Add`, `Item`, `SubMenu`, and `Separator` build a menu by appending; `Items(...)` replaces the items of a `ContextMenu` or `MenuBar`. `Item(Command)` uses the Command's normalized default text and access key; `Item(string, Command)` and `Text(string)` override the MenuItem text and access key together with `_` markers. `MenuItem.Text` and `Icon` inherit the Command defaults only while they have no value source; explicit empty text and a null icon suppress those defaults. An `IsEnabled` binding is preserved and ANDed with `CanExecute` for the effective state.
