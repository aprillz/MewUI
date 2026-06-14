# MewDock

A docking framework for MewUI: dockable document tabs and tool panes, drag-and-drop rearranging, splits,
auto-hide edges, maximize, and cross-window popouts. It is a C#-idiomatic port of
[FlexLayout](https://github.com/caplin/FlexLayout) with a Visual Studio style docking layer on top.

- Namespace: `Aprillz.MewUI.MewDock`
- Targets: `net8.0`, `net10.0`
- NativeAOT-friendly (source-generated, reflection-free JSON).

## Concepts

- **Document pane** - lives in the center document area, shown with a top tab strip and a maximize button.
  This is the default for a tab.
- **Pane (tool)** - docks to an edge or collapses to an auto-hide border (a thin strip that reveals on click).
  A pane wears a caption bar instead of a top tab strip; a single-tool group hides its tab strip entirely.
- **Group (tabset)** - a stack of panes sharing one frame. Splitting a group makes a row/column of groups.
- **Popout** - a pane or group torn out into its own OS window. Popout windows are owned by the host window
  (they do not show in the taskbar) and carry no title.
- **Center content** - the middle of the layout. By default it is the document host; a host can replace it
  with any element while tools still dock around it.

## Quick start

```csharp
using Aprillz.MewUI.MewDock;

var manager = new DockingManager
{
    // Builds content for panes restored from a saved layout, keyed off DockPane.Component.
    ContentFactory = pane => new TextBlock { Text = pane.Title },
};

// Load a layout (FlexLayout-compatible JSON).
manager.LoadLayout(layoutJson);

// Add panes at runtime with explicit content (bypasses ContentFactory).
manager.AddDocumentPane("Report", new ReportView());
manager.AddToolPane("Explorer", new ExplorerView(), DockEdge.Left);

// DockingManager is a Panel: drop it straight into a window.
var window = new Window().Resizable(1100, 740).Content(manager);
Application.Run(window);
```

## `DockingManager` (the facade)

One control that hosts the whole dock space. Add it to a window; it owns the model and the layout view.

| Member | Description |
|---|---|
| `LoadLayout(string json)` | Replace the layout from JSON. |
| `SaveLayout() : string` | Serialize the current layout (including dock sub-layouts and popouts). |
| `AddDocumentPane(string title, UIElement content, string? component = null) : DockPane` | Add a document tab to the center. |
| `AddToolPane(string title, UIElement content, DockEdge edge = Left, string? component = null) : DockPane` | Add a tool pane docked to an edge. |
| `ContentFactory : Func<DockPane, UIElement?>?` | Builds content for panes restored from a layout. |
| `HeaderFactory : Func<DockPane, UIElement?>?` | Custom tab-header content; null uses the default header. |
| `TabMenuOpening : EventHandler<DockTabMenuEventArgs>` | Raised each time a tab's right-click menu opens; add items to `e.Menu`. |
| `GroupMenuOpening : EventHandler<DockGroupMenuEventArgs>` | Raised each time a group's (tab-strip) menu opens. |
| `CenterContent : UIElement?` | Replace the document host with a custom center element. |
| `ActivePane : DockPane?` / `ActiveGroup : DockGroup?` | The focused pane and its group. |
| `DocumentPanes` / `Panes : IReadOnlyList<DockPane>` | Document panes / tool panes. |
| `Groups : IReadOnlyList<DockGroup>` | Every tab group. |
| `ActivePaneChanged : EventHandler<DockPane?>` | Raised when focus moves to a different pane. |
| `Changed : EventHandler` | Raised after any layout change. |

## `DockPane` (a handle)

A lightweight handle over one pane. It carries identity plus the common verbs; the layout itself stays inside
the manager.

| Member | Description |
|---|---|
| `Title` (get/set) / `Component` | Display title (setting it renames the tab) and the serialization key. |
| `IsDocument` / `IsActive` | Document pane vs tool pane; whether it is the active pane. |
| `Group : DockGroup?` / `Edge : DockEdge?` | The group it lives in (null when auto-hidden) and the edge it sits on. |
| `Content` | The element passed to `AddDocumentPane`/`AddToolPane` (null for factory-restored panes). |
| `Activate()` / `Close()` | Select or close the pane. |
| `Float()` / `FloatGroup()` | Pop the pane, or its whole group, out into a window. |
| `SplitOff(DockEdge)` | Split this pane off its own group toward an edge. |
| `MoveInto(DockGroup)` / `DockInto(DockGroup, DockEdge)` | Join another group as a tab / dock against one of its edges. |
| `Pin()` / `Unpin()` | Pin an auto-hide pane into a docked group, or send a docked group back to auto-hide. |

```csharp
manager.ActivePane?.FloatGroup();      // pop the active group out
manager.Panes[0].Unpin();              // send a tool back to its auto-hide edge
```

`DockEdge` is `Left | Top | Right | Bottom`.

## `DockGroup` (a handle)

A handle over one tab group (a tabset). Auto-hide borders are not groups, so an auto-hidden pane reports a null
`Group`.

| Member | Description |
|---|---|
| `IsDocument` / `IsMaximized` | Document group vs tool group; whether it is maximized. |
| `Edge : DockEdge?` | The edge the group is docked to (null for a document / floating group). |
| `Panes : IReadOnlyList<DockPane>` / `ActivePane : DockPane?` | The tabs in the group; the selected one. |
| `AddPane(string title, UIElement content, string? component = null) : DockPane` | Add a tab; the kind follows the group. |
| `Activate()` / `Close()` | Focus the group; close it and all its tabs. |
| `Float()` | Pop the whole group out into a window. |
| `ToggleMaximize()` | Maximize / restore (document groups only). |
| `Unpin()` | Send a docked tool group back to its auto-hide edge (tool groups only). |

## Placement and lookup

**Bootstrap vs group-level.** `DockingManager.AddDocumentPane` / `AddToolPane` work even when no group exists yet -
they create one. So you always start at the manager level, and the returned `DockPane`'s `Group` is the group that
was just created. You cannot create an empty group (an empty tabset is tidied away), so "create a group" is just
"add a pane".

```csharp
var pane = manager.AddDocumentPane("First", new View()); // fine on an empty layout, creates the group
var group = pane.Group;                                   // the new group
group.AddPane("Second", new View());                      // add to the same group (kind follows the group)
```

To target an existing group / pane:
- `DockGroup.AddPane(...)` - add a tab to that group; the new tab's kind follows the group's `IsDocument`.
- `DockPane.MoveInto(group)` / `DockInto(group, edge)` - move into another group / dock against one of its edges.

**There is no lookup API** - `Panes` / `DocumentPanes` / `Groups` are enumerable, so use LINQ:

```csharp
var grid = manager.Panes.FirstOrDefault(p => p.Component == "grid");
```

## Tab context menu

Right-clicking a tab opens a context menu whose default items depend on the tab kind:

- **Document tab**: Close / Close Others / Close All / Float / New Vertical Tab Group / New Horizontal Tab Group /
  Move to Next/Previous Tab Group / Maximize-Restore. The close variants and the splits are scoped to the tab's own
  group; "Move to ... Tab Group" targets the adjacent group in traversal order (disabled at the ends).
- **Tool tab**: mirrors the tool caption - a pinned tool shows Float / Auto Hide / Close; an auto-hidden (border)
  tab shows Dock / Float / Close.

Right-clicking the **empty tab-strip area** opens the group menu (Float / Maximize-Restore or Auto Hide / Close
All), customizable with the `GroupMenuOpening` event.

Append app commands by handling `TabMenuOpening`, raised each time the menu opens (after the defaults):

```csharp
manager.TabMenuOpening += (s, e) =>
{
    e.Menu.AddSeparator();
    e.Menu.AddItem("Copy Path", () => CopyPath(e.Pane));
};
```

Default labels come from `MewUIDockString`, so they localize with the rest.

## Layout JSON

`LoadLayout` / `SaveLayout` use a FlexLayout-compatible model. A tab is a document by default; mark it a pane
with `"isDocument": false` (auto-hide border tabs are stamped panes automatically).

```json
{
  "global": {},
  "borders": [
    { "location": "left", "children": [
      { "type": "tab", "name": "Explorer", "component": "grid" }
    ]}
  ],
  "layout": {
    "type": "row",
    "children": [
      { "type": "tabset", "weight": 60, "children": [
        { "type": "tab", "name": "Report", "component": "chart" }
      ]},
      { "type": "tabset", "weight": 40, "children": [
        { "type": "tab", "name": "Notes", "component": "notes", "enableClose": false }
      ]}
    ]
  }
}
```

## Content and persistence

A pane gets its content one of two ways:

1. **Explicit content** - `AddDocumentPane` / `AddToolPane` / `DockGroup.AddPane` take a live element, stored
   against the pane and shown directly. It does not go through `ContentFactory`.
2. **Factory content** - a tab loaded from JSON carries a `component` key. When the view needs its body it calls
   `ContentFactory(pane)`, and the host builds the element by reading `pane.Component`.

`component` is just a string the framework carries on the node and serializes; it does **not** map to content by
itself. The mapping lives in your `ContentFactory` (a `switch` or dictionary on `pane.Component`):

```csharp
manager.ContentFactory = pane => pane.Component switch
{
    "grid"  => new GridView(),
    "chart" => new ChartView(),
    _       => new TextBlock { Text = pane.Title },
};
```

Persistence follows from this: `SaveLayout` writes each tab's `name` and `component`, not its live element. So a
pane survives `Save` -> `Load` only if it has a `component` that `ContentFactory` can rebuild. The Add methods take
a `component` argument, so **passing a key when you add** gives you both immediate display (explicit content) and
round-trip restore (rebuilt from the key by `ContentFactory`):

```csharp
manager.AddDocumentPane("Report", new ReportView(), component: "report"); // shown now + survives save/load
```

Omit `component` and the pane is shown immediately but is **not** restored after a round-trip (re-add it after
loading).

## Localization

User-visible strings live in `MewUIDockString` as `ObservableValue<string>` (menus, tooltips, drag chips, the
unnamed-tab fallback). Defaults are English. Set `.Value` to translate; bound consumers (tooltips) update
immediately, transient ones (menus, drag chips) pick up the new value the next time they are shown.

```csharp
MewUIDockString.MenuClose.Value = "닫기";
MewUIDockString.ToolTipClose.Value = "닫기";
```

The empty center carries no text - put a start page or branding there via `CenterContent` if you want one.
