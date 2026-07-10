# Application and Window Lifecycle

This document summarizes the Application/Window lifecycle and DX in MewUI, from startup to shutdown.
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

### 5.1 Closing a window
`Window.Close()` (and the platform close button) runs the full close lifecycle:

1) `Closing` is raised; set `args.Cancel = true` to keep the window open
2) The native window is destroyed
3) `Closed` is raised and the window is unregistered from the Application

Windows shown with an owner (`Show(owner)` / `ShowDialogAsync(owner)`) close together when their owner closes.

#### Example: Confirm before closing
```csharp
window.Closing += args =>
{
    if (hasUnsavedChanges && !ConfirmDiscard())
        args.Cancel = true;
};

window.Closed += () => SaveWindowPlacement();
```

### 5.2 When the application exits
The message loop exits when the **last window** closes; `Application.Run(...)` then returns.
Closing the main window alone does not exit the app while other windows are still open.

If closing the main window should quit the app, show secondary windows with the main window as their owner (`tools.Show(main)`) so they close together, or close them from the main window's `Closed` handler.

### 5.3 Application.Quit
`Application.Quit()` terminates the message loop immediately:

- Open windows do **not** get a `Closing` callback, so nothing can cancel the exit
- The per-window close lifecycle is not guaranteed on this path; do not rely on `Closing`/`Closed` handlers for save prompts or persistence when quitting
- Platform resources are torn down and `Application.Run(...)` returns

### 5.4 Recommended patterns
- Default: let the user close windows; the app exits with the last one.
- An "Exit" menu/button that should honor confirmation: call `mainWindow.Close()` so the `Closing` handler can prompt and cancel.
- Main window ends the application: subscribe `Application.Quit` to the main window's `Closed`.
- Immediate exit with no prompts (state already saved, watchdog restart, etc.): `Application.Quit()`.

#### Example: Main window ends the application
The canonical recipe ties the app lifetime to the main window. Everything routes through `main.Close()`, so the confirmation stays in one place and keeps its veto; Quit only runs after the close actually went through, ending the app even if tool/background windows are still open.

```csharp
// 1) Confirmation (optional): one Closing handler guards every exit path.
main.Closing += args =>
{
    if (hasUnsavedChanges && !ConfirmDiscard())
        args.Cancel = true;
};

// 2) Persistence: runs only when the close was allowed.
main.Closed += SaveSession;

// 3) Main window = app lifetime.
main.Closed += Application.Quit;

// 4) Every exit command goes through Close, never straight to Quit.
new Button().Content("Exit").OnClick(() => main.Close());
```

For an unconditional exit (state already saved, watchdog restart), call `Application.Quit()` directly - no `Closing`/`Closed` handlers run.

#### Sequencing Close and Quit
```csharp
main.Close();
Application.Quit();
```

This sequence means "close gracefully if allowed, but exit regardless" - useful for logout, fatal-error exit, or restart-after-update flows. `Close()` runs the close lifecycle before `Quit()` takes effect on every platform (synchronously on X11/macOS; on Win32 the posted `WM_CLOSE` is drained before the loop exits).

- If `Closing` does not cancel: `Closed` cleanup runs, then the app exits.
- If `Closing` cancels: the exit still happens, and that window skips its `Closed` cleanup (it is torn down by the Quit path).

The synchronous sequence is only safe while no `Closing` handler defers. With a deferral (see [5.5](#55-async-close-closeasync-and-closing-deferrals)), `Close()` returns with the decision still pending and `Quit()` ends the loop before it resolves. `CloseAsync` expresses the same intent and waits for the decision either way:

```csharp
await main.CloseAsync();   // full close lifecycle, deferrals included
Application.Quit();        // exit regardless of the outcome
```

Prefer this form for the "exit regardless" flows.

Pick by intent: when a confirmation should be able to keep the app running, use `main.Closed += Application.Quit` with `main.Close()`; use the sequence only when the exit must happen regardless.

### 5.5 Async close: CloseAsync and Closing deferrals
`Window.CloseAsync()` requests a close and reports the outcome:

```csharp
bool closed = await window.CloseAsync();   // true = closed, false = cancelled by Closing
```

- An already-closed window completes with `true` (idempotent).
- Concurrent close requests join one pending decision; `Closing` runs once.

When the close decision itself is asynchronous (a confirmation dialog, an async save), take a deferral inside `Closing` instead of cancelling and re-closing:

```csharp
window.Closing += async args =>
{
    using (args.GetDeferral())        // take it before the first await
    {
        if (!await ConfirmDiscardAsync())
            args.Cancel = true;
    }                                 // the deferral completes here - the decision is submitted
};
```

- The window stays open until every deferral completes; the aggregated `Cancel` then decides (any cancel wins).
- Once allowed, the close proceeds without raising `Closing` again.
- `CloseAsync` completes only after the deferrals resolve, so its result stays truthful with async handlers.
- Close requests arriving while a decision is pending join it - the confirmation is not shown twice.

This also enables multi-window exit orchestration:

```csharp
foreach (var w in openWindows)
    if (!await w.CloseAsync()) return;   // one cancel aborts the exit
// all closed - the app exits by the last-window rule
```

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
- The app exits when the last window closes; `Window.Close()` is the graceful (cancellable) path, `Application.Quit()` is the immediate one
