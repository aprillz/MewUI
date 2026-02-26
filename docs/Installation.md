# Installation & Packages

This document covers the MewUI NuGet package structure, installation, and backend selection at publish time.

---

## 1. Package Structure

MewUI is organized into **metapackages** (convenience bundles) and **individual packages** (fine-grained composition).

```
Aprillz.MewUI                  ← All-in-one metapackage (all platforms + all backends)
├─ Aprillz.MewUI.Core          ← Core (controls, layout, markup, binding)
├─ Aprillz.MewUI.Platform.*    ← Platform hosts
│   ├─ .Platform.Win32
│   ├─ .Platform.X11
│   └─ .Platform.MacOS
└─ Aprillz.MewUI.Backend.*     ← Rendering backends
    ├─ .Backend.Direct2D        (Windows)
    ├─ .Backend.Gdi             (Windows)
    ├─ .Backend.MewVG.Win32     (Windows, NanoVG/OpenGL)
    ├─ .Backend.MewVG.X11       (Linux, NanoVG/OpenGL)
    └─ .Backend.MewVG.MacOS     (macOS, NanoVG/Metal)
```

Separately managed packages (not included in metapackages):
- `Aprillz.MewUI.Svg` — SVG parsing/rendering
- `Aprillz.MewUI.Win32.WebView2` — WebView2 integration (Windows only)

---

## 2. Installation

### 2.1 Quick Start — Metapackages

In most cases, just add a single platform metapackage.

| Target Platform | Package | Includes |
|----------------|---------|----------|
| **Windows** | `Aprillz.MewUI.Windows` | Core + Win32 + Direct2D + GDI + MewVG |
| **Linux** | `Aprillz.MewUI.Linux` | Core + X11 + MewVG |
| **macOS** | `Aprillz.MewUI.MacOS` | Core + MacOS + MewVG |
| **Cross-platform** | `Aprillz.MewUI` | All platforms + all backends |

```bash
# Windows app
dotnet add package Aprillz.MewUI.Windows

# Cross-platform app
dotnet add package Aprillz.MewUI
```

### 2.2 Individual Packages

Instead of a metapackage, you can reference only the packages you need.

```xml
<ItemGroup>
  <PackageReference Include="Aprillz.MewUI.Core" Version="0.10.3" />
  <PackageReference Include="Aprillz.MewUI.Platform.Win32" Version="0.10.3" />
  <PackageReference Include="Aprillz.MewUI.Backend.Gdi" Version="0.10.3" />
</ItemGroup>
```

### 2.3 Additional Packages

Add SVG or WebView2 support separately.

```bash
dotnet add package Aprillz.MewUI.Svg
dotnet add package Aprillz.MewUI.Win32.WebView2
```

---

## 3. Backend Selection at Publish

### 3.1 Overview

Metapackages include all backends for the target platform.
At publish time, use the `MewUIBackend` property to keep only one backend.
If not specified, all backends are included in the publish output.

### 3.2 CLI

```bash
# Direct2D only
dotnet publish -r win-x64 -p:MewUIBackend=Direct2D

# GDI only (lightweight)
dotnet publish -r win-x64 -p:MewUIBackend=Gdi

# MewVG only
dotnet publish -r win-x64 -p:MewUIBackend=MewVG
```

### 3.3 In csproj

```xml
<PropertyGroup>
  <MewUIBackend>Direct2D</MewUIBackend>
</PropertyGroup>
```

### 3.4 In a Publish Profile

```xml
<!-- Properties/PublishProfiles/Win-Direct2D.pubxml -->
<Project>
  <PropertyGroup>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <MewUIBackend>Direct2D</MewUIBackend>
  </PropertyGroup>
</Project>
```

```bash
dotnet publish -p:PublishProfile=Win-Direct2D
```

### 3.5 MewUIBackend Values

| Value | Kept | Removed |
|-------|------|---------|
| `Direct2D` | Backend.Direct2D | Backend.Gdi, Backend.MewVG.Win32 |
| `Gdi` | Backend.Gdi | Backend.Direct2D, Backend.MewVG.Win32 |
| `MewVG` | Backend.MewVG.* | Backend.Direct2D, Backend.Gdi |
| *(not set)* | All | — |

> Linux and macOS include only the MewVG backend, so `MewUIBackend` is not needed.

---

## 4. Cross-Platform Publish (Aprillz.MewUI)

The all-in-one package (`Aprillz.MewUI`) includes assemblies for every platform,
but `dotnet publish -r <rid>` **automatically excludes non-target platform assemblies**.

| RID | Kept | Auto-removed |
|-----|------|-------------|
| `win-x64` | Core, Win32, Direct2D, Gdi, MewVG.Win32 | X11, MacOS, MewVG.X11, MewVG.MacOS |
| `linux-x64` | Core, X11, MewVG.X11 | Win32, MacOS, Direct2D, Gdi, MewVG.Win32, MewVG.MacOS |
| `osx-arm64` | Core, MacOS, MewVG.MacOS | Win32, X11, Direct2D, Gdi, MewVG.Win32, MewVG.X11 |

RID filtering and `MewUIBackend` filtering can be combined.

```bash
# Windows + Direct2D only
dotnet publish -r win-x64 -p:MewUIBackend=Direct2D
```

---

## 5. Rendering Backend Guide

| Backend | Platform | Notes |
|---------|----------|-------|
| **Direct2D** | Windows | GPU-accelerated, high-quality text rendering. Recommended default for Windows |
| **GDI** | Windows | CPU-based, ultra-lightweight, minimal dependencies |
| **MewVG** | Windows, Linux, macOS | Managed port of NanoVG. Uses OpenGL (Win32/X11) or Metal (macOS) |

See [Application Lifecycle](ApplicationLifecycle.md) for backend registration in app code.

---

## 6. File-Based Apps (.NET 10+)

In .NET 10 file-based apps, reference packages with the `#:package` directive.

```csharp
#:sdk Microsoft.NET.Sdk
#:property OutputType=Exe
#:property TargetFramework=net10.0

#:package Aprillz.MewUI@0.10.3

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

// ...
Application.Run(window);
```

---

## 7. Version Compatibility

- All MewUI packages (Core, Platform.*, Backend.*, metapackages) are published with the **same version**.
- Referencing a single metapackage automatically aligns all dependency versions.
- When composing individual packages, ensure **all packages use the same version**.
