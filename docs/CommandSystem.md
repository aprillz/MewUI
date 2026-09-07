# Command System

MewUI's command system unifies keyboard input, buttons, menus, toolbars, and direct code invocation through one semantic execution path. It is split so that five questions are each answered by a different axis: what the action is (`Command`), who runs it (the scope), where the search starts (the target), what it acts on (the argument), and which value it picks (data).

```text
Surface (Button / MenuItem / ToolBar entry / InputMap)   "run this command" + the value the item declares (CommandData)
        │
        ▼ Target (CommandTarget): where to start
   CommandRouter ── walks the context chain looking for a scope
        │
        ▼ Scope (CommandScope): the registered handler
   Handler ── runs with the argument
```

## Command: identity

A `Command` carries only the identity of an action and its presentation (`CommandPresentation`). What it does and whether it can run right now live entirely outside the `Command`. Menus, toolbars, and shortcuts share one `Command` instance, and the context it runs in gives it meaning. Two different instances remain different commands even when they use the same `Id`.

```csharp
var save = new Command("file.save", "_Save", saveIcon);

window.Commands.Register(save, () => document.Save(), () => document.IsDirty);
window.InputMap.Map(save, new KeyGesture(Key.S, ModifierKeys.Primary));
```

The constructor `text` accepts `_Save` access-key markers; `__` stands for one literal underscore. The source is kept in `Command.Presentation.AccessText`, while `save.Text` returns the normalized `"Save"`. Surfaces that support access keys, such as menus, also use `AccessKey` and `AccessKeyIndex`. Consumers that show no mnemonic, such as toolbars, command palettes, and tooltips, can display `Command.Text` directly without leaking the marker.

## Scope: who runs it

A scope (`CommandScope`) pairs commands with handlers. Every element and window has its own scope through `Commands`, and `Application` has one too. A scope holds one handler per command; dispose the `CommandRegistration` that `Register` returns, or call `Unregister`, to remove it. `CommandScope.Parent` builds a semantic chain independent of the visual tree.

The registration shapes differ by what they bind. They are all called `Register`.

| What is bound | Shape | Meaning |
|---|---|---|
| Delegates | `Register(cmd, Action, Func<bool>?)` | Execute and can-execute |
| The invocation context | `Register(cmd, Action<CommandContext>, Func<CommandContext, bool>?)` | When the window, the source element, or the cancellation token is needed. An asynchronous `Func<CommandContext, ValueTask>` form exists too |
| A target object | `Register(cmd, target, static t => ..., static t => ...)` | Closure-free static lambdas |
| An argument type | `Register(cmd, (Item item) => ..., (Item item) => ...)` | A handler that receives the invocation argument (see the argument section) |

Put the handler on the scope of **the element that owns the state**. A command that edits a document goes on the editor, one that acts on a chat message goes on the card holding the message list, one that changes a shape's fill goes on the shape. Do not put it on a recycled element such as an item container: once the container takes another item, the registration points at the wrong one. The argument section shows how a handler learns which item it was invoked on.

Whether to use a command or a binding depends on how many surfaces consume the meaning. One surface editing one piece of state (a form checkbox, a radio group, a segmented control) is a binding. A meaning shared by two or more surfaces (a menu, a toolbar, and a shortcut), or one whose meaning depends on focus (save the active document), is a command.

## Target: where the search starts

The target (`CommandTarget`) is where the router starts looking for a handler. It is an element or a standalone scope, and it is opaque. The router walks the target element's context chain looking for a scope, then consults `CommandRouter.FallbackTarget`, the window scope, and the `Application` scope in that order. Key gestures resolve the same way: the nearest `InputMap` decides what a gesture means.

The nearest handler owns the command. When its `CanExecute` is false the router does not move on to a farther scope, which lets an inner scope shadow an outer command as "not right now".

Each surface picks its target differently.

| Surface | Target |
|---|---|
| Button, ToggleButton, toolbar entries | Itself |
| Shortcut | The focused element |
| ContextMenu | The placement target (`PlacementTarget`) captured when it opens. The menu takes focus, but commands still run against the element it opened over, and submenus inherit the same target |
| MenuBar | The element focused just before the menu opened |
| A dynamic menu with a standalone scope | `menu.SetCommandTarget(CommandTarget.From(scope))` |

A surface that lives outside the document, such as a toolbar, reaches the active document's handlers only when the shell points `CommandRouter.FallbackTarget` at that document. Handlers never learn what the target was and do not need to. The same handler is meant to run with the same meaning whether a menu or a shortcut invoked it, which is why `CommandContext` does not expose the target. When a handler needs the thing it was invoked on, the argument section provides it.

```csharp
var menu = new ContextMenu();
var scope = new CommandScope();
var select = new Command("document.select", "Select");

scope.Register(select, SelectDocument, CanSelectDocument);
menu.Item(select);
menu.SetCommandTarget(CommandTarget.From(scope));
menu.Show(owner);
```

## Argument: what it acts on

When "Delete" is picked from a context menu over a list, the handler has to know which item. That value is the argument, and the framework finds it and passes it in.

An element supplies arguments by implementing `ICommandArgumentSource`. The `ItemContainer` and `GridViewRow` that item controls create already do, so their `Item` becomes the argument. There is one rule: **the value of the nearest source above the invocation anchor is the argument.** The anchors are those of the target table: a menu starts at its placement target, a shortcut at the focused element, a button at itself. A button inside an item template therefore receives its own item.

The receiver is a typed handler.

```csharp
var delete = new Command("chat.delete", "Delete");
var reply = new Command("chat.reply", "Reply");

card.Commands.Register(delete, (ChatMessage msg) => messages.Remove(msg), (ChatMessage msg) => msg.Mine);
card.Commands.Register(reply, (ChatMessage msg) => input.Value = $"@{msg.Sender} ");

list.PrepareContainer<ChatMessage>((container, _, _, _) => container.ContextMenu = messageMenu);
```

The handler holds no menu reference, walks no ancestors, and casts nothing. Always write the lambda parameter type: an untyped `msg => ...` resolves to the `CommandContext` shape instead.

When there is no argument, or its type does not match, the handler evaluates as unable to run and the menu item or button is drawn disabled. Per-item enabling, such as the `msg.Mine` predicate above, goes through the same path. The target rule that the nearest handler owns the command applies here too: invoking without an argument inside a chain that has a typed handler does not fall through to an untyped handler in an outer scope.

A ContextMenu captures the argument the moment it opens. If the list scrolls while the menu is up and the container takes another item, picking an entry still acts on the item the menu opened over. If that item was removed after the menu opened, the handler receives an item that is already gone, so a removal handler does nothing.

## Data: which value it picks

Commands that pick one value, such as alignment (left, center, right) or a fill color, differ per item only in the value. That value belongs not to the command but to **the surface item, as `CommandData`.** The command is the single verb "change the fill"; which value is the item's data.

```csharp
var setFill = new Command("shape.fill", "Fill");
sharp.Commands.Register(setFill, (Color color) => sharp.Fill(color));
rounded.Commands.Register(setFill, (Color color) => rounded.Fill(color));

var fillMenu = new ContextMenu()
    .Item("Blue", setFill, Color.FromRgb(70, 130, 230))
    .Item("Green", setFill, Color.FromRgb(100, 200, 120));

sharp.ContextMenu(fillMenu);
rounded.ContextMenu(fillMenu);
```

An item that declares data passes it as the invocation argument, and a typed handler receives it. The value set can be large and can change at run time while the command stays one: a symbol list or a font list menu is one command plus generated items. Surfaces that show a value like an editor (combo boxes, sliders) remain the domain of bindings.

Every invoking surface can carry data.

| Surface | Declaration |
|---|---|
| Menu item | `Item(text, command, data)`, `MenuItem.CommandData` |
| Button, ToggleButton | The `CommandData` property, fluent `.CommandData(value)` |
| Toolbar entry | `Item(command, data, icon)`, `Toggle(command, data, icon)`, `ToolBarItem.CommandData` |
| Shortcut | `Map(command, data, gesture)`. One command can map to a different gesture per value |

When an item declares data, that data replaces the operand from the argument section. One invocation carries one argument.

Because value items share one command, text and icons are written per item: the menu item's text, and the `Text` and `Icon` overrides of a toolbar entry. Shortcut labels are looked up by the command and data pair, so the "Left" item shows only Ctrl+L.

## Checked state

On/off state belongs to the surface, not to the command. `ToggleButton.IsChecked` belongs to the control, and a toolbar `Toggle` entry's checked state belongs to `ToolBarToggleItem.IsChecked`; the control the toolbar creates writes the user's change back to it. Menu items have no check mark. When several surfaces must show the same state, bind that state to each of them.

## C# Markup usage

Connect a Button to a semantic action with `Command(...)` or `BindCommand(...)`. By default a Button keeps its explicit `Content`; pass a `CommandPresentationMode` to generate content from the command.

```csharp
new Button()
    .Command(save, presentation: CommandPresentationMode.TextAndIcon)
```

Explicitly set or bound `Content` wins over generated command content.

`DropDownButton` is deliberately not a command consumer. Activating any part of it opens `DropDownMenu`, and the commands belong to the menu items. `SplitButton` is a Button, so its primary face runs the inherited `Command` while the drop-down face only opens the menu.

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

When `save` cannot run, only the `SplitButton` primary face is disabled and the drop-down still opens. Only the owning control is a primary command source; buttons inside its template forward activation and never run the command themselves.

Each `SegmentButton` container of a `ButtonGroup` is an independent command consumer. Assign the per-item command in `PrepareContainer`; its `CanExecute` feeds that segment's effective enabled state.

```csharp
new ButtonGroup()
    .Items(alignmentCommands, command => command.Text)
    .PrepareContainer<Command>((segment, command, _) =>
        segment.Command(command));
```

`SegmentedControl` is a selection control that picks one value, so it does not become a command consumer. Use `ButtonGroup` as above when each item is an independent action, such as align left, center, right, or justify, and use `SegmentedControl.SelectedIndex`/`SelectedItem` when the current alignment should be a selected value. This wiring needs no `CommandData`: each segment receives the command it runs.

### ToolBar

A `ToolBar` holds bands, a band holds groups, and a group holds entries. Entries are models rather than controls: the toolbar creates one control per entry, and groups a band cannot fit are hidden behind that band's overflow button. `Item`, `Toggle`, and `Split` take a command; `Menu`, `Label`, `Splitter`, and `Host` do not.

```csharp
var save = new Command("file.save", "_Save", saveIcon);
var wrap = new Command("view.wordWrap", "_Word wrap", wrapIcon);

var bar = new ToolBar()
    .Band(
        new ToolBarGroup()
            .Item(new Command("file.new", "_New", newIcon))
            .Split(save, new Menu().Item(saveAs).Item(saveAll)),
        new ToolBarGroup()
            .Label("View")
            .Toggle(wrap, isChecked: true)
            .Menu("Zoom", new Menu().Item(zoomIn).Item(zoomOut), zoomIcon)
            .Separator()
            .Host(new TextBox().Width(140).Placeholder("Search")));

bar.Commands.Register(save, () => document.Save(), () => document.IsDirty);
```

`Item` becomes a button, `Split` becomes a `SplitButton` whose primary face runs the command and whose chevron opens the menu, and `Menu` becomes a `DropDownButton` with no command of its own. `Host` is the one entry a band cannot hide: an arbitrary element has no row to fold into, so it shrinks to its minimum size instead.

`Toggle` runs a command and also shows an on/off state. The checked state belongs to the entry, not the command: `ToolBarToggleItem.IsChecked` is the source, and the created control writes the user's change back to it.

Entries that pick a value declare their data together with a per-entry icon. Three entries share one command, so the icon comes from the entry rather than the command.

```csharp
new ToolBarGroup()
    .Toggle(setAlignment, TextAlignment.Left, alignLeftIcon)
    .Toggle(setAlignment, TextAlignment.Center, alignCenterIcon)
    .Toggle(setAlignment, TextAlignment.Right, alignRightIcon)
```

Entries show their command according to `ToolBar.ItemPresentation`, which defaults to `CommandPresentationMode.Icon`. A single entry can override it with `ToolBarItem.Presentation`, and `Text` and `Icon` show the entry's own presentation instead of the command's. An icon-only entry whose command has no icon falls back to text rather than rendering empty. Icons are drawn at `ThemeMetrics.CommandIconSize`, the same size menus use.

### Reactive presentation and localization

`Command.Presentation.AccessText` and `Icon` are MewProperties. `Command.BindText(...)` and `BindIcon(...)` are not one-shot copies: they create real one-way bindings to `AccessTextProperty` and `IconProperty`.

```csharp
var save = new Command("file.save", icon: saveIcon)
    .BindText(AppStrings.Save); // ObservableValue<string>, for example "_Save"
```

When the value changes, the command's `Text` and access key are recomputed, and open menus and opt-in buttons that use the command's default presentation update. `CommandPresentation` never carries `CanExecute`, selection or checked state, shortcuts, or invocation values; those belong to execution state, the consumer, `InputMap`, and the item's `CommandData` respectively.

Menus own no callbacks and no shortcuts of their own either. A menu item can use the command's default text and access key, or override both together to fit the context it appears in.

```csharp
var fileMenu = new Menu()
    .Item(save)
    .Item("Save _As...", saveAs)
    .Separator()
    .Item("Unavailable", isEnabled: false); // presentation-only item
```

The strings passed to `Item(string, Command)` and `MenuItem.Text` follow the same `_` rule. Explicit item text overrides both the command's default text and its access key, so the same command can show a different access key in different menus.

A menu's shortcut column is a reverse lookup of the `InputMap` gesture that is actually effective at the current command target, so shortcuts are never declared twice.

Command icons are `IconTemplate` factories that build a fresh visual at the size the surface requests. `IconTemplateSize.Dip` is the layout size; `Pixel` is the physical pixel size at the current DPI. ContextMenu, MenuBar dropdowns, and ToolBar entries all request `ThemeMetrics.CommandIconSize` (16 DIP).

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

`IconTemplate.Build` must return a new parentless `FrameworkElement` on every call, so several surfaces can show the same command without fighting over a visual parent. Non-visual resources such as `ImageSource`, `SvgImageSource`, or a frozen `PathGeometry` are created outside the factory and shared. Build happens once when a surface is created, not per frame or per `CanExecute` evaluation. A bitmap factory can pick the smallest source at or above `size.Pixel` and lay the visual out at `size.Dip`. When the DPI changes, active surfaces rebuild their icon visuals at the new size.

The core consumers that materialize command icons are ContextMenu, MenuBar dropdowns, ToolBar entries, and Buttons with a presentation mode (including a `SplitButton` primary face). A Button's default remains explicit `Content`.

A menu item can override the command's icon.

```csharp
new MenuItem("_Copy", copy)
    .Icon(compactCopyIcon);
```

`MenuItem.Text` and `Icon` are **placement overrides**: the command default is used only while the property has no value source. An explicit empty string hides the text, and an explicit `null` icon hides the command icon. `BindText`, `BindIcon`, `BindCommand`, and `BindIsEnabled` create real bindings to the corresponding MenuItem MewProperties. Local `IsEnabled` is combined with `CanExecute` by AND and is never overwritten by the binding.

## ContextMenu placement

A `ContextMenu` opens with `Show(placementTarget)`. `Placement` decides where it appears.

| `Placement` | Position |
|---|---|
| `Pointer` (default) | At the pointer. Pass the position with `Show(target, positionInWindow)` |
| `Below` / `Above` | Under or over the target's edge, flipping to the other side when there is no room |
| `Right` / `Left` | Beside the target's right or left edge, flipping to the other side when there is no room |

`PlacementOffset` nudges the menu from that position, and once open, `PlacementTarget` tells which element it opened over. A menu assigned to an element's `ContextMenu` property opens on right-click automatically: at the pointer when `Placement` is `Pointer`, otherwise anchored to that element. Submenus inherit the parent's placement target and command target.

```csharp
new Button()
    .Content("Options")
    .ContextMenu(new ContextMenu { Placement = MenuPlacement.Below }
        .Item(exportPdf)
        .Item(print));
```

## Standard editing commands

`StandardCommands` provides `Cut`, `Copy`, `Paste`, `Delete`, `Undo`, `Redo`, and `SelectAll`. TextBox-family controls register handlers for them on their own scope. The default keys are mapped in the Application `InputMap`, so a local or Window `InputMap` can remap or shadow them.

```csharp
editor.InputMap.Map(StandardCommands.Copy, new KeyGesture(Key.Insert, ModifierKeys.Control));
```

## TextBox, ContextMenu, InputMap, and the Edit menu

The diagram below is neither a type hierarchy nor a visual tree. It is a **logical composition** showing how one editing meaning reached from several entry points collapses into a single command execution.

```text
Keyboard Primary+X/C/V
  └─ Application InputMap ───────────────┐
                                         │
TextBox right-click ContextMenu          ├─ StandardCommands.Cut/Copy/Paste
  └─ Cut / Copy / Paste menu items ──────┤             │
                                         │             ▼
MenuBar Edit menu                        │    Executes the TextBox handler
  └─ Cut / Copy / Paste menu items ──────┘    at the current command target
                                                       │
                                                       ▼
                                             Selection / clipboard changes
```

A `TextBox` registers execute and `CanExecute` handlers for `Cut`, `Copy`, and `Paste` on its own `Commands` when it is created. The default Application `InputMap` maps `Primary+X`, `Primary+C`, and `Primary+V` to the same standard commands. The ContextMenu and the Edit menu hold no delegates and only reference those commands.

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
```

The menus do not copy the TextBox handlers. The connection is decided by the **command target** at the moment a menu opens or a key is pressed. The keyboard starts at the focused TextBox and finds the effective `InputMap`; the TextBox context menu captures the right-clicked TextBox as its target; the MenuBar Edit menu preserves the target focused just before it opened. All three share the same `CanExecute` results: with no selection, `Cut` and `Copy` are disabled in both menus, and in a read-only TextBox `Cut` and `Paste` are disabled. Shortcut labels are reverse lookups against the current target, so remapping a key needs no menu edits.

## When state is re-queried

`CanExecute` and argument predicates must be cheap and side-effect free. The framework stores nothing and asks again when it needs to. It asks:

- at the end of a dispatcher turn that processed work
- on mouse button release, focus change, and window state change
- when a menu opens, and at each of the moments above while it is open
- when the application calls `window.RequerySuggested()`

Only tracked surfaces, such as attached Buttons and open menus, are re-evaluated; the visual tree is never scanned. Right before execution, `CanExecute` is checked again regardless of what the surface showed.

State the framework cannot observe, such as a plain field changed outside those moments, needs a `RequerySuggested()` call or a change routed through the dispatcher before surfaces follow. `Button.CanClick` and `MenuItem.CanClick` are predicates for conditions local enough that a command would be ceremony; they combine with local `IsEnabled` and the command's `CanExecute` by AND, so any one of the three disables the surface, and they are asked again at the same moments.

## Lifetime management

Buttons are tracked as command sources only while attached to a visual root, and a ContextMenu only while open. Closing a window clears its source tracker. When a temporary handler is registered on a long-lived scope, dispose its `CommandRegistration` so captured objects are not kept alive.

## Removed legacy APIs

The following paths were removed because they produced duplicate execution or a different enabled state from the command system.

- `Window.KeyBindings`, `Window.ProcessKeyBindings`, core `KeyBinding`
- `MenuItem.Click`, `MenuItem.Shortcut`
- callback-based `Menu.Item`/`ContextMenu.Item` and their shortcut arguments
- `ContextMenu.ShowAt` is obsolete, replaced by `Show` and `Placement`

`Button.Click`/`OnClick` remains for plain UI clicks; behavior that is reused, gated, given a shortcut, or shared with a menu uses a command.

## Icon lifetime and size

`Command.Icon` and `MenuItem.Icon` are `IconTemplate?`. `MenuItem.Icon` falls back to the command icon only while it has no value source; an explicit null hides it. A ContextMenu builds each command item's template at 16 DIP when it opens and releases the visuals' parents when it closes; reopening builds new visuals.

The factory receives the DIP size and the target pixel size computed from the current DPI. DPI conversion and disabled opacity are the surface's job. Capture a shareable source outside the factory instead of parsing it on every call. The surface constrains the returned element to a square slot, so `Stretch.Uniform` is recommended for vectors and bitmaps.

## At a glance

| Axis | Question | Lives in | What the surface knows |
|---|---|---|---|
| Command | What action | The `Command` instance | Identity and presentation |
| Scope | Who runs it | `Element.Commands`, `Window.Commands`, `Application` | Nothing |
| Target | Where the search starts | Captured by the surface at invocation | Its own way of capturing |
| Argument | What it acts on | The nearest `ICommandArgumentSource` above the anchor | Menus capture it on open |
| Data | Which value | The surface item's `CommandData` | Its own declared value |
