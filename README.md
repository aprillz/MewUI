[![한국어](https://img.shields.io/badge/README.md-한국어-green.svg)](ko)

![Aprillz.MewUI](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/logo/logo_h-1280.png)


![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-X11-FCC624?logo=linux&logoColor=black)
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

---
## 🪶 Lightweight

- **Executable size:** NativeAOT + Trim focused (sample `win-x64-trimmed` is ~ `2.2 MB`)
- **Sample runtime benchmark** (NativeAOT + Trimmed, 50 launches):

| Backend | Loaded avg/p95 (ms) | FirstFrame avg/p95 (ms) | WS avg/p95 (MB) | PS avg/p95 (MB) |
|---|---:|---:|---:|---:|
| Direct2D | 10 / 11 | 178 / 190 | 40.0 / 40.1 | 54.8 / 55.8 |
| GDI | 15 / 21 | 54 / 67 | 15.2 / 15.3 | 4.6 / 4.8 |

---

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

    #:package Aprillz.MewUI@0.8.3

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
- Publish: `dotnet publish .\samples\MewUI.Sample\MewUI.Sample.csproj -c Release -p:PublishProfile=win-x64-trimmed`
- Inspect: `.artifacts\publish\MewUI.Sample\win-x64-trimmed\`

Reference (sample, `win-x64-trimmed`):
- `Aprillz.MewUI.Sample.exe` ~ `2,257 KB`

---
## 🔗 State & Binding (AOT-friendly)

Bindings are explicit and delegate-based (no reflection):

```csharp
using Aprillz.MewUI.Binding;
using Aprillz.MewUI.Controls;

var percent = new ObservableValue<double>(
    initialValue: 0.25,
    coerce: v => Math.Clamp(v, 0, 1));

var slider = new Slider().BindValue(percent);
var label  = new Label().BindText(percent, v => $"Percent ({v:P0})");
```

---
## 🧱 Controls / Panels

Controls (implemented):
- `Button`
- `Label`, `Image`
- `TextBox`, `MultiLineTextBox`
- `CheckBox`, `RadioButton`
- `ComboBox`, `ListBox`
- `Slider`, `ProgressBar`
- `TabControl`, `GroupBox`
- `MenuBar`, `ContextMenu`, `ToolTip` (in-window popups)
- `Window`
- `DispatcherTimer`

Panels:
- `Grid` (rows/columns with `Auto`, `*`, pixel)
- `StackPanel` (horizontal/vertical + spacing)
- `DockPanel` (dock edges + last-child fill)
- `UniformGrid` (equal cells)
- `WrapPanel` (wrap + item size + spacing)
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
- `Vulkan` (Linux/X11, experimental): `Aprillz.MewUI.Backend.Vulkan.X11`

Backends are registered by the referenced backend packages (Trim/AOT-friendly). In app code you typically either:
- call `*Backend.Register()` before `Application.Run(...)`, or
- use the builder chain: `Application.Create().Use...().Run(...)`
---
## 🪟 Platform Abstraction

Windowing and the message loop are abstracted behind a platform layer.

Currently implemented:
- Windows (`Aprillz.MewUI.Platform.Win32`)
- Linux/X11 (`Aprillz.MewUI.Platform.X11`, experimental)

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
- [Theme](docs/Theme.md)
- [Application Lifecycle](docs/ApplicationLifecycle.md)
- [Layout](docs/Layout.md)
- [RenderLoop (internal)](docs/RenderLoop.md)

---
## 🧭 Roadmap (TODO)

**Controls**
- [ ] `ToggleSwitch`
- [ ] `GridView`

**Features**
- [ ] Simple template support (Delegate-based)

**Platforms**
- [ ] Linux/Wayland
- [ ] macOS

**Tooling**
- [ ] Hot Reload 
- [ ] Design-time preview
