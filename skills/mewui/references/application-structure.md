# Structure views and application lifetime

Keep startup short. Put long-lived state and services outside build functions, and make each
major screen a named `Window` or `UserControl` when it needs its own ownership, reuse, preview
target, or test boundary.

## Choose the view boundary

| Need | Define |
| --- | --- |
| Application top level, dialog, independent native surface | `Window` subclass |
| Reusable page, panel, card, or feature area inside a window | `UserControl` subclass |
| Small one-use visual fragment | private method returning `Element` |
| Repeated data-driven row or cell | typed item template |

Do not make every panel a class. Extract a named view when it owns state, commands, services,
preview sample data, or a meaningful feature boundary.

## Compose a named window and user control

Use constructor fields for dependencies and override `OnBuild` for repeatable visual composition.
A `UserControl` returns its root element; a `Window` configures itself.

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

var state = new AppState();

Application
    .Create()
    .UseWin32()
    .UseDirect2D()
    .WithShutdownMode(ShutdownMode.OnLastWindowClose)
    .BuildMainWindow(() => new MainWindow(state))
    .Run();

sealed class MainWindow : Window
{
    private readonly AppState _state;

    public MainWindow(AppState state)
    {
        _state = state;
        this.Resizable(900, 640, minWidth: 640, minHeight: 480);
    }

    protected override void OnBuild()
    {
        this.Title("Tasks")
            .Content(new HomeView(_state));
    }
}

sealed class HomeView : UserControl
{
    private readonly AppState _state;

    public HomeView(AppState state)
    {
        _state = state;
    }

    protected override Element? OnBuild() =>
        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Margin(20)
            .Children(
                new TextBlock().BindText(_state.Title).FontSize(24).Bold(),
                new TextBox().BindText(_state.Title));
}

sealed class AppState
{
    public ObservableValue<string> Title { get; } = new("My tasks");
}
```

The first layout pass builds a `UserControl` lazily after its constructor has initialized fields.
Do not call its protected `Build()` in an ordinary constructor. Call it only when code genuinely
needs `Content` before layout.

Keep one-shot window configuration and subscriptions in the constructor. Put configuration that
can safely run again in `OnBuild`; Hot Reload and Preview can re-run it. Never attach both a
fluent window `.Build(...)` callback and an overridden `Window.OnBuild()` to the same window.
Use an override for a named reusable window and fluent `.Build(...)` for a one-off composition-site
window.

## Keep state above rebuildable views

Every `ObservableValue<T>`, collection, command, and service must live at least as long as the
controls bound to it. State created inside `OnBuild` is recreated on a Hot Reload or Preview
refresh. Store durable state in the application state, window, parent view, or injected service.

For a medium application, a practical layout is:

```text
MewUIApp/
|-- MewUIApp.csproj
|-- Program.cs
|-- AppState.cs
|-- Views/
|   |-- MainWindow.cs
|   |-- HomeView.cs
|   `-- SettingsDialog.cs
`-- Services/
    `-- DataService.cs
```

MewUI uses C# Markup, so no XAML files, `DataContext`, code-behind pairing, or generated partial
views are required.

## Start without a main window

Use windowless startup for tray applications, global-hotkey palettes, background hosts, and apps
that create their first window later. Configure `OnStartup`, omit `BuildMainWindow`, and explicitly
select the shutdown policy:

```csharp
Application
    .Create()
    .UseWin32()
    .UseDirect2D()
    .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
    .OnStartup(() =>
    {
        // Register long-lived tray, hotkey, or background services here.
        // Create and show a Window only when the user requests it.
    })
    .Run();
```

Windowless `Run()` does not change `ShutdownMode` automatically. The default
`OnLastWindowClose` exits after the last later-created window closes. Use
`OnExplicitShutdown` when the process must remain alive with zero windows, and provide a real exit
path that disposes services and calls `Application.Shutdown()`.

`OnMainWindowClose` requires `Application.Current.MainWindow`. A run started without a main
window has no such identity; assign the later-created primary window before relying on that mode.
Use `RunAsync()` with a cancellation token when the surrounding host owns asynchronous lifetime.

Use `Window.Close()` for a graceful, cancellable close of one window. Use
`Application.Shutdown()` for explicit process-loop termination. UI state changes belong on the UI
thread. An `async void` event handler may await work and resume on the captured UI context, but it
must handle its own exceptions.
