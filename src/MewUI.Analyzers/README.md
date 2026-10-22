# Aprillz.MewUI.Analyzers

> **Status (2026-06-15): early experimental version.** Diagnostic ids, behavior, and formatting
> output are still being refined and may change.

Roslyn analyzers and refactorings for MewUI fluent markup. Ships as a NuGet analyzer
(`analyzers/dotnet/cs`), so it works in Visual Studio, VS Code (C# Dev Kit), Rider, and CI from one
reference. Build-time only: nothing ships into the runtime / NativeAOT output.

- Namespace: `Aprillz.MewUI.Analyzers`
- Target: `netstandard2.0`, Roslyn pinned to 4.8 for broad host compatibility.
- Korean version: [한국어](README.ko.md)

## Status

| Id | Feature | Kind | Source |
|---|---|---|---|
| `MEW1101` | Object initializer -> fluent chain | analyzer + code fix | `InitializerToFluentAnalyzer.cs`, `InitializerToFluentCodeFix.cs` |
| `MEW1102` | Fluent chain expand / collapse | refactoring | `FluentChainFormatRefactoring.cs` |
| `MEW1103` | Merge statements into a fluent chain | refactoring | `MergeChainStatementsRefactoring.cs` |
| `MEW1104` | Configuration statement -> fluent call | refactoring | `AssignmentToFluentCallRefactoring.cs` |
| `MEW1105` | Merge statements into a fluent chain | analyzer + code fix | `ChainStatementAnalyzer.cs`, `ChainStatementCodeFix.cs` |
| `MEW1106` | Configuration statement -> fluent call | analyzer + code fix | `ChainStatementAnalyzer.cs`, `ChainStatementCodeFix.cs` |

Shared pieces:

- `FluentMethodResolver.cs` resolves a property/event name to its fluent setter extension. The
  extension methods are the source of truth (no mapping table to drift); it also tries the
  `On`-prefixed name (`Click` -> `OnClick`) and reads the hint attributes (see "Declaring what an
  extension sets") that name the members a composite setter, a shorthand, or an alias assigns.
- `FluentInitializerPlanner.cs` plans a whole object initializer for MEW1101: which contiguous
  members each fluent call consumes, with what arguments, and which members stay behind. The
  analyzer and the code fix share it, so a reported initializer always converts the way it was judged.
- `ChainStatementDescriber.cs` decides what a single statement contributes to a chain on its
  receiver (a fluent call, an event subscription, a property assignment, a static attached setter, or
  a call an extension declares it replaces). MEW1103 / MEW1104 / MEW1105 / MEW1106 all go through it,
  so they never judge the same statement differently.
- `FluentChainLayout.cs` is the shared layout engine (used by MEW1101 / MEW1102 / MEW1103 / MEW1105):
  it rebuilds a chain from its structure, expands element children as a tree, keeps values inline, and
  re-indents multi-line lambda bodies.

76 tests in `tests/MewUI.Analyzers.Test` cover all six.

## `MEW1101` - Convert object initializer to fluent chain

Rewrites an object initializer into the equivalent MewUI fluent setter chain (and expands it).

```csharp
new Border { CornerRadius = 8, BorderThickness = 1, Child = body }
// -> Convert to fluent chain
new Border()
    .CornerRadius(8)
    .BorderThickness(1)
    .Child(body)
```

Severity is `Hidden` (no squiggle, lightbulb only). Raise it via `.editorconfig` if you want it
visible: `dotnet_diagnostic.MEW1101.severity = suggestion`.

### Rules

1. **Trigger.** An object creation with an object initializer where at least one member `Name = value`
   maps to a fluent call.
2. **Fluent setter.** An in-scope extension method named exactly `Name` (or `On` + `Name` for events),
   callable on the created type, taking a single parameter that `value` converts to. The extension
   methods are the source of truth - any setter you add is picked up automatically.
3. **Conversion.** Each matching member becomes `.Name(value)` in source order; `new T { ... }` gains
   an explicit `()`. Two or more converted calls are expanded onto separate lines.
4. **Events.** A delegate-typed property `Click = handler` maps to `.OnClick(handler)` via the
   `On`-prefix rule.

   ```csharp
   new MenuItem { Text = "Open", Click = OnOpen }
   // -> Convert to fluent chain
   new MenuItem()
       .Text("Open")
       .OnClick(OnOpen)
   ```

5. **Hinted calls.** An extension whose parameters carry `[FluentSetsMember]` or whose method carries
   `[FluentSetsConstant]` (see "Declaring what an extension sets") replaces the members it declares:
   a composite setter takes several members as its arguments, a shorthand replaces constant
   assignments with a parameterless call, an alias maps one member to a differently named setter, and
   a combined method does both (`ViewBox = r, ViewBoxUnits = ImageViewBoxUnits.Pixels` ->
   `.ViewBoxPixels(r)`).

   ```csharp
   new SplitButton { Command = save, CommandPresentationMode = CommandPresentationMode.TextAndIcon }
   // -> Convert to fluent chain
   new SplitButton().Command(save, CommandPresentationMode.TextAndIcon)

   new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
   // -> Convert to fluent chain
   new TextBlock().Center()
   ```

   A hinted call is used only when every member it declares is assigned, contiguously and in the
   declared order, and every value converts to its parameter or equals the declared constant (an enum
   must match by type, not just by number). `Command = save` alone does not become `.Command(save)`:
   the omitted optional argument would overwrite `CommandPresentationMode` with its default. A
   reversed or interrupted pair is converted member by member instead (`.CenterVertical()` then
   `.CenterHorizontal()`), and a non-matching constant falls back to the same-name setter
   (`.VerticalAlignment(VerticalAlignment.Top)`).
6. **Priority.** When several calls could start at the same member: the one consuming more
   contiguous members wins; at equal count a hinted call beats the same-name setter, which beats the
   `On`-prefixed one; between hinted calls the one passing fewer arguments wins (`.ViewBoxPixels(r)`
   over `.ViewBox(r, ImageViewBoxUnits.Pixels)`). The generated call is then bound like ordinary C# and kept only if it resolves
   to the chosen extension. If candidates of the same priority cannot be told apart that way (two
   differently named shorthands declaring the same constant, say), the members they cover stay in the
   initializer.
7. **Partial conversion.** Members with no matching call stay in a residual initializer.

   ```csharp
   new Widget { Text = "hi", Tag = obj, Width = 5 }
   // -> Tag has no setter, so it stays behind
   new Widget() { Tag = obj }
       .Text("hi")
       .Width(5)
   ```

8. **No diagnostic** when no member maps to a call.

### Not yet handled

- Collection members: `Children = { a, b }` -> `.Children(a, b)`.
- Recursing into nested object initializers.

## `MEW1102` - Fluent chain expand / collapse

A one-shot refactoring (Ctrl+.), not a format-on-save hook (format-on-save calls the host's built-in
formatter, never a refactoring). Both actions are always offered, so "Expand" also re-formats an
already-expanded chain in place.

```csharp
new Button().Content("OK").Width(80)
// -> Expand fluent chain
new Button()
    .Content("OK")
    .Width(80)
// -> Collapse fluent chain to one line  (the inverse)
```

### Rules

1. **Trigger.** Caret inside a member-access invocation chain (one or more calls).
2. **Expand.** Each chained `.Method(...)` moves to its own line, indented one level under the line
   the chain starts on.
3. **Element children.** When an argument is itself an *element chain*, the argument list breaks and
   each element is expanded as its own tree, separated by a blank line:

   ```csharp
   new StackPanel().Vertical().Children(new Button().Content("A").Width(80), new Button().Content("B").Width(80))
   // -> Expand fluent chain
   new StackPanel()
       .Vertical()
       .Children(
           new Button()
               .Content("A")
               .Width(80),

           new Button()
               .Content("B")
               .Width(80)
       )
   ```

   An element chain is one rooted in `new X()`, a call (`Factory()`), or a value (a local / field).
   A chain rooted in a **type** (a static factory such as `Color.FromRgb(...)`) is a value and stays
   inline (it is not split). This needs semantics, so MEW1102 uses the semantic model; MEW1101 /
   MEW1103 format synthesized chains and fall back to a name heuristic (types are PascalCase).

   ```csharp
   new ColorPicker().SelectedColor(Color.FromRgb(255, 0, 0)).Width(120)
   // -> Expand fluent chain   (Color.FromRgb stays inline)
   new ColorPicker()
       .SelectedColor(Color.FromRgb(255, 0, 0))
       .Width(120)
   ```

4. **Lambda blocks.** A multi-line lambda body is re-indented so it aligns with its new position:

   ```csharp
   new Window().Resizable(800, 600).OnMouseDown(e => { if (e.Button == MouseButton.Left) DragMove(); })
   // -> Expand fluent chain
   new Window()
       .Resizable(800, 600)
       .OnMouseDown(e =>
       {
           if (e.Button == MouseButton.Left)
               DragMove();
       })
   ```

5. **Collapse.** The inverse: the whole chain, including nested element children, is joined back onto
   one line.
6. **Idempotent.** Each run rebuilds the chain from its structure, so repeated runs are stable.

### Not yet handled

- A configurable max line length / call-count threshold via `.editorconfig`.

## `MEW1103` - Merge statements into a fluent chain

Folds a `var x = ...;` / `x = ...;` statement and the consecutive statements that configure `x` into
a single chain. Caret on the anchor statement.

```csharp
_titleBar = new Border().MinHeight(40);
_titleBar.Child(body);
_titleBar.Click += () => OnClick();
// -> Merge into fluent chain
_titleBar = new Border()
    .MinHeight(40)
    .Child(body)
    .OnClick(() => OnClick());
```

### Rules

1. **Anchor.** A single local declaration (`var x = ...`) or a simple assignment (`x = ...`). The
   assigned value must be an object creation, a call chain, an identifier, or a member access: calls
   are appended without parentheses, so anything looser (a conditional, say) would change meaning.
2. **Follow-ups.** Consecutive statements that configure `x` - see the statement kinds below. Each
   resulting call must return `x`'s own type, so chaining and assigning back to `x` stay valid; the
   first non-matching statement stops collection.
3. **Top-level statements.** A file with top-level statements works the same way. Collection stops at
   the first compilation unit member that is not a statement, so trailing type declarations are never
   folded in.
4. **`.Ref(out var x)`.** For a *local declaration* of a reference type, the reference is captured
   inline with `.Ref(out var x)` (the MewUI idiom) instead of keeping a `var x = ...;` statement,
   when a `Ref` extension exists. The call goes right after the expression that creates the instance.
   Field / property assignments keep `x = chain;`.

   ```csharp
   var panel = new StackPanel().Spacing(8);
   panel.Vertical();
   panel.Add(header);
   panel.Add(body);
   // -> Merge into fluent chain
   new StackPanel()
       .Ref(out var panel)
       .Spacing(8)
       .Vertical()
       .Children(header, body);
   ```

5. **Collection setters.** Consecutive calls replaced by the same collection setter become one call,
   as shown above; the setter takes the whole list at once.
6. The merged chain is expanded via the shared layout engine.

## `MEW1104` - Configuration statement to fluent call

Converts one statement into the fluent call it is equivalent to. Caret on the statement.

```csharp
_titleBar.Child = new DockPanel().Children(...);   // -> _titleBar.Child(new DockPanel()...)
_titleBar.Click += OnClick;                        // -> _titleBar.OnClick(OnClick)
Grid.SetColumn(_titleBar, 1);                      // -> _titleBar.Column(1)
panel.AddRange(a, b);                              // -> panel.Children(a, b)
```

### Statement kinds

These are the shapes MEW1103 folds into a chain and MEW1104 converts on their own.

| Statement | Becomes | How it resolves |
|---|---|---|
| `x.Prop = value;` | `.Prop(value)` | an extension named after the property |
| `x.Event += handler;` | `.OnEvent(handler)` | the `On` prefix convention |
| `Owner.SetProp(x, value);` | `.Prop(value)` | the `Set` prefix convention, on a static two-parameter method |
| `x.Add(a);` | `.Children(a)` | an extension that declares it replaces `Add` (see below) |

Not offered when nothing resolves, when the resulting call would not return the receiver's type, or
when the statement is already a fluent chain.

### Declaring what an extension sets

Most extensions are found by the naming conventions above. When an extension cannot be matched by
name or signature, it declares what it sets with one of three attributes
(`src/MewUI/Markup/Hints/FluentHintAttributes.cs`). They are internal to MewUI and read by metadata
name (`Aprillz.MewUI.FluentReplacesMemberAttribute`, `Aprillz.MewUI.FluentSetsMemberAttribute`,
`Aprillz.MewUI.FluentSetsConstantAttribute`), so the analyzer does not reference MewUI, and another
assembly can compile the same declarations as its own internal types. Applying the extension in the
members' place must have the same effect and ordering; the attribute is the only guarantee of that,
so it is added by hand after checking both bodies.

**`FluentReplacesMember`** - the extension replaces calls to an instance method (MEW1103 to MEW1106):

```csharp
[FluentReplacesMember(nameof(Panel.Add))]
[FluentReplacesMember(nameof(Panel.AddRange))]
public static T Children<T>(this T panel, params Element[] children) where T : Panel
```

The extension's only value parameter must be a `params` array that accepts every argument of the
call it replaces.

**`FluentSetsMember`** - a parameter receives the value of the named property (MEW1101). On every
value parameter of a method it declares a composite setter; on the only parameter it declares an
alias:

```csharp
public static T Command<T>(
    this T button,
    [FluentSetsMember(nameof(Button.Command))] Command? command,
    [FluentSetsMember(nameof(Button.CommandPresentationMode))] CommandPresentationMode presentation = CommandPresentationMode.None)
    where T : Button

public static T BorderThickness<T>(this T border, [FluentSetsMember(nameof(Border.NonUniformBorderThickness))] Thickness value) where T : Border
```

Parameter order is the assignment order. A method with any unannotated value parameter, optional or
not, is not a candidate, because its default could overwrite another member.

**`FluentSetsConstant`** - a parameterless shorthand assigns a constant to the named property
(MEW1101). Several on one method form one contract, ordered by `order` (contiguous from 0):

```csharp
[FluentSetsConstant(nameof(FrameworkElement.HorizontalAlignment), HorizontalAlignment.Center, order: 0)]
[FluentSetsConstant(nameof(FrameworkElement.VerticalAlignment), VerticalAlignment.Center, order: 1)]
public static T Center<T>(this T element) where T : FrameworkElement
```

**Combined** - a method may carry both: every value parameter annotated with `FluentSetsMember` and
one or more `FluentSetsConstant` on the method. The contract is the parameters in order followed by
the constants in `order`, and the generated call passes only the parameter arguments:

```csharp
[FluentSetsConstant(nameof(Image.ViewBoxUnits), ImageViewBoxUnits.Pixels)]
public static Image ViewBoxPixels(this Image image, [FluentSetsMember(nameof(Image.ViewBox))] Rect? viewBox)
```

A malformed declaration (wrong constructor shape, duplicate or non-contiguous `order`, duplicate
member, empty name, a constant on a method with an unannotated parameter) is ignored, so it never
produces a conversion.

## `MEW1105` / `MEW1106` - The same two as diagnostics

MEW1103 and MEW1104 need the caret on the right statement. MEW1105 (merge) and MEW1106 (single
statement) report the same opportunities as `Hidden` diagnostics with the same fixes, so a whole file
can be converted with Fix All.

Statements absorbed by a MEW1105 merge are not also reported as MEW1106, so the two fixes never
target the same statement.

Being `Hidden`, they show up only as a lightbulb. To convert a file from the command line, raise the
severity and run `dotnet format`:

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.MEW1105.severity = suggestion
```

```
dotnet format analyzers <project> --severity info --diagnostics MEW1105
```

Pass one id per run: `dotnet format` can fail to build a Fix All action when several are combined. A
run applies only non-overlapping fixes, so repeat it until the file stops changing.

## Testing

`tests/MewUI.Analyzers.Test` uses `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` and
`.CodeRefactoring.Testing`. Tests define small self-contained fluent APIs in the test source, so
resolution runs without the MewUI build.

```
dotnet test tests/MewUI.Analyzers.Test/MewUI.Analyzers.Test.csproj
```
