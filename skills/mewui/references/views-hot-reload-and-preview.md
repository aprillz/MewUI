# Build views for Hot Reload and Preview

Use `OnBuild` as the repeatable composition boundary for named `Window` and `UserControl` views.
Use the fluent window `.Build(...)` callback only for a one-off window defined at its composition
site. MewUI registers these build owners for Hot Reload; application code does not add a metadata
update handler.

## Make build code re-runnable

Hot Reload and editor Preview may execute a build owner repeatedly. Separate responsibilities:

```csharp
sealed class SettingsDialog : Window
{
    private readonly SettingsState _state;

    public SettingsDialog(SettingsState? state = null)
    {
        _state = state ?? SettingsState.CreatePreviewFallback();

        // One-shot window specification and subscriptions stay outside OnBuild.
        this.Fixed(480, 360)
            .StartCenterOwner()
            .OnClosed(_state.SaveIfNeeded);
    }

    protected override void OnBuild()
    {
        // Re-runnable composition: replace content from durable state.
        this.Title("Settings")
            .Padding(20)
            .Content(new SettingsView(_state));
    }
}
```

- Keep durable state, services, commands, timers, and subscriptions outside `OnBuild`.
- Do not append to retained collections or duplicate event subscriptions during a rebuild.
- Keep window properties that cannot change after showing, such as startup placement, in the
  constructor or composition-site chain.
- Editing a build body rebuilds that node. Editing an existing event-handler body normally applies
  on the next event without rebuilding the view.
- Signature, base-type, and other structural edits can require a process restart.

Run a Debug/JIT application with:

```text
dotnet watch run
```

Hot Reload is inactive in Release and NativeAOT output. Disable it only when necessary with:

```xml
<PropertyGroup>
  <MewUIHotReload>false</MewUIHotReload>
</PropertyGroup>
```

## Make views discoverable by Preview

The MewUI editor Preview can display the live application main window and scan referenced
assemblies for named `Window` and `UserControl` subclasses. A scanned type is directly creatable
when it has a parameterless constructor or every constructor parameter has a default value.

If production uses constructor injection, provide safe optional fallbacks only when they represent
valid preview behavior:

```csharp
sealed class ProductCard : UserControl
{
    private readonly ProductCardState _state;

    public ProductCard(ProductCardState? state = null)
    {
        _state = state ?? ProductCardState.Sample;
        this.DesignSize(420, 260);
    }

    protected override Element? OnBuild() =>
        new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().BindText(_state.Name).Bold(),
                new TextBlock().BindText(_state.Description));
}
```

Do not weaken a required production dependency merely to satisfy Preview. When no honest fallback
exists, keep the constructor required and create a small preview-only wrapper `UserControl` that
supplies sample dependencies.

`DesignSize`, `DesignWidth`, and `DesignHeight` are preview hints. They do not set production
layout. Preview runs real application code in a real process, so guard only outward side effects:

```csharp
var configuration = LoadVisibleConfiguration();

if (!Design.IsPreviewMode)
{
    trayService.Install();
    server.Start(configuration.Port);
}

Application.Run(new MainWindow(configuration));
```

Allow themes, fonts, styles, resources, and visual composition to execute so the preview remains
representative. Guard sockets, global hooks, tray integration, device access, destructive writes,
and external service startup. Constructors can run again when targets change or views rebuild, so
preview branches do not replace ordinary cleanup and idempotency.

## Preview workflow

In the MewUI editor extension, open a C# file and run `MewUI: Start Preview`. Select the live main
window or a discovered named view. Use this loop:

1. Confirm the intended target is selected and rendered.
2. Edit an `OnBuild` body and save; expect a Hot Reload update.
3. Exercise Preview sample states, theme variants, clipping, and intended component size.
4. If composition becomes stale, refresh the target before restarting the process.
5. Treat compile errors and rude-edit restarts separately from rendering errors.

Preview is a development aid, not runtime verification. After previewing, build the package-only
project and run the real application. For publish work, also run the exact RID/NativeAOT profile;
Preview uses JIT session builds and does not prove trimming or AOT compatibility.
