# Use commands, menus, shortcuts, and toolbars

Create one `Command` per application action, register its handler in a command scope, and map keyboard gestures in an input scope. Menu and toolbar entries then share the same action and presentation.

```csharp
var status = new ObservableValue<string>("Ready");
var primary = ModifierKeys.Primary;

var create = new Command("app.file.new", "_New");
var open = new Command("app.file.open", "_Open");
var save = new Command("app.file.save", "_Save")
    .Description("Save the current document.");
var wrap = new Command("app.view.wrap", "_Word wrap");
var exit = new Command("app.file.exit", "E_xit");

window.Commands.Register(create, () => status.Value = "New document");
window.Commands.Register(open, () => status.Value = "Opened");
window.Commands.Register(save, () => status.Value = "Saved");
window.Commands.Register(wrap, () => status.Value = "Word wrap changed");
window.Commands.Register(exit, window.Close);
window.InputMap.Map(save, new KeyGesture(Key.S, primary));

var menuBar = new MenuBar()
    .Height(28)
    .Items(
        new MenuItem("_File").Menu(
            new Menu()
                .Item(create)
                .Item(open)
                .Item(save)
                .Separator()
                .Item(exit)));
```

Command IDs must be stable and unique within the application. An underscore marks the menu access key. `ModifierKeys.Primary` maps to the platform's primary command modifier.

Every command that can execute needs a handler in an ancestor `CommandScope`. Every shortcut needs a reachable `InputMap`. Put them on the `Window` for application-wide behavior or on a container for behavior that is active only while focus is inside that container.

## Toolbars

Toolbars organize commands into bands and groups. The same `save` command can appear in both the menu and toolbar:

```csharp
var toolbar = new ToolBar()
    .Band(
        new ToolBarGroup()
            .Item(create)
            .Item(open)
            .Item(save),
        new ToolBarGroup()
            .Label("View")
            .Toggle(wrap, isChecked: true));
```

Use `.Split(command, menu)` for a primary action with alternatives, `.Menu(text, menu, icon)` for a menu-only entry, and `.Host(element)` only when a real control such as a search box belongs in the band.

Keep command execution separate from presentation. Bind command text when the label must change at runtime, and use `OnCanClick` on a directly hosted `Button` or the command's availability mechanism supported by the selected package version for disabled state.

## Context menus

Attach a `ContextMenu` to its owner element and reuse registered commands:

```csharp
var editor = new TextBox()
    .ContextMenu(
        new ContextMenu()
            .Item(open)
            .Item(save)
            .Separator()
            .Item("Unavailable action", isEnabled: false));
```

Register handlers in the nearest stable owner rather than attaching a new click handler every time the menu opens. This keeps shortcut, enabled-state, and action behavior consistent across all entry points.
