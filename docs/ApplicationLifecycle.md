# Application and Window Lifecycle

This document summarizes the **startup-focused** Application/Window lifecycle and DX in MewUI.
It clarifies the boundary between “before Run” and “after Run”.

---

## 1. Pre-run configuration

This section describes how to configure the platform host and graphics backend before calling `Application.Run(...)`.

MewUI aims to avoid a core-level enum/switch selection for platform/graphics backends.  
Instead, each package provides registration and selection to remain trim/AOT-friendly.

### 1.1 Recommended approach

Register the platform/backend packages before calling `Application.Run(...)`.
```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Backends;
using Aprillz.MewUI.PlatformHosts;

// Detect OS at runtime and register only the platform/backend valid for that OS.
// (In this example: Windows=Win32, Linux=X11, macOS is planned.)
if (OperatingSystem.IsWindows())
{
    Win32Platform.Register();
    Direct2DBackend.Register(); // or GdiBackend.Register() / OpenGLWin32Backend.Register()
}
else if (OperatingSystem.IsLinux())
{
    X11Platform.Register();
    OpenGLX11Backend.Register();
}
else if (OperatingSystem.IsMacOS())
{
    // TODO: register once macOS platform host/backend are implemented
    throw new PlatformNotSupportedException("macOS platform host is not implemented yet.");
}
else
{
    throw new PlatformNotSupportedException("Unsupported OS.");
}

Application.Run(mainWindow);
```

### 1.2 Single-target Apps: Application.Create() Chain
If your app is fixed to **one platform + one graphics backend** (e.g., Windows-only), an `Application.Create()` chain is the simplest.

Assumptions:
- Your project **references** the platform/backend packages (so extension methods like `.UseWin32()` are available).
- The build and package references are already fixed; you are not selecting OS at runtime.

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Backends;
using Aprillz.MewUI.PlatformHosts;

Application.Create()
    .UseWin32()
    .UseDirect2D()
    .Run(mainWindow);
```

### 1.3 Multi-target Apps: Fixing the chain
Instead of runtime OS branching, you can **define symbols via csproj conditions (typically RID/CI publish)** and then **fix the chain with `#if`**.
This is also convenient for trimming/distribution, because you can structure package references so that each build only includes what it needs.

#### 1.3.1 Define symbols in csproj (example)
```xml
<PropertyGroup>
  <TargetFrameworks>net10.0-windows;net10.0</TargetFrameworks>
  <!-- assume CI/publish injects the RID via: dotnet publish -r ... -->
  <RuntimeIdentifiers>win-x64;linux-x64;osx-x64;osx-arm64</RuntimeIdentifiers>
</PropertyGroup>

<!-- dev runs (RID is often empty): use the runtime OS branching path -->
<PropertyGroup Condition="'$(RuntimeIdentifier)' == ''">
  <DefineConstants>$(DefineConstants);DEV</DefineConstants>
</PropertyGroup>

<!-- publish/CI (RID is set): fix OS symbols based on RID -->
<PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('win-'))">
  <DefineConstants>$(DefineConstants);WINDOWS</DefineConstants>
</PropertyGroup>
<PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('linux-'))">
  <DefineConstants>$(DefineConstants);LINUX</DefineConstants>
</PropertyGroup>
<PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('osx-'))">
  <DefineConstants>$(DefineConstants);MACOS</DefineConstants>
</PropertyGroup>
```

#### 1.3.2 Fix the chain in Program.cs (example)
```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Backends;
using Aprillz.MewUI.PlatformHosts;

Application.Create()

#if WINDOWS || DEV
    .UseWin32()
    .UseDirect2D()
#elif LINUX
    .UseX11()
    .UseOpenGL()
#elif MACOS
    .ThrowPlatformNotSupported("macOS platform host is not implemented yet.")
#else
    .ThrowPlatformNotSupported()
#endif
    .Run(mainWindow);
```

### 1.4 Runtime branching while keeping a builder chain
If you must branch at runtime and still keep a chain-like style, you can use a builder variable and continue chaining after the branch.

### Notes
- **Only configurable before Run**: after Run, changing core app settings should throw or be ignored (the policy must be consistent in code).
- **Plugin-based registration**: platform/backend packages provide Register/selection.

---

## 2. Application Startup Flow

### 2.1 Application.Run
When `Application.Run(Window)` is called, the flow is:

1) Set `Application.Current`
2) Create the PlatformHost and initialize the Dispatcher
3) Register and show the Window
4) Enter the message loop

#### Example: Minimal Setup
```csharp
var window = new Window()
    .Title("Hello")
    .Content(new TextBlock().Text("Hello, MewUI"));

Application.Run(window);
```

### 2.2 Theme configuration
For ThemeVariant/Accent/ThemeSeed/ThemeMetrics configuration, see:

- [Theme documentation](Theme.md)

---

## 3. Window Startup Flow

### 3.1 Constructing a Window
`new Window()` only creates the object; **no native handle exists yet**.

### 3.2 Show
At `Window.Show()` time:
1) Register into Application
2) Create backend resources (WindowHandle)
3) Raise Loaded
4) Perform first Layout & Render

### 3.3 ShowDialogAsync (Modal)
`ShowDialogAsync` shows a window as a modal dialog and completes when it is closed.
When an `owner` is provided, the owner window is disabled while the dialog is open (platform dependent).

```csharp
var dialog = new Window()
    .Title("Dialog")
    .Content(new TextBlock().Text("Hello from dialog"));

await dialog.ShowDialogAsync(owner: main);
```

#### Example: Multiple Windows
```csharp
var main = new Window()
    .Title("Main")
    .Content(new TextBlock().Text("Main window"));

var tools = new Window()
    .Title("Tools")
    .Content(new TextBlock().Text("Tools window"));

main.OnLoaded(() => tools.Show());
Application.Run(main);
```

---

## 4. RenderLoopSettings

RenderLoop behavior is controlled via `Application.Current.RenderLoopSettings`:

- `Mode`: `OnRequest` / `Continuous`
- `TargetFps`: 0 means unlimited
- `VSyncEnabled`: controls backend present/swap behavior

#### Example: RenderLoop Settings
```csharp
Application.Current.RenderLoopSettings.SetContinuous(true);
Application.Current.RenderLoopSettings.VSyncEnabled = false;
Application.Current.RenderLoopSettings.TargetFps = 0; // unlimited
```

---

## 5. Shutdown Flow

- `Window.Close()` → destroy backend → unregister from Application
- When the last window closes, the platform loop may exit (platform policy)
- `Application.Quit()` explicitly terminates the loop

---

## 6. Exception Handling

- Exceptions on the UI thread are routed to `Application.DispatcherUnhandledException`
- Unhandled exceptions are treated as fatal by default

#### Example: Handling DispatcherUnhandledException
```csharp
Application.DispatcherUnhandledException += e =>
{
    try
    {
        MessageBox.Show(e.Exception.ToString(), "Unhandled UI exception");
    }
    catch
    {
        // ignore
    }
    e.Handled = true;
};
```

---

## 7. Summary

- The core flow is: **pre-run configuration → Run → message loop**
- Theme/RenderLoop should be decided before Run
- A Window only acquires native resources at Show time
