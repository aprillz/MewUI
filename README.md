![Aprillz.MewUI](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/logo/logo-480.png)


![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-X11-FCC624?logo=linux&logoColor=black)
![NativeAOT](https://img.shields.io/badge/NativeAOT-Ready-2E7D32)
![License: MIT](https://img.shields.io/badge/License-MIT-000000)
[![NuGet](https://img.shields.io/nuget/v/Aprillz.MewUI.svg?label=NuGet)](https://www.nuget.org/packages/Aprillz.MewUI/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aprillz.MewUI.svg?label=Downloads)](https://www.nuget.org/packages/Aprillz.MewUI/)

---

**😺 MewUI** is minimal, code-first .NET GUI library aimed at **NativeAOT + Trim**.

- **Status:** 🧪 experimental prototype (APIs and behavior may change).

- **Note:** 🤖 most of the code in this repository is written with the help of GPT.
---

## NuGet

- https://www.nuget.org/packages/Aprillz.MewUI/
- Install: `dotnet add package Aprillz.MewUI`

---
## Video
https://github.com/user-attachments/assets/876c1450-de36-4ccf-9cf8-b47f035a300f

## Screenshots

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

### MewUI is a code-first UI library with three priorities:
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

Controls:
- `Label`, `Button`, `TextBox`
- `CheckBox`, `RadioButton`
- `ListBox`, `ComboBox`
- `Slider`, `ProgressBar`
- `Window`

Panels:
- `Grid` (rows/columns with `Auto`, `*`, pixel)
- `StackPanel` (horizontal/vertical + spacing)
- `DockPanel` (dock edges + last-child fill)
- `UniformGrid` (equal cells)
- `WrapPanel` (wrap + item size + spacing)
---
## 🎨 Theme

Theme is split into two parts:
- `Palette` - colors (including derived colors based on background + accent)
- `Theme` - non-color parameters (corner radius, default font, plus a `Palette`)

Accent switching:

```csharp
Theme.Current = Theme.Current.WithAccent(Color.FromRgb(214, 176, 82));
```

---
## 🖌️ Rendering Backends

Rendering is abstracted through:
- `IGraphicsFactory` / `IGraphicsContext`

Backends:
- `Direct2D` (Windows)
- `GDI` (Windows)
- `OpenGL` (Windows / Linux)

The sample defaults to `Direct2D` on Windows, and `OpenGL` on Linux.
- `Direct2D`: slower startup and larger resident memory, but better suited for complex layouts/effects
- `GDI`: lightweight and fast startup, but CPU-heavy and not ideal for high-DPI, large windows, or complex UIs
- `OpenGL`: cross-platform and works with the Linux/X11 host, but currently more limited and still experimental

---
## 🪟 Platform Abstraction

Windowing and the message loop are abstracted behind a platform layer.

Currently implemented:
- Windows (`Win32PlatformHost`)
- Linux/X11 (experimental)

---
## 🧭 Roadmap (TODO)

**Controls**
- [ ] `Image`
- [ ] `GroupBox`
- [ ] `TabControl`
- [ ] `ScrollViewer`

**Rendering**
- [ ] Better text shaping (HarfBuzz)
- [ ] Glyph atlas + caching improvements

**Platforms**
- [ ] Wayland
- [ ] macOS

**Tooling**
- [ ] Hot Reload 
- [ ] Design-time preview
