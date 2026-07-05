# Aprillz.MewUI

A cross-platform, lightweight, code-first .NET GUI framework for building and shipping NativeAOT/Trim-friendly desktop apps without requiring a separate .NET runtime installation.

- GitHub: https://github.com/aprillz/MewUI
- License: MIT

## Concept

- Fluent **C# markup** (no XAML)
- Designed for **small footprint** (AOT/Trim-friendly), **fast startup**, and **low memory usage**
- Explicit **AOT-friendly binding** (no reflection-heavy path binding as the primary model)
- Thin core with optional extension packages for larger features

## Project status

MewUI is actively developed, with published packages, cross-platform hosts, multiple rendering backends, and optional extension packages. The public API surface is still being stabilized, so breaking changes can happen between minor releases.

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
