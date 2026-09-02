# Troubleshoot a package application

## A type or fluent method is missing

1. Run `dotnet list package` and confirm the selected `Aprillz.MewUI*` versions.
2. Inspect `obj/project.assets.json` when central or transitive versions may differ.
3. Restore again and use IDE completion or the restored package XML documentation.
4. Confirm the required platform or extension package is installed.
5. Remove an unavailable API instead of guessing a similar WPF or Avalonia name.

Common invalid substitutions are XAML, `DataContext`, `DependencyProperty`, `StyledProperty`, `DynamicResource`, string property paths, and WPF `ICommand` properties.

## Startup fails

- Register one platform and one compatible backend before `Run`.
- Use a platform metapackage that contains both registrations.
- Keep all MewUI package versions aligned.
- Do not register alternative backends together and expect later selection.

## Binding does not update

- Keep the `ObservableValue<T>` or model alive.
- Confirm the target is a bindable `MewProperty`.
- Provide convert-back for a converted two-way binding.
- For `TextBox`, test through editing or `ReplaceSelection`, not external `Text` assignment.
- In recycled templates, release item-specific subscriptions during unbind.

## Build and package verification

```text
dotnet restore
dotnet build --no-restore
```

Validate a new application with public `PackageReference` entries only. A different local build of MewUI does not prove that the selected public package contains the same API.

If an output file is locked, do not terminate the user's application. Wait, close it normally, or use an isolated output path.

For trimming or NativeAOT failures, reproduce the exact `dotnet publish` command and runtime identifier. Report build, trimmed publish, and NativeAOT publish as separate results.
