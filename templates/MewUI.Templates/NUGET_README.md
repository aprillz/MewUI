# Aprillz.MewUI.Templates

`dotnet new` templates for [MewUI](https://github.com/aprillz/MewUI) apps.

```
dotnet new install Aprillz.MewUI.Templates
```

| Template | Short name | Creates |
|---|---|---|
| MewUI App | `mewui` | A desktop app for Windows, Linux and macOS on the `Aprillz.MewUI` package |
| MewUI Browser App | `mewui-browser` | A WebAssembly app on `Aprillz.MewUI.Platform.Browser` and `Aprillz.MewUI.Backend.MewVG.Browser` |

```
dotnet new mewui -o MyApp
dotnet new mewui-browser -o MyBrowserApp
```

The browser app needs the WebAssembly build tools: `dotnet workload install wasm-tools`.

Each template references the MewUI packages of the same version as this package.
