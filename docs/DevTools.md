# DevTools

MewUI ships an element inspector, a visual tree window, a frame statistics overlay and a profiler timeline. They are off by default and you turn them on with one MSBuild property.

---

## 1. Enable

```xml
<PropertyGroup>
  <MewUIDevTools>true</MewUIDevTools>
</PropertyGroup>
```

The default is `true` in the `Debug` configuration and `false` in `Release`, so a plain `dotnet run` already has them. Set the property when you want them in a Release configuration, for example to profile optimized code.

Trimmed and NativeAOT publishes (`PublishTrimmed`, `PublishAot`) never host the DevTools. The property panel lists an element's CLR members by reflection, and a trimmed member list would quietly leave members out, so the build turns them off and warns instead of shipping a tool that lies. Everything the DevTools need is then removed from the output, which is why an app that never enables them pays nothing for their presence in the framework.

## 2. Open

| Tool | Shortcut |
| --- | --- |
| Element inspector | `Ctrl/Cmd+Shift+I` |
| Visual tree window | `Ctrl/Cmd+Shift+T` |
| Performance monitor | `Ctrl/Cmd+Shift+P` |
| Profiler timeline | `Ctrl/Cmd+Shift+Alt+P` |

`WindowDevTools.IsSupported` answers whether this build can host the tools at all, without needing a window. In trimmed and NativeAOT builds the trimmer folds it to a constant, so code you guard with it is removed along with the tools.

The tools themselves are reachable through `Window.DevTools`, which is `null` when the build did not enable them.

```csharp
window.DevTools?.ToggleInspector();

if (window.DevTools is WindowDevTools devTools)
{
    devTools.InspectorVisibleChanged += visible => status.Text = visible ? "Inspector on" : "Inspector off";
}
```

`WindowDevTools` carries a toggle, a state property and a change event for each tool.

| Tool | Toggle | State | Event |
| --- | --- | --- | --- |
| Element inspector | `ToggleInspector()` | `InspectorIsVisible` | `InspectorVisibleChanged` |
| Visual tree window | `ToggleVisualTree()` | `VisualTreeIsOpen` | `VisualTreeOpenChanged` |
| Performance monitor | `TogglePerformanceMonitor()` | `PerformanceMonitorIsVisible` | `PerformanceMonitorVisibleChanged` |
| Profiler timeline | `ToggleProfiler()` | `ProfilerIsOpen` | `ProfilerOpenChanged` |

The inspector and the performance monitor draw on the window itself, so they are visible or hidden. The visual tree and the profiler are windows of their own, so they are open or closed.

## 3. What each tool shows

**Element inspector** highlights the element under the cursor and shows its bounds, layout slot and the properties that decided its appearance. For a `Control` it also shows which style layer won each property.

**Visual tree window** lists the window's element tree, including popups and adorners, and selects the element you click in the target window. Selecting a node highlights it through the inspector overlay, so opening the tree turns the inspector on as well.

**Performance monitor** is an overlay with the recent frame times, draw calls and cull ratio. It draws after everything else in the frame so its own cost is reported in the numbers you read.

**Profiler timeline** records per-frame samples for layout, render, text and backend phases, and lets you pause with `Space`. Clicking a sample that belongs to an element highlights that element in the target window.

## 4. Cost when disabled

The DevTools code lives in the MewUI assembly, so no separate package or reference is involved. When the property is off, a single gate turns false and the frame loop, the input path and the profiler collection are skipped without allocating or measuring anything. In trimmed and NativeAOT publishes the trimmer folds that gate into a constant and removes the tools entirely.
