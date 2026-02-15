[![한국어](https://img.shields.io/badge/README.md-한국어-green.svg)](ko)

![Aprillz.MewUI](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/logo/logo_h-1280.png)


![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-X11-FCC624?logo=linux&logoColor=black)
![macOS](https://img.shields.io/badge/macOS-12%2B-901DBA?logo=Apple&logoColor=white)
![NativeAOT](https://img.shields.io/badge/NativeAOT-Ready-2E7D32)
![License: MIT](https://img.shields.io/badge/License-MIT-000000)
[![NuGet](https://img.shields.io/nuget/v/Aprillz.MewUI.svg?label=NuGet)](https://www.nuget.org/packages/Aprillz.MewUI/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aprillz.MewUI.svg?label=Downloads)](https://www.nuget.org/packages/Aprillz.MewUI/)

---

**😺 MewUI** is a cross-platform and lightweight, code-first .NET GUI framework aimed at NativeAOT.

### 🧪 Experimental Prototype
  > [!IMPORTANT]  
  > This project is a **proof-of-concept prototype** for validating ideas and exploring design directions.  
  > As it evolves toward **v1.0**, **APIs, internal architecture, and runtime behavior may change significantly**.  
  > Backward compatibility is **not guaranteed** at this stage.

### 🤖 AI-Assisted Development
  > [!NOTE]
  > This project was developed using an **AI prompt–driven workflow**.  
  > Design and implementation were performed through **iterative prompting without direct manual code edits**,  
  > with each step reviewed and refined by the developer.

---

## 🚀 Try It Out

You can run it immediately by entering the following command in the Windows command line or a Linux terminal. (.NET 10 SDK required)
> [!WARNING]
> This command downloads and executes code directly from GitHub.
```bash
curl -sL https://raw.githubusercontent.com/aprillz/MewUI/refs/heads/main/samples/FBASample/fba_sample.cs -o - | dotnet run -
```

### Video
https://github.com/user-attachments/assets/2e0c1e0e-3dcd-4b5a-8480-fa060475249a

### Screenshots

| Light | Dark |
|---|---|
| ![Light (screenshot)](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/screenshots/light.png) | ![Dark (screenshot)](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/screenshots/dark.png) |

---
## ✨ Highlights

- 📦 **NativeAOT + trimming** first
- 🪶 **Lightweight** by design (small EXE, low memory footprint, fast first frame — see benchmark below)
- 🧩 Fluent **C# markup** (no XAML)

## 🚀 Quickstart

- NuGet: https://www.nuget.org/packages/Aprillz.MewUI/
  - Install: `dotnet add package Aprillz.MewUI`

- Single-file app (VS Code friendly)
  - See: [samples/FBASample/fba_calculator.cs](samples/FBASample/fba_calculator.cs)
  - Minimal header (without AOT/Trim options):

    ```csharp
    #:sdk Microsoft.NET.Sdk
    #:property OutputType=Exe
    #:property TargetFramework=net10.0

    #:package Aprillz.MewUI@0.9.0

    // ...
    ```

- Run: `dotnet run your_app.cs`
---
## 🧪 C# Markup at a Glance

- Sample source: https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Sample/Program.cs

   ```csharp
    var window = new Window()
        .Title("Hello MewUI")
        .Size(520, 360)
        .Padding(12)
        .Content(
            new StackPanel()
                .Spacing(8)
                .Children(
                    new Label()
                        .Text("Hello, Aprillz.MewUI")
                        .FontSize(18)
                        .Bold(),
                    new Button()
                        .Content("Quit")
                        .OnClick(() => Application.Quit())
                )
        );

    Application.Run(window);
    ```

---
## 🎯 Concept

### MewUI is a code-first GUI framework with three priorities:
- **NativeAOT + trimming friendliness**
- **Small footprint, fast startup, low memory usage**
- **Fluent C# markup** for building UI trees (no XAML)
- **AOT-friendly binding**

### Non-goals (by design):
- WPF-style **animations**, **visual effects**, or heavy composition pipelines
- A large, “kitchen-sink” control catalog (keep the surface area small and predictable)
- Complex path-based data binding
- Full XAML/WPF compatibility or designer-first workflows

---
## ✂️ NativeAOT / Trim

- The library aims to be trimming-safe by default (explicit code paths, no reflection-based binding).
- Windows interop uses source-generated P/Invoke (`LibraryImport`) for NativeAOT compatibility.
- On Linux, building with NativeAOT requires the AOT workload in addition to the regular .NET SDK (e.g. install `dotnet-sdk-aot-10.0`).
- If you introduce new interop or dynamic features, verify with the trimmed publish profile above.

To check output size locally:
- Publish: `dotnet publish .\samples\MewUI.Gallery\MewUI.Gallery.csproj -c Release -p:PublishProfile=win-x64-trimmed`
- Inspect: `.artifacts\publish\MewUI.Gallery\win-x64-trimmed\`

Reference (`Aprillz.MewUI.Gallery.exe` @v0.10.0)
- win-x64:  ~3,545 KB
- osx-arm64: ~2,664 KB
- linux-arm64: ~3939 KB
---
## 🔗 State & Binding (AOT-friendly)

Bindings are explicit and delegate-based (no reflection):

```csharp
using Aprillz.MewUI.Binding;
using Aprillz.MewUI.Controls;

var percent = new ObservableValue<double>(
    initialValue: 0.25,
    coerce: v => Math.Clamp(v, 0, 1));

var slider = new Slider()
            .BindValue(percent);
var label  = new Label()
            .BindText(percent, v => $"Percent ({v:P0})");
```

---
## 🧱 Controls / Panels

Controls (Implemented):
- `Button`
- `Label`, `Image`
- `TextBox`, `MultiLineTextBox`
- `CheckBox`, `RadioButton`, `ToggleSwitch`
- `ComboBox`, `ListBox`, `TreeView`, `GridView`
- `Slider`, `ProgressBar`
- `TabControl`, `GroupBox`
- `MenuBar`, `ContextMenu`, `ToolTip` (in-window popups)
- `Window`, `DispatcherTimer`

Panels: (Spacing supported)
- `Grid` (rows/columns with `Auto`, `*`, pixel)
- `StackPanel` (horizontal/vertical)
- `DockPanel` (dock edges + last-child fill)
- `UniformGrid` (equal cells)
- `WrapPanel` (wrap + item size)
---
## 🎨 Theme

MewUI uses a `Theme` object (colors + metrics) and `ThemeManager` to control defaults and runtime changes.

- Configure defaults before `Application.Run(...)` via `ThemeManager.Default*`
- Change at runtime via `Application.Current.SetTheme(...)` / `Application.Current.SetAccent(...)`

See: `docs/Theme.md`

---
## 🖌️ Rendering Backends

Rendering is abstracted through:
- `IGraphicsFactory` / `IGraphicsContext`

Backends:
- `Direct2D` (Windows): `Aprillz.MewUI.Backend.Direct2D`
- `GDI` (Windows): `Aprillz.MewUI.Backend.Gdi`
- `OpenGL` (Windows): `Aprillz.MewUI.Backend.OpenGL.Win32`
- `OpenGL` (Linux/X11): `Aprillz.MewUI.Backend.OpenGL.X11`
- `OpenGL` (macOS): `Aprillz.MewUI.Backend.OpenGL.MacOS`

Backends are registered by the referenced backend packages (Trim/AOT-friendly). In app code you typically either:
- call `*Backend.Register()` before `Application.Run(...)`, or
- use the builder chain: `Application.Create().Use...().Run(...)`
---
## 🪟 Platform Abstraction

Windowing and the message loop are abstracted behind a platform layer.

Currently implemented:
- Windows (`Aprillz.MewUI.Platform.Win32`)
- Linux/X11 (`Aprillz.MewUI.Platform.X11`)
- macOS (`Aprillz.MewUI.Platform.MacOS`)

### Linux dialogs dependency
On Linux, `MessageBox` and file dialogs are currently implemented via external tools:
- `zenity` (GNOME/GTK)
- `kdialog` (KDE)

If neither is available in `PATH`, MewUI throws:
`PlatformNotSupportedException: No supported Linux dialog tool found (zenity/kdialog).`

---
## 📄Docs

- [C# Markup](docs/CSharpMarkup.md)
- [Binding](docs/Binding.md)
- [Items and Templates](docs/ItemsAndTemplates.md)
- [Theme](docs/Theme.md)
- [Application Lifecycle](docs/ApplicationLifecycle.md)
- [Layout](docs/Layout.md)
- [RenderLoop](docs/RenderLoop.md)
- [Hot Reload](docs/HotReload.md)
- [Custom Controls](docs/CustomControls.md)

---
## 🧭 Roadmap (TODO)
 
**Platforms**
- [ ] Linux/Wayland
- [ ] macOS

**Tooling**
- [x] Hot Reload 
- [ ] Design-time preview
