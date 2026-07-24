# Editor Preview

This document explains how the MewUI **editor preview** (VS Code extension) works, and how to write code that keeps the preview smooth during development.

The preview shows the rendered output of your `Window` and `UserControl` types in an editor panel without launching the app. Opening a session builds once; from then on every save updates the screen through Hot Reload, typically within a second.

---

## 1. Getting started

Requirements:

- .NET SDK 8.0 or later (10.0 recommended)
- A trusted workspace (the preview builds and runs your project code)
- Projects referencing the MewUI metapackages (`Aprillz.MewUI`, `Aprillz.MewUI.Windows`, ...) work with no extra setup. Only when referencing the repository by project reference does the executable project import the targets directly:

```xml
<Import Project="..\..\MewUI\build\Aprillz.MewUI.targets" />
```

Usage: open a C# file and run **MewUI: Start Preview** from the command palette. The session opens for the executable project the active file belongs to (for a library file, the executable project referencing that library is located).

No app window appears on screen during a preview session. Every window renders offscreen and shows only in the panel.

---

## 2. Preview targets

The target dropdown contains:

| Entry | Meaning |
|---|---|
| Top entry `Application main window` | The **live window instance** your entry point passed to `Application.Run(...)`, reflecting the real configuration and state `Main` loaded |
| `TypeName (window)` | A scanned Window type. Selecting it **creates a new instance** to display |
| `TypeName (usercontrol)` | A scanned UserControl type, hosted in an auto-sized wrapper window |

Target scanning rules:

- `Window`/`UserControl` subclasses are discovered across the executable assembly and every referenced assembly. `internal` types are included.
- A type is creatable when it has **a parameterless constructor or a constructor whose parameters all have default values**. Types without one stay listed but disabled, with the reason shown.
- The target declared in the active editor file is selected automatically (controlled by the `mewui.preview.autoSelectTarget` setting).

---

## 3. Preview-friendly code guidelines

### 3.1 Guard side effects with `Design.IsPreviewMode`

The preview **runs your actual app**. The entry point `Main` executes in full, and target type constructors really run. Outward-facing side effects - sockets, background services, tray icons, global hooks - therefore happen for real in a preview session. An app that opens a server port, for example, can fight the real app instance over the port.

`Design.IsPreviewMode` is fixed at process start (before `Main`), so it is reliable from the first line of `Main`:

```csharp
var config = LoadConfig();                  // things the screen needs run as-is
if (!Design.IsPreviewMode)
{
    trayIcon.Install();
    server.Start(config.Port);              // guard only the side effects
}
Application.Run(BuildMainWindow(config));
```

Choosing what to guard: **let "visible" things - themes, fonts, styles, window composition - run, and block only the outward effects.** Guarding the screen composition itself degrades preview fidelity.

Note that constructor side effects run repeatedly - on every target switch and Hot Reload rebuild. Types that create timers or subscriptions in their constructor need a preview branch or cleanup logic.

### 3.2 Make constructor-injected arguments optional with fallbacks

Windows and controls that take dependencies through their constructor show up disabled in the target list. **Making the parameters optional with internal fallbacks** enables the preview without touching any call site:

```csharp
public SettingsDialog(AppConfig? config = null)
{
    _config = config ?? new AppConfig();    // preview: default / real: injected
    ...
}
```

For heavy dependencies that are awkward to default-construct (another window, a service), a nullable field with `?.` at the use sites is safer:

```csharp
public SetupWizard(AppConfig? config = null, MainWindow? mainWindow = null)
{
    _config = config ?? new AppConfig();
    _mainWindow = mainWindow;               // null in the preview
}
...
_mainWindow?.UpdateZeroconfService();
```

### 3.3 Sample data

The preview executes real code, so there is no separate design-time DataContext concept like WPF's. When sample data is needed, plain C# idioms cover it:

```csharp
protected override Element? OnBuild()
{
    var items = Design.IsPreviewMode
        ? SampleData.Clients                // preview: mock
        : _service.LoadClients();           // real: service
    ...
}
```

To compare multiple states on one screen, make a small UserControl that lays the states out. That control itself becomes a preview target:

```csharp
internal sealed class ButtonStates : UserControl
{
    protected override Element? OnBuild() =>
        new StackPanel().Spacing(8).Children(
            new Button().Content("Normal"),
            new Button().Content("Disabled").Enabled(false));
}
```

### 3.4 Preview size hints

Components display at their content size by default (desired size, clamped to the panel). To preview at a specific size, set a hint:

```csharp
public ProductCard()
{
    this.DesignSize(400, 300);      // both axes fixed
    // this.DesignWidth(400);       // width fixed, height fits content
}
```

Hints are recorded only in preview sessions and cost nothing at production runtime. Window targets keep their own `WindowSize` logic; a DesignSize hint overrides only the specified axes.

### 3.5 Build idioms: override vs. callback

Named windows/controls that become preview targets define their build with a virtual `OnBuild()` override (type scanning enables per-type preview); one-off windows at the composition site use the fluent `Build(x => ...)`. Build ownership (callback first) and the **re-runnability rule** (one-shot specs and event subscriptions stay outside the build - preview rebuilds surface violations immediately) are specified in the "Registering build code" section of the [Hot Reload](HotReload.md) document.

UserControl does not need a `Build()` call in its constructor. `OnBuild` content builds automatically at the first layout pass (lazy build); an explicit call is only needed when content is required before layout runs.

---

## 4. Understanding the update pipeline

| Edit kind | Behavior | Feel |
|---|---|---|
| Method body edit | Hot Reload delta applies, then the active target rebuilds | within 1s of saving |
| Rude edit (new type/signature, ...) | Automatic process restart and reconnect | a few seconds, last frame retained |
| Compile error | Session stays alive; the status bar shows the error | resumes on the next good save |
| New type added | Target list refreshes when the delta applies | no restart needed |

The status bar shows the update path (delta/restart), which distinguishes the cause when an update feels slow. For the canonical per-edit rebuild scope and retained state, see the [Hot Reload](HotReload.md) document (the preview differs only in rebuilding the active target unconditionally on every delta).

Two-step manual recovery:

- **Refresh**: re-runs and re-attaches the current target's OnBuild. The process stays alive, so it is fast. Try this first when the screen looks wrong.
- **Restart**: full process restart. The last resort when process state (statics, singletons) is corrupted.

Closing the panel keeps the session alive for 10 minutes by default, so reopening reattaches instantly (`mewui.preview.keepSessionMinutes`). To end the session completely, run **MewUI: Stop Preview**.

---

## 5. Settings summary

| Setting | Default | Description |
|---|---|---|
| `mewui.preview.autoSelectTarget` | `true` | Follow the active editor file |
| `mewui.preview.keepSessionMinutes` | `10` | How long the session survives after the panel closes. `0` stops immediately |
| `mewui.preview.reloadDriver` | `auto` | `watch` (Hot Reload) / `buildRestart` (restart on save) / `auto` (falls back when watch fails) |
| `mewui.preview.sessionStartTimeoutSeconds` | `60` | Fall back to a shim session when no connection arrives within this long after app output goes quiet. `0` disables the fallback |

---

## 6. Troubleshooting

**Session start is slow**: the first start pays the full cold-build cost. Later starts are a few seconds (incremental build, restore skipped), and reopening a closed panel reattaches instantly thanks to session keep-alive.

**It says "shim session (low fidelity)"**: the app's `Main` never reached `Application.Run` (blocking, early exit, exception), so the preview restarted without the entry point. App themes/fonts do not apply in this mode. Check `Main` for code that blocks before `Application.Run` and apply the `Design.IsPreviewMode` guard.

**A target shows as disabled**: the type has required constructor arguments. Apply the optional-with-fallback pattern from section 3.2, or add a preview-only wrapper UserControl that fills in the arguments.

**The preview interferes with the real app (port conflicts, ...)**: apply the side-effect guards from section 3.1. A preview session is a real process too.

**Every update takes seconds**: check the status bar to see whether every save is treated as a rude edit. If plain method-body edits keep restarting the process, check project settings (Hot Reload opt-out, ...). Projects with `<MewUIHotReload>false</MewUIHotReload>` run the preview in restart-on-save mode.

**PublishAot projects**: no action needed. Preview sessions run under the JIT, and the required setting (StartupHookSupport) applies to session builds only. Publish output is unaffected.
