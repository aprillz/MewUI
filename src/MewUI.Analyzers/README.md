# Aprillz.MewUI.Analyzers

Roslyn analyzers and code fixes for MewUI fluent markup. Ships as a NuGet analyzer
(`analyzers/dotnet/cs`), so it works in Visual Studio, VS Code (C# Dev Kit), Rider, and CI from
one reference. Build-time only: nothing ships into the runtime / NativeAOT output.

- Namespace: `Aprillz.MewUI.Analyzers`
- Target: `netstandard2.0`, Roslyn pinned to 4.8 for broad host compatibility.

## Status

| Id | Feature | Kind | Source | State |
|---|---|---|---|---|
| `MEW1101` | Object initializer -> fluent chain | analyzer + code fix | `InitializerToFluentAnalyzer.cs`, `InitializerToFluentCodeFix.cs` | **Implemented** (partial conversion) |
| `MEW1102` | Fluent chain expand / collapse | refactoring | `FluentChainFormatRefactoring.cs` | **Implemented** (nested tree) |

`FluentMethodResolver.cs` is the shared resolver `MEW1101` uses; `MEW1102` is purely syntactic and
needs no semantics. Both ship in one assembly. 7 tests in `tests/MewUI.Analyzers.Test` cover full /
partial / no-op conversion and expand / collapse / nested expand / nested collapse.

Not yet implemented (see each feature's *Not yet handled* below and
[agent/fluent-formatter/plan.md](../../agent/fluent-formatter/plan.md) Phase 3): event / collection
setters for `MEW1101`, `.editorconfig` thresholds and MewUI-type gating for `MEW1102`, resolver
caching, and NuGet packaging as `analyzers/dotnet/cs`.

## `MEW1101` - Convert object initializer to fluent chain

Rewrites an object initializer into the equivalent MewUI fluent setter chain.

```csharp
new Button { Text = "OK", Width = 80 }
// -> Convert to fluent chain
new Button().Text("OK").Width(80)
```

Severity is `Hidden`: no squiggle, surfaced via the lightbulb (Ctrl+.). Raise it through
`.editorconfig` if you want it visible:

```ini
dotnet_diagnostic.MEW1101.severity = suggestion
```

### Rules

1. **Trigger.** An object creation with an object initializer where at least one member
   `Name = value` maps to a fluent setter.
2. **What counts as a fluent setter.** An in-scope *extension* method, named exactly `Name`,
   callable on the created type, taking a single parameter that `value` is implicitly convertible
   to. The extension methods themselves are the source of truth - there is **no mapping table** to
   drift, so any setter you add (including your own) is picked up automatically.
3. **Conversion.** Each matching member becomes `.Name(value)` in source order. `new T { ... }`
   gains an explicit `()`.
4. **Partial conversion.** Members with no matching setter are left in a residual initializer:

   ```csharp
   new Widget { Text = "hi", Tag = obj, Width = 5 }
   // -> Tag has no setter, so it stays behind
   new Widget() { Tag = obj }.Text("hi").Width(5)
   ```

5. **No diagnostic** when no member maps to a setter (nothing to convert).

### Not yet handled (planned)

These currently fall through to the residual initializer rather than being converted:

- Events: `Click = handler` -> `.OnClick(handler)` (name is not 1:1, needs an `On*` rule)
- Collection members: `Children = { a, b }` -> `.Children(a, b)`
- Recursing into nested object initializers

## `MEW1102` - Fluent chain formatter

A one-shot **refactoring** (Ctrl+.), not a format-on-save hook: format-on-save in both VS and VS
Code calls the built-in formatter, never a code fix, so a refactoring is the only portable trigger.
Put the caret anywhere in a chain of two or more chained calls:

```csharp
new Button().Content("OK").Width(80)
// -> Expand fluent chain
new Button()
    .Content("OK")
    .Width(80)
// -> Collapse fluent chain to one line  (the inverse)
```

### Rules

1. **Trigger.** Caret inside an invocation chain whose receiver is a member access, with at least
   two chained calls. Purely syntactic - no semantic binding, so it works on any fluent chain
   (and is harmless on others, since it only appears when you open the lightbulb on it).
2. **Expand.** Each chained `.Method(...)` moves to its own line, indented one level (four spaces)
   under the line the chain starts on.
3. **Nesting.** When an argument is itself a chain of two or more calls, the argument list breaks
   onto its own lines and that argument is expanded recursively one level deeper - the markup tree:

   ```csharp
   new DockPanel()
       .LastChildFill()
       .Children(
           Menu().DockTop(),
           new TabControl()
               .Padding(0)
               .TabItems(...)
       )
   ```

   Simple argument lists (no chain argument) stay inline.
4. **Collapse.** The inverse: the whole chain - including nested chain arguments - is joined back
   onto one line.
5. **Idempotent.** Each run rebuilds the chain from its structure (ignoring existing trivia), so
   repeated Expand/Collapse is stable.
6. Only the opposite action is offered for the current state (Expand on a one-line chain, Collapse
   on a multi-line one).

### Not yet handled (planned)

- A configurable max line length / call-count threshold via `.editorconfig` (currently always one
  call per line when expanded).
- Restricting to MewUI-typed chains when desired.

See [agent/fluent-formatter/plan.md](../../agent/fluent-formatter/plan.md) for the full design.

## Testing

`tests/MewUI.Analyzers.Test` uses `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` and
`.CodeRefactoring.Testing`. `MEW1101` tests define a small self-contained fluent API in the test
source (so resolution runs without the MewUI build); `MEW1102` tests use `StringBuilder.Append`
chains (real fluent, no custom types needed since the refactoring is syntactic).

```
dotnet test tests/MewUI.Analyzers.Test/MewUI.Analyzers.Test.csproj
```
