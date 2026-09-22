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

// Detect OS at runtime and register only the platform/backend valid for that OS.
if (OperatingSystem.IsWindows())
{
    Win32Platform.Register();
    Direct2DBackend.Register(); // or GdiBackend.Register() / MewVGWin32Backend.Register()
}
else if (OperatingSystem.IsLinux())
{
    X11Platform.Register();
    MewVGX11Backend.Register();
}
else if (OperatingSystem.IsMacOS())
{
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
}
else
{
    throw new PlatformNotSupportedException("Unsupported OS.");
}

Application.Run(mainWindow);
```

A WebAssembly build registers `BrowserPlatform` and `MewVGBrowserBackend`, and starts with `Application.RunAsync`. See [2.5](#25-browser-lifetime-webassembly).

### 1.2 Single-target Apps: Application.Create() Chain
If your app is fixed to **one platform + one graphics backend** (e.g., Windows-only), an `Application.Create()` chain is the simplest.

Assumptions:
- Your project **references** the platform/backend packages (so extension methods like `.UseWin32()` are available).
- The build and package references are already fixed; you are not selecting OS at runtime.

```csharp
using Aprillz.MewUI;

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

Application.Create()

#if WINDOWS || DEV
    .UseWin32()
    .UseDirect2D()
#elif LINUX
    .UseX11()
    .UseMewVGX11()
#elif MACOS
    .UseMacOS()
    .UseMewVGMetal()
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

Use the startup overload when initialization must run after dispatcher installation but before the window is shown.

```csharp
Application.Run(window, () =>
{
    // The Dispatcher and UI SynchronizationContext are installed,
    // and the window has not been shown yet.
    InitializeServices();
});
```

### 2.2 Starting without a main window

`Application.Run(Action)` starts the dispatcher and platform message loop without creating or showing a main window. The required startup callback runs once on the UI thread after dispatcher installation. It may show ordinary windows immediately or from later dispatcher work.

```csharp
Application.Run(() =>
{
    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    RegisterGlobalHotkey(() =>
        Application.Current.Dispatcher!.BeginInvoke(
            () => new PaletteWindow().Show()));
});
```

Windowless execution does not set `OnExplicitShutdown` automatically. Set it explicitly when closing the last palette or utility window must leave the process running.

With the builder, configure `OnStartup` and omit the main window factory. `WithShutdownMode` applies the policy before startup runs.

```csharp
Application.Create()
    .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
    .OnStartup(RegisterGlobalHotkey)
    .Run();
```

When both `BuildMainWindow(...)` and `OnStartup(...)` are configured, startup runs before the factory-created main window is shown. Calling `OnStartup` again replaces the previous callback.

### 2.3 Startup callback arguments

Every startup entry point has an overload taking `Action<string[]>`. The framework supplies the command-line arguments without the executable path, so startup logic assembled outside the entry point does not have to thread `args` through itself.

```csharp
Application.Create()
    .OnStartup(args =>
    {
        if (args.Contains("--minimized"))
            StartMinimized();
    })
    .Run<MainWindow>();
```

### 2.4 Theme configuration
For ThemeVariant/Accent/ThemeSeed/ThemeMetrics configuration, see:

- [Theme documentation](Theme.md)

The default font family is decided when the platform package registers (`Win32Platform.Register()`, `UseWin32()`, and their X11/macOS/browser equivalents), which precedes `Application.Create()` and window construction. Reading `ThemeMetrics.Default.FontFamily` or `ThemeManager.DefaultMetrics.FontFamily` after that point reports the family that will actually be used. See [Theme documentation](Theme.md) for how an unspecified family follows the system UI font.

### 2.5 Browser lifetime (WebAssembly)

The browser host differs from the desktop hosts in how a run starts, ends, and renders.

- `Application.RunAsync` is the entry point. `Application.Run` throws `NotSupportedException`, because the JavaScript event loop must keep running while the application is alive.
- `RunAsync` installs the dispatcher, runs the startup callback, shows the main window and returns immediately. The task it returns completes at shutdown.
- The host page drives the frames: it calls the render entry point from its own animation-frame loop, so MewUI runs no loop of its own.
- One top-level `Window`. Showing a second one throws `NotSupportedException`, since the page surface is the only one.
- `ShowDialogAsync` is the only modal form; `ShowDialog` throws `NotSupportedException` because this host runs no nested loop. Dialogs, popups and menus are drawn inside the single surface.
- `Application.Shutdown()` completes the `RunAsync` task and stops the frames. The page itself stays loaded, since a tab cannot close itself.
- Closing the single window shuts the application down whatever `ShutdownMode` says.

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
In the browser it is the only modal form available (see [2.5](#25-browser-lifetime-webassembly)).

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

In the browser, `Continuous` and `VSyncEnabled` still decide whether every frame repaints, while `TargetFps` has no effect: the page's animation frames set the cadence.

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
`Application.ShutdownMode` decides whether closing a window terminates the message loop.

- `OnLastWindowClose` (the default): exits when the last window closes. This also applies to a window opened after `Application.Run(Action)` started without one.
- `OnMainWindowClose`: exits when the main window closes. The window-based `Run` overloads record that identity; a run started without a main window has none until one is assigned (see 5.3).
- `OnExplicitShutdown`: window closes never exit the loop; the application waits for `Application.Shutdown()`.

The browser host has one window and ends the run when it closes, so the policy makes no difference there.

The policy is scoped to one run and starts at `OnLastWindowClose` every time. Configure it before the run with the builder, or assign `Application.Current.ShutdownMode` from the startup callback.

```csharp
Application.Create()
    .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
    .OnStartup(StartBackgroundApplication)
    .Run();
```

### 5.3 Application.MainWindow
`Application.Current.MainWindow` is the identity `OnMainWindowClose` watches. The window-based `Run` overloads (`Run(Window)`, `BuildMainWindow(...)`, `Run<TWindow>()`) set it, and it is also assignable while the run is in progress.

Assigning it is the only way a run started without a main window can use `OnMainWindowClose`: promote a window opened later.

```csharp
Application.Create()
    .WithShutdownMode(ShutdownMode.OnMainWindowClose)
    .OnStartup(() =>
    {
        var dashboard = new DashboardWindow();
        dashboard.Show();
        Application.Current.MainWindow = dashboard;   // closing this one ends the run
    })
    .Run();
```

- The identity is scoped to one run; the next run starts with none.
- Startup runs after the run window is recorded, so an assignment there overrides the window the builder supplied.
- Assigning `null` clears the identity, which leaves `OnMainWindowClose` with nothing to trigger on.
- The builder path bundles identity with "show at start". Assigning the property gives identity alone, so a window can be shown (or not) on your own terms.
- The preview host captures the main window when the session starts, so a window promoted later is not reflected in preview targets. See [Preview](Preview.md).

### 5.4 Application.Shutdown
`Application.Shutdown()` terminates the message loop immediately:

- Open windows do **not** get a `Closing` callback, so nothing can cancel the exit
- The per-window close lifecycle is not guaranteed on this path; do not rely on `Closing`/`Closed` handlers for save prompts or persistence when shutting down
- Platform resources are torn down and `Application.Run(...)` returns

`Shutdown(int exitCode)` also assigns `Environment.ExitCode`. `Run` stays `void`, so the code survives even when a fatal UI-thread exception makes `Run` throw instead of returning.

```csharp
Application.Shutdown(2);   // loop ends, process exits with 2
```

### 5.5 Recommended patterns
- Default: let the user close windows; the app exits with the last one.
- An "Exit" menu/button that should honor confirmation: call `mainWindow.Close()` so the `Closing` handler can prompt and cancel.
- Main window ends the application: subscribe `Application.Shutdown` to the main window's `Closed`.
- Immediate exit with no prompts (state already saved, watchdog restart, etc.): `Application.Shutdown()`.

#### Example: Main window ends the application
The canonical recipe ties the app lifetime to the main window. Everything routes through `main.Close()`, so the confirmation stays in one place and keeps its veto; Shutdown only runs after the close actually went through, ending the app even if tool/background windows are still open.

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
main.Closed += Application.Shutdown;

// 4) Every exit command goes through Close, never straight to Shutdown.
new Button().Content("Exit").OnClick(() => main.Close());
```

For an unconditional exit (state already saved, watchdog restart), call `Application.Shutdown()` directly - no `Closing`/`Closed` handlers run.

#### Sequencing Close and Shutdown
```csharp
main.Close();
Application.Shutdown();
```

This sequence means "close gracefully if allowed, but exit regardless" - useful for logout, fatal-error exit, or restart-after-update flows. `Close()` runs the close lifecycle before `Shutdown()` takes effect on every platform (synchronously on X11/macOS; on Win32 the posted `WM_CLOSE` is drained before the loop exits).

- If `Closing` does not cancel: `Closed` cleanup runs, then the app exits.
- If `Closing` cancels: the exit still happens, and that window skips its `Closed` cleanup (it is torn down by the Shutdown path).

The synchronous sequence is only safe while no `Closing` handler defers. With a deferral (see [5.6](#56-async-close-closeasync-and-closing-deferrals)), `Close()` returns with the decision still pending and `Shutdown()` ends the loop before it resolves. `CloseAsync` expresses the same intent and waits for the decision either way:

```csharp
await main.CloseAsync();   // full close lifecycle, deferrals included
Application.Shutdown();        // exit regardless of the outcome
```

Prefer this form for the "exit regardless" flows.

Pick by intent: when a confirmation should be able to keep the app running, use `main.Closed += Application.Shutdown` with `main.Close()`; use the sequence only when the exit must happen regardless.

### 5.6 Async close: CloseAsync and Closing deferrals
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
- By default the app exits when the last window closes; select another lifetime with `ShutdownMode`. `Window.Close()` is the graceful (cancellable) path, while `Application.Shutdown()` is immediate
