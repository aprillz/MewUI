[![한국어](https://img.shields.io/badge/README.md-한국어-green.svg)](README.ko.md)

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

<div align="center">

🎗️ <b>In loving memory of my beloved cat and companion, April. Forever in my heart.</b>
<p align="center">
<img width="1280" height="560" alt="Untitled-2 (2)" src="https://github.com/user-attachments/assets/8b3bfa9e-714d-439c-9f2b-7b2fb5a9794d" />
  
</p>

`2012.04.15 – 2026.06.20`

<p align="center">
  사랑하는 사월아, 내 곁에 와줘서 고마워. 너와 함께한 모든 순간이 행복했단다.<br>
  꼭 다시 만나자. 내 보물 송사월.
</p>

<p align="center">
  My beloved April, thank you for coming into my life, and I was always so happy.<br>
  We will surely meet again. My treasure, April.
</p>

</div>

---

**😺 MewUI** is a cross-platform, lightweight, code-first .NET GUI framework for building and shipping NativeAOT/Trim-friendly desktop apps without requiring a separate .NET runtime installation.

> [!NOTE]
> The official pronunciation of **MewUI** is **/mjuː aɪ/** (“myoo-eye”).


### Project Status: Active Development
  > [!IMPORTANT]
  > MewUI is an actively developed framework with published NuGet packages, cross-platform hosts, multiple rendering backends, and optional extension packages.
  >
  > The public API surface is still being stabilized, so breaking changes can happen between minor releases. For production apps, pin package versions and review release notes before upgrading.

### 🤖 AI-Assisted Development
  > [!NOTE]
  > This project was developed using an **AI prompt–driven workflow**.  
  > Design and implementation were performed through **iterative prompting without direct manual code edits**,  
  > with each step reviewed and refined by the developer.

---

## 🚀 Try It Out
**No clone. No download. No project setup.**  
You can **run MewUI immediately** with a single command on **Windows**, **Linux** or **macOS**.  (.NET 10 SDK required)
> [!TIP]
> This is the **quickest way to try MewUI** without going through the usual repository and project setup steps.
```bash
curl -sL https://raw.githubusercontent.com/aprillz/MewUI/refs/heads/main/samples/FBASample/fba_gallery.cs -o - | dotnet run -
```

> [!WARNING]
> This command downloads and executes code directly from GitHub.

### Video
https://github.com/user-attachments/assets/fc2d6ad8-3317-4784-a6e5-a00c68e9ed3b

### Screenshots

| Light | Dark |
|---|---|
| ![Light (screenshot)](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/screenshots/light.png) | ![Dark (screenshot)](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/screenshots/dark.png) |

---
## ✨ Highlights

- 📦 **NativeAOT + trimming** first
- 🪶 **Lightweight** by design (small EXE, low memory footprint, fast first frame)
- 🧩 Fluent **C# markup** (no XAML)

## 🚀 Quickstart

- NuGet: https://www.nuget.org/packages/Aprillz.MewUI/
  - `Aprillz.MewUI` is a **metapackage** that bundles Core, all platform hosts, and all rendering backends.
  - Platform-specific packages are also available: `Aprillz.MewUI.Windows`, `.Linux`, `.MacOS`
  - Install: `dotnet add package Aprillz.MewUI`
  - See: [Installation & Packages](docs/Installation.md)

- Single-file app (VS Code friendly)
  - See: [samples/FBASample/fba_calculator.cs](samples/FBASample/fba_calculator.cs)
  - Minimal header (without AOT/Trim options):

    ```csharp
    #:sdk Microsoft.NET.Sdk
    #:property OutputType=Exe
    #:property TargetFramework=net10.0

    #:package Aprillz.MewUI

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

MewUI is a code-first GUI framework with a small, explicit core and platform-specific hosts/backends.

- **NativeAOT + trimming friendliness**
- **Small footprint, fast startup, low memory usage**
- **Fluent C# markup** for building UI trees (no XAML)
- **AOT-friendly explicit binding**
- **Thin core, optional extensions** for larger features

### Non-goals (by design):
- Full XAML/WPF compatibility
- A drop-in replacement for WPF or Avalonia with identical APIs and behavior
- Designer-first development workflows
- Complex path-based data binding
- An exhaustive, all-in-one control catalog

The core covers common desktop UI patterns; specialized features such as charts and docking ship as extension packages.

---
## ✂️ NativeAOT / Trim

- The library aims to be trimming-safe by default (explicit code paths, no reflection-based binding).
- Windows interop uses source-generated P/Invoke (`LibraryImport`) for NativeAOT compatibility.
- On Linux, building with NativeAOT requires the AOT workload in addition to the regular .NET SDK (e.g. install `dotnet-sdk-aot-10.0`).
- If you introduce new interop or dynamic features, verify with the trimmed publish profile above.

NativeAOT executable size depends on the platform host, rendering backend, resources, and publish options. The table below measures the **main executable only**; ZIP is the same executable compressed with the default measurement settings. Sizes use binary units: **1 MB = 1024 KB**.

| Sample | Platform / backend | Executable | ZIP |
|---|---|---:|---:|
| Hello World | Windows x64 / GDI | 2.907 MB | 1.324 MB |
| Hello World | Windows x64 / Direct2D | 3.046 MB | 1.381 MB |
| Hello World | Windows x64 / MewVG | 3.211 MB | 1.458 MB |
| Hello World | Linux x64 / X11 + MewVG | 4.414 MB | 2.126 MB |
| Hello World | macOS arm64 / MewVG | 2.635 MB | 1.186 MB |
| Gallery | Windows x64 / GDI | 5.838 MB | 2.564 MB |
| Gallery | Windows x64 / Direct2D | 5.959 MB | 2.612 MB |
| Gallery | Windows x64 / MewVG | 6.176 MB | 2.707 MB |
| Gallery | Linux x64 / X11 + MewVG | 7.420 MB | 3.518 MB |
| Gallery | macOS arm64 / MewVG | 5.625 MB | 2.555 MB |

<img src="https://github.com/user-attachments/assets/92dae0e7-6ecb-46f8-b405-2fcab629375b" />

The Gallery is a full-featured showcase sample. Use the Hello World rows as the minimum deployment-size baseline.

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
- `Button`, `ToggleButton`
- `Label`, `TextBlock`, `Image`
- `TextBox`, `MultiLineTextBox`, `PasswordBox`
- `CheckBox`, `RadioButton`, `ToggleSwitch`
- `ComboBox`, `ListBox`, `TreeView`, `GridView`
- `Slider`, `ProgressBar`, `ProgressRing`, `NumericUpDown`
- `TabControl`, `GroupBox`, `Expander`, `Border`
- `ColorPicker`, `DatePicker`, `Calendar`
- `MenuBar`, `ContextMenu`, `ToolTip` (in-window popups)
- `ScrollViewer`
- `Window`, `DispatcherTimer`

Panels:
- `Grid` (rows/columns with `Auto`, `*`, pixel)
- `StackPanel` (horizontal/vertical)
- `DockPanel` (dock edges + last-child fill)
- `UniformGrid` (equal cells)
- `WrapPanel` (wrap + item size)
- `Canvas` (absolute positioning)
- `SplitPanel` (drag splitter)

> All panels except `Canvas` (absolute) and `SplitPanel` support `Spacing`.

---
## 🧩 Extensions

Optional packages layered on top of the core - reference only what you need.

| Extension | Description | Package |
|-----------|-------------|---------|
| [**MewDock**](extensions/MewUI.MewDock/README.md) | Visual Studio style docking - document/tool tabs, drag rearranging, splits, auto-hide, maximize, popouts | `Aprillz.MewUI.MewDock` |
| [**SVG**](extensions/MewUI.Svg/README.md) | Pure C# SVG parsing/rendering (no System.Drawing, AOT compatible) | `Aprillz.MewUI.Svg` |
| [**Skia**](extensions/MewUI.Skia/README.md) | `SkiaCanvasView` (draw with SkiaSharp) + GPU zero-copy interop | `Aprillz.MewUI.Skia` |
| [**MewCharts**](extensions/MewUI.MewCharts/README.md) | Charts (Cartesian/Pie/Polar) via the LiveChartsCore engine, no SkiaSharp dependency | `Aprillz.MewUI.MewCharts` |
| [**WebView2**](extensions/MewUI.WebView2.Win32/README.md) | Win32 WebView2 control (requires the Microsoft Edge WebView2 runtime, Windows only) | `Aprillz.MewUI.WebView2.Win32` |

**Skia interop** - add the zero-copy bridge matching your backend to enable the GPU fast path.

| Backend | Package |
|---------|---------|
| Direct2D | `Aprillz.MewUI.Skia.Interop.Direct2D` |
| GDI | `Aprillz.MewUI.Skia.Interop.Gdi` |
| MewVG / Win32 | `Aprillz.MewUI.Skia.Interop.MewVG.Win32` |
| MewVG / X11 | `Aprillz.MewUI.Skia.Interop.MewVG.X11` |
| MewVG / macOS | `Aprillz.MewUI.Skia.Interop.MewVG.MacOS` |

> Without an interop package, Skia content still renders via the CPU upload fallback. Skia is also bundled as metapackages `Aprillz.MewUI.Skia.Windows` / `.Linux` / `.MacOS` / `.All`.

> **MewDock** is a C# port of [FlexLayout](https://github.com/caplin/FlexLayout) (MIT). **MewCharts** bundles the [LiveChartsCore](https://github.com/beto-rodriguez/LiveCharts2) engine (MIT). See `THIRD_PARTY_NOTICES.md` for license notices.

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

| Backend | Platform | Package |
|---------|----------|---------|
| **Direct2D** | Windows | `Aprillz.MewUI.Backend.Direct2D` |
| **GDI** | Windows | `Aprillz.MewUI.Backend.Gdi` |
| **MewVG** | Windows | `Aprillz.MewUI.Backend.MewVG.Win32` |
| **MewVG** | Linux/X11 | `Aprillz.MewUI.Backend.MewVG.X11` |
| **MewVG** | macOS | `Aprillz.MewUI.Backend.MewVG.MacOS` |

> **[MewVG](https://github.com/aprillz/MewVG)** is a managed port of [NanoVG](https://github.com/memononen/nanovg), using OpenGL on Windows/Linux and Metal on macOS.

Backends are registered by the referenced backend packages (Trim/AOT-friendly). In app code you typically either:
- call `*Backend.Register()` before `Application.Run(...)`, or
- use the builder chain: `Application.Create().Use...().Run(...)`

When using a metapackage (e.g., `Aprillz.MewUI.Windows`), you can select a single backend at publish time with `-p:MewUIBackend=Direct2D`. See [Installation & Packages](docs/Installation.md) for details.

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

- [Installation & Packages](docs/Installation.md)
- [C# Markup](docs/CSharpMarkup.md)
- [Binding](docs/Binding.md)
- [Items and Templates](docs/ItemsAndTemplates.md)
- [Theme](docs/Theme.md)
- [Application Lifecycle](docs/ApplicationLifecycle.md)
- [Layout](docs/Layout.md)
- [RenderLoop](docs/RenderLoop.md)
- [Hot Reload](docs/HotReload.md)
- [Custom Controls](docs/CustomControls.md)
- [Control Template](docs/ControlTemplate.md)

---
## 🤝 Community

- [Contributing](CONTRIBUTING.md)
- [Code of Conduct](.github/CODE_OF_CONDUCT.md)

---
## 🧭 Roadmap

**Platforms**
- [ ] Linux/Wayland
- [ ] Linux framebuffer

**Tooling**
- [x] Hot Reload (experimental)
- [ ] Design-time preview

---
## License

MewUI is licensed under the [MIT License](LICENSE).

Third-party software notices are available in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
