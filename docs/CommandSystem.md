# Command System

MewUI's Command System unifies keyboard input, buttons, menus, and direct code invocation through one semantic execution path.
It separates command identity (`Command`), execution scope (`CommandScope`), input gestures (`InputMap`), and presentation controls.

```text
InputMap / Button / Menu
          ↓
       Command
          ↓
    CommandRouter
          ↓
 CommandScope CanExecute / Execute
```

## Basic structure

A `Command` contains only an action identity and a stable `CommandPresentation`. Register execution delegates with a
`CommandScope` and key gestures with an `InputMap`. Two different `Command` instances remain different commands even
when they use the same `Id`.

```csharp
var save = new Command("file.save", "_Save");

window.Commands.Register(save, () => document.Save(), () => document.IsDirty);
window.InputMap.Map(save, new KeyGesture(Key.S, ModifierKeys.Primary));
```

The constructor `text` accepts `_Save` access-key markers; `__` represents one literal underscore. The source is stored
in `Command.Presentation.AccessText`, while `save.Text` returns the current normalized `"Save"`. Built-in
access-key-aware presenters such as menus also use `AccessKey` and `AccessKeyIndex`. Other consumers, including
toolbars, command palettes, and tooltips, can display `Command.Text` directly without leaking the marker.

Dispose the `CommandRegistration` returned by `CommandScope.Register`, or call `Unregister`, to remove a handler. A scope
can contain only one handler for each command.

## C# Markup usage

Connect a Button to a semantic action with `Command(...)` or `BindCommand(...)`. By default, Button continues to use
explicit `Content`. Pass a `CommandPresentationMode` to opt into generated command content.

```csharp
new Button()
    .Command(save, presentation: CommandPresentationMode.TextAndIcon)
```

Explicitly assigned or bound `Content` takes precedence over generated command presentation.

`DropDownButton` is intentionally not a Command consumer. Activating any part of it opens its
`DropDownMenu`; commands belong to the menu items. `SplitButton` is a Button and uses its inherited
`Command` for the primary face, while its drop-down face only opens the menu.

```csharp
var more = new DropDownButton
{
    Content = new TextBlock().Text("More"),
    DropDownMenu = new Menu().Item(exportPdf).Item(print),
};

var saveSplit = new SplitButton
{
    Content = new TextBlock().Text("Save"),
    Command = save,
    DropDownMenu = new Menu().Item(saveAs).Item(saveAll),
};
```

If `save` cannot execute, only the `SplitButton` primary face is disabled; the drop-down remains
reachable. The owner is the sole primary Command source—the template's internal buttons forward
activation and do not execute the Command themselves.

Each `SegmentButton` container in a `ButtonGroup` is also an independent Command consumer. Connect the per-item
Command in `PrepareContainer`; its `CanExecute` result participates in that segment's effective enabled state.

```csharp
new ButtonGroup()
    .Items(alignmentCommands, command => command.Text)
    .PrepareContainer<Command>((segment, command, _) =>
        segment.Command(command));
```

`SegmentedControl` remains a selection control rather than becoming a Command consumer. For independent actions such
as left/center/right/justify alignment, use `ButtonGroup` as above. To bind the current alignment as a selected value,
use `SegmentedControl.SelectedIndex`/`SelectedItem`. This connection does not require `CommandParameter`: each
`SegmentButton` receives the Command it executes.

### Reactive presentation and localization

`Command.Presentation.AccessText` and `Icon` are MewProperties. `Command.BindText(...)` and `BindIcon(...)` create
real one-way bindings to `AccessTextProperty` and `IconProperty`; they are not snapshot helpers.

```csharp
var save = new Command("file.save", icon: saveIcon)
    .BindText(AppStrings.Save); // ObservableValue<string>, e.g. "_Save"
```

When the source changes, the Command recomputes its text/access-key projection and updates open menus and opted-in
Buttons that use its default presentation. `CommandPresentation` deliberately excludes `CanExecute`, selection/check
state, shortcuts, and `CommandParameter`; those belong to execution state, the consumer, `InputMap`, and the invocation
context respectively.

Menus do not own callbacks or shortcuts either. They can use the Command's default text and access key together, or
override both for a particular presentation context.

```csharp
var fileMenu = new Menu()
    .Item(save)
    .Item("Save _As...", saveAs)
    .Separator()
    .Item("Unavailable", isEnabled: false); // Presentation-only item
```

`Item(string, Command)` and `MenuItem.Text` use the same underscore syntax. Explicit item text overrides both the
Command's default text and access key, allowing different menus to present the same command with different access keys.

The menu shortcut column looks up the effective `InputMap` gesture for the current command target. Do not duplicate
the shortcut declaration on the menu item.

Define a command icon with an `IconTemplate` that creates a new visual at the size requested by its presenter.
`IconTemplateSize.Dip` is the layout size and `Pixel` is the physical pixel requirement at the current DPI.
ContextMenu and MenuBar dropdowns use 16 DIPs. The default size for a future Toolbar presenter is 24 DIPs.

```csharp
var copyGeometry = PathGeometry.Parse(copyPathData);
copyGeometry.Freeze();

var copyIcon = new IconTemplate(
    size => new PathShape()
        .Data(copyGeometry)
        .Size(size.Dip)
        .Stretch(Stretch.Uniform));

var copy = new Command("edit.copy", "Copy", copyIcon);
```

Every `IconTemplate.Build` call must return a new parentless `FrameworkElement`. This allows multiple presenters to
show the same Command without conflicting over a visual parent. Non-visual resources such as `ImageSource`,
`SvgImageSource`, and frozen `PathGeometry` can be created outside the factory and shared. Return a new `Image` for
SVG, a new `PathShape` for geometry, or a new `TextBlock` for an emoji. Build the visual once when the presenter is
created, not on every render frame or `CanExecute` evaluation.
A raster factory can select the smallest source at least as large as `size.Pixel` and lay out the visual at
`size.Dip`. Active presenters rematerialize the icon when their DPI changes.

Core consumers that currently materialize Command icons are ContextMenu, MenuBar dropdowns, ToolBar entries, and Buttons (including the primary face of `SplitButton`) with a presentation mode. Button still defaults to explicit `Content`. All of them draw the icon at `ThemeMetrics.CommandIconSize` (16 DIP).

A MenuItem can override the Command icon.

```csharp
new MenuItem("_Copy", copy)
    .Icon(compactCopyIcon);
```

`MenuItem.Text` and `Icon` are placement overrides. They inherit from the Command only while the property has no value
source. An explicit empty string hides the text, and an explicit null icon suppresses the Command icon. `BindText`,
`BindIcon`, `BindCommand`, and `BindIsEnabled` create real bindings to the corresponding MenuItem MewProperties. The
local enabled value is ANDed with `CanExecute`; command evaluation never overwrites that binding.

## Routing and scopes

Element and Window each provide `Commands` and `InputMap`. Execution starts at the current target and searches the
element command context, Window, and Application in order. `CommandScope.Parent` can also define a semantic scope
chain that is independent of the visual tree.

The nearest scope handler owns the command. A handler whose `CanExecute` is `false` does not fall back to another
handler for the same command in a more distant scope. The nearest effective `InputMap` determines the gesture meaning
in the same way.

A dynamic ContextMenu that uses an explicit scope must also specify its target.

```csharp
var menu = new ContextMenu();
var scope = new CommandScope();
var select = new Command("document.select", "Select");

scope.Register(select, SelectDocument, CanSelectDocument);
menu.Item(select);
menu.SetCommandTarget(CommandTarget.From(scope));
menu.ShowAt(owner, position);
```

## Standard editing commands

`StandardCommands` provides `Cut`, `Copy`, `Paste`, `Delete`, `Undo`, `Redo`, and `SelectAll`. TextBox controls register
handlers for these commands in their own scope. Default gestures are mapped in the Application `InputMap`, so a local or
Window `InputMap` can remap or shadow them.

```csharp
editor.InputMap.Map(StandardCommands.Copy, new KeyGesture(Key.Insert, ModifierKeys.Control));
```

## TextBox, ContextMenu, InputMap, and the Edit menu

The following diagram is not a type inheritance hierarchy or the actual visual tree. It is a logical view of how
several UI entry points converge on the same editing command execution.

```text
Keyboard Primary+X/C/V
  └─ Application InputMap ───────────────┐
                                         │
TextBox right-click ContextMenu          ├─ StandardCommands.Cut/Copy/Paste
  └─ Cut / Copy / Paste menu items ──────┤             │
                                         │             ▼
MenuBar Edit menu                        │    Execute the TextBox handler
  └─ Cut / Copy / Paste menu items ──────┘    at the current command target
                                                       │
                                                       ▼
                                               Selection/clipboard changes
```

When a `TextBox` is created, it registers the execution and `CanExecute` handlers for `Cut`, `Copy`, and `Paste` with its
`Commands`. The default Application `InputMap` maps `Primary+X`, `Primary+C`, and `Primary+V` to those same standard
commands. ContextMenu and Edit menu items reference only the `Command`; they do not carry separate execution
delegates.

The following example attaches a custom ContextMenu to make the relationship explicit. Without an assigned
ContextMenu, TextBox creates its default editing menu from the same standard commands when needed.

```csharp
var editor = new TextBox()
    .Text("Select text, then use Cut or Copy.")
    .ContextMenu(
        new ContextMenu()
            .Item(StandardCommands.Cut)
            .Item(StandardCommands.Copy)
            .Item(StandardCommands.Paste));

var editMenu = new Menu()
    .Item(StandardCommands.Cut)
    .Item(StandardCommands.Copy)
    .Item(StandardCommands.Paste);

var menuBar = new MenuBar()
    .Items(new MenuItem("_Edit").Menu(editMenu));

// Place menuBar and editor in the layout of the same Window.
```

The menu objects neither inherit from TextBox nor duplicate its command handlers. The command target at the time a
menu opens or a key is pressed provides the actual connection.

- Keyboard: resolution starts at the focused TextBox and finds an effective `InputMap`. The Application mapping
  converts `Primary+X/C/V` to standard commands, and the router executes the focused TextBox handler.
- TextBox ContextMenu: the menu captures its right-click owner as the target. The commands therefore continue to
  operate on the original TextBox selection even after the menu takes focus.
- MenuBar Edit menu: the menu preserves the focus target that existed just before it opened. If a TextBox had focus,
  the Edit menu's `Cut`, `Copy`, and `Paste` commands route to that TextBox.

All three paths also share the same `CanExecute` result. With no selection, the TextBox `Cut` and `Copy` handlers are
not executable, so both ContextMenu and Edit menu disable those items. A read-only TextBox disables `Cut` and
`Paste`. The menu shortcut column looks up the effective target `InputMap`, so remapping a gesture does not require
editing separate shortcut strings in ContextMenu and the Edit menu.

## CanExecute and state updates

`CanExecute` must be fast and free of side effects. MewUI tracks only active command sources, such as connected
Buttons and open menus, and evaluates their state at the end of a dispatcher turn. Focus, property, and input-map
changes also trigger evaluation. Execution always checks `CanExecute` again immediately before invoking a handler.

When an unobservable value such as a regular field changes outside the UI thread, marshal the change to the UI
dispatcher. The default model does not perform arbitrary full visual-tree scans or expose per-command change events.

## Lifetime management

A Button is tracked as a command source only while connected to a visual root. A ContextMenu is tracked only while
open. Closing a Window clears that Window's source tracker. When registering a temporary handler in a long-lived
scope, dispose its `CommandRegistration` so captured objects are not retained unnecessarily.

## Removed legacy APIs

The following paths were removed because they could duplicate Command System execution or maintain conflicting
enabled states.

- `Window.KeyBindings`, `Window.ProcessKeyBindings`, and the core `KeyBinding`
- `Button.CanClick` and the C# Markup `OnCanClick`
- `MenuItem.Click`, `MenuItem.CanClick`, and `MenuItem.Shortcut`
- Callback-based `Menu.Item`/`ContextMenu.Item` overloads and shortcut arguments

The ordinary UI event `Button.Click` and its `OnClick` extension remain available. Use Command for reusable actions,
enabled conditions, shortcuts, and actions shared with menus.

## Icon lifetime and size

`Command.Icon` and `MenuItem.Icon` use `IconTemplate?`. A MenuItem inherits the Command icon only while its Icon has no
value source; an explicitly assigned null suppresses it. A ContextMenu builds each command item's template at 16 DIPs
when it opens and detaches the generated visual when it closes. Reopening the menu creates a new visual.

The factory receives both the DIP size and the target pixel size calculated for the current DPI. The presenter handles
DPI conversion and disabled opacity. Capture reusable sources instead of parsing them inside the factory. The presenter constrains the returned
element to a square slot, so `Stretch.Uniform` is recommended for vectors and bitmaps.
