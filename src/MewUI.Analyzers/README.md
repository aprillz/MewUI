# Aprillz.MewUI.Analyzers

> **Status (2026-06-15): early experimental version.** Diagnostic ids, behavior, and formatting
> output are still being refined and may change.

Roslyn analyzers and refactorings for MewUI fluent markup. Ships as a NuGet analyzer
(`analyzers/dotnet/cs`), so it works in Visual Studio, VS Code (C# Dev Kit), Rider, and CI from one
reference. Build-time only: nothing ships into the runtime / NativeAOT output.

- Namespace: `Aprillz.MewUI.Analyzers`
- Target: `netstandard2.0`, Roslyn pinned to 4.8 for broad host compatibility.
- Korean version: [한국어](ko/README.md)

## Status

| Id | Feature | Kind | Source |
|---|---|---|---|
| `MEW1101` | Object initializer -> fluent chain | analyzer + code fix | `InitializerToFluentAnalyzer.cs`, `InitializerToFluentCodeFix.cs` |
| `MEW1102` | Fluent chain expand / collapse | refactoring | `FluentChainFormatRefactoring.cs` |
| `MEW1103` | Merge statements into a fluent chain | refactoring | `MergeChainStatementsRefactoring.cs` |
| `MEW1104` | Property assignment -> fluent call | refactoring | `AssignmentToFluentCallRefactoring.cs` |

Shared pieces:

- `FluentMethodResolver.cs` resolves a property/event name to its fluent setter extension. The
  extension methods are the source of truth (no mapping table to drift); it also tries the
  `On`-prefixed name (`Click` -> `OnClick`).
- `FluentChainLayout.cs` is the shared layout engine (used by MEW1101 / MEW1102 / MEW1103): it
  rebuilds a chain from its structure, expands element children as a tree, keeps values inline, and
  re-indents multi-line lambda bodies.

20 tests in `tests/MewUI.Analyzers.Test` cover all four.

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
   maps to a fluent setter.
2. **Fluent setter.** An in-scope extension method named exactly `Name` (or `On` + `Name` for events),
   callable on the created type, taking a single parameter that `value` converts to. The extension
   methods are the source of truth - any setter you add is picked up automatically.
3. **Conversion.** Each matching member becomes `.Name(value)` in source order; `new T { ... }` gains
   an explicit `()`. Two or more converted members are expanded onto separate lines.
4. **Events.** A delegate-typed property `Click = handler` maps to `.OnClick(handler)` via the
   `On`-prefix rule.

   ```csharp
   new MenuItem { Text = "Open", Click = OnOpen }
   // -> Convert to fluent chain
   new MenuItem()
       .Text("Open")
       .OnClick(OnOpen)
   ```

5. **Partial conversion.** Members with no matching setter stay in a residual initializer.

   ```csharp
   new Widget { Text = "hi", Tag = obj, Width = 5 }
   // -> Tag has no setter, so it stays behind
   new Widget() { Tag = obj }
       .Text("hi")
       .Width(5)
   ```

6. **No diagnostic** when no member maps to a setter.

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

1. **Anchor.** A single local declaration (`var x = ...`) or a simple assignment (`x = ...`).
2. **Follow-ups.** Consecutive statements that are either `x.Method(...);` (a fluent call) or
   `x.Event += handler;` (an event subscription, folded in as `.OnEvent(handler)`). Each must return
   `x`'s own type, so chaining and assigning back to `x` stay valid; the first non-matching statement
   stops collection.
3. **`.Ref(out var x)`.** For a *local declaration* of a reference type, the reference is captured
   inline with `.Ref(out var x)` (the MewUI idiom) instead of keeping a `var x = ...;` statement,
   when a `Ref` extension exists. Field / property assignments keep `x = chain;`.

   ```csharp
   var panel = new StackPanel();
   panel.Vertical();
   panel.Spacing(8);
   // -> Merge into fluent chain
   new StackPanel()
       .Ref(out var panel)
       .Vertical()
       .Spacing(8);
   ```

4. The merged chain is expanded via the shared layout engine.

## `MEW1104` - Property assignment to fluent call

Converts `receiver.Prop = value;` into `receiver.Prop(value);` when a fluent setter for `Prop` exists
on the receiver's type. Caret on the assignment.

```csharp
_titleBar.Child = new DockPanel().Children(...);
// -> Convert to fluent call
_titleBar.Child(new DockPanel().Children(...));
```

Not offered when no fluent setter resolves for the assigned member.

## Testing

`tests/MewUI.Analyzers.Test` uses `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` and
`.CodeRefactoring.Testing`. Tests define small self-contained fluent APIs in the test source, so
resolution runs without the MewUI build.

```
dotnet test tests/MewUI.Analyzers.Test/MewUI.Analyzers.Test.csproj
```
