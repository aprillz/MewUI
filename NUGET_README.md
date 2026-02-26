# Aprillz.MewUI

A cross-platform and lightweight, code-first .NET GUI framework aimed at NativeAOT.

- GitHub: https://github.com/aprillz/MewUI
- License: MIT

## Concept

- Fluent **C# markup** (no XAML)
- Designed for **small footprint** (AOT/Trim-friendly), **fast startup**, and **low memory usage**
- Keep the binding model simple (no complex, reflection-heavy path binding)

## Install

`Aprillz.MewUI` is a metapackage that includes Core, all platform hosts (Win32, X11, macOS), and all rendering backends (Direct2D, GDI, MewVG).

```sh
# Cross-platform (all-in-one)
dotnet add package Aprillz.MewUI

# Or platform-specific
dotnet add package Aprillz.MewUI.Windows
dotnet add package Aprillz.MewUI.Linux
dotnet add package Aprillz.MewUI.MacOS
```

## Quick start

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

var window = new Window()
    .Title("Hello MewUI")
    .Size(520, 360)
    .Padding(12)
    .Content(
        new StackPanel()
            .Spacing(8)
            .Children(
                new Label().Text("Hello, Aprillz.MewUI").FontSize(18).Bold(),
                new Button().Content("Quit").OnClick(() => Application.Quit())
            )
    );

Application.Run(window);
```
