# Confirm public APIs from the restored package

Use the XML documentation shipped with the exact restored NuGet package as the first API reference. It is valid evidence for public types, members, overload intent, parameters, return values, and documented exceptions. It supplements the task recipes in this skill; it does not replace them.

## Resolve the exact package first

1. Run `dotnet list package` in the consumer project.
2. Inspect `obj/project.assets.json` when a central, transitive, or target-specific version is unclear.
3. Obtain the cache root with `dotnet nuget locals global-packages --list`; do not assume a user-specific cache path.
4. Under `<global-packages>/<lowercase-package-id>/<version>/lib/<target-framework>/`, inspect the XML file beside the assembly.

The lookup is shell-independent. Run these .NET CLI commands, then use the reported values to form the path above:

```text
dotnet list package
dotnet nuget locals global-packages --list
```

For example, to inspect `ControlExtensions.BindText`, open the matching
`Aprillz.MewUI.xml` and search for that member name with the environment's file-search
or editor capability. Do not copy the XML file into the project and do not select a
different cached version merely because it is easier to find. Prefer IDE completion
for discovery, XML documentation for contract text, and a package-only compile for
final overload resolution.

XML member IDs encode member kind and signature: `T:` is a type, `M:` a method, `P:` a property,
and a doubled backtick followed by a number marks generic arity. When the raw ID is difficult to
read, locate the member name and use the surrounding `<summary>`, `<param>`, `<returns>`, and
`<exception>` elements.

## Fluent extension rules

Most C# Markup extensions return the receiver so calls can be chained. Classify the method before using it:

- Property setters such as `.Text(value)`, `.Margin(value)`, and `.IsEnabled(value)` assign one public property.
- Boolean conveniences may default to `true`; `.Disable()` and `.Enable()` are fixed-value shorthands.
- Event methods such as `.OnClick(handler)` and `.OnKeyDown(handler)` add handlers. Repeating them adds subscriptions; never add them repeatedly from a recycled template's `bind` callback.
- Binding methods such as `.BindText(source)` and `.Bind(property, source, ...)` create a binding. Conversion, convert-back, mode, and fallback requirements depend on the selected overload.
- Collection builders such as `.Children(...)`, `.Items(...)`, `.Columns(...)`, `.Item(...)`, and `.Band(...)` mutate a collection or definition set. Do not assume they behave like a scalar property setter; read that overload's XML contract.
- Template methods split construction from rebinding. Create visuals and attach stable handlers in `build`; apply the current item in `bind`.
- `.Ref(out value)` captures an element reference during construction; `.Register(context, name)` and `context.Get<T>(name)` are for template parts.

## Important exceptions

- `Window` sizing is not ordinary element sizing. Use `.Resizable`, `.Fixed`, or a fit-content method instead of element `.Width`, `.Height`, or `.Size` assumptions.
- Composite methods can update more than one property. Examples include `.Center()`, window sizing helpers, and `.Command(command, presentation)`.
- Fixed shorthand methods such as `.Bold()`, `.DockTop()`, and `.Vertical()` do not take the underlying enum or property value.
- Event, binding, template, collection, command, and lifecycle helpers are behavioral operations, not property assignments.
- A method available on one concrete receiver may not exist on its base type or a sibling control. Confirm the receiver constraint in the signature.
- Overloads taking `string`, `Element`, `Command`, `ObservableValue<T>`, or a conversion delegate can share a method name. Do not choose an overload from its name alone.
- Generated typed binding may require model types to be accessible from generated code. Prefer top-level `internal` or `public` model types.
- Optional extension packages have their own assembly and XML file. Confirm both the package version and the required startup registration before using their APIs.

## Final confirmation

Add the smallest uncertain expression to the consumer project or a disposable package-only compile project. Build every target framework used by that project. A source-checkout build, a different cached version, or a text search without compilation does not prove that the selected package accepts the call.

When XML documentation does not explain how several public APIs fit together and internet access is available, use [upstream-source-map.md](upstream-source-map.md). Current upstream source is secondary composition evidence; the restored package contract and package-only compile remain the final compatibility check.
