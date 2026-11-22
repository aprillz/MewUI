# Tray Icon

`TrayIcon` shows an icon for the application in the Windows notification area, the macOS menu bar or a Linux desktop's status area, with an optional menu. It needs no window, so a utility that runs without a main window (see [Application Lifecycle](ApplicationLifecycle.md)) can live in the tray alone. On other platforms `TrayIcon.IsSupported` is false and the icon does nothing.

On Linux the icon uses the StatusNotifierItem protocol, which KDE Plasma and Xfce panels host. GNOME shows it only with the AppIndicator extension installed. `TrayIcon.IsSupported` is false while the desktop has no host, and a visible icon appears by itself when one starts.

```csharp
var show = new Command("app.show", "_Show");
var exit = new Command("app.exit", "E_xit");

var tray = new TrayIcon()
    .Icon(IconSource.FromResource<App>("App.app.ico"))
    .ToolTipText("My App")
    .ContextMenu(new ContextMenu()
        .Item(show)
        .Separator()
        .Item(exit))
    .Command(show)
    .IsVisible();

tray.Commands.Register(show, ShowMainWindow);
tray.Commands.Register(exit, Application.Shutdown);
```

## Showing and removing

Nothing appears until `IsVisible` is true while the application runs. The icon itself is created on a later dispatcher turn, so properties set after `IsVisible` in an initializer or a fluent chain are already in place when it first appears. A `TrayIcon` may be created before `Application.Run`; it appears once the run starts.

Setting `IsVisible` to false removes the icon and setting it back shows it again. `Dispose` removes it for good. When the run ends, every tray icon is removed and disposed, so no icon is left behind in the notification area.

Without an `Icon`, the application's own icon is shown. `IsMonochromeIcon` marks the icon as a single-color glyph that the platform may tint to match the bar; the macOS menu bar does, other platforms show the icon as it is.

Use a `TrayIcon` on the UI thread.

## The menu

The menu is an ordinary `ContextMenu`, built the same way as an element's context menu. MewUI presents it the way tray menus appear on each platform:

| Platform | Presentation |
|---|---|
| Windows | The `ContextMenu` itself, with the application's theme, fonts, icons and item layout. It opens at the icon and closes on a click outside, on Escape, or when another window becomes active. The keyboard works as in any MewUI menu. |
| macOS | A native menu read from the `ContextMenu` each time it opens: item text (macOS menus have no access keys, so the `_` markers are dropped), separators, submenus, enabled state, check marks, icons and shortcut labels. Single-color icons follow the menu's colors. The `ContextMenu`'s own look (`ItemHeight`, `ItemPadding`, `MaxMenuHeight`, fonts) does not apply. |
| Linux | A menu the desktop's panel draws from the `ContextMenu`: item text with access keys, separators, submenus, enabled state, check marks, icons and shortcut labels. The `ContextMenu`'s own look does not apply. |

`MenuOpening` is raised just before the menu opens. A handler may fill the menu or replace `ContextMenu`; it cannot cancel the opening.

## Commands

The icon's `Command`, and the commands of its menu items, resolve in `TrayIcon.Commands` first and then in `Application.Current.Commands`. They run through `Application.Current.CommandRouter`, so they work while no window is open, and a handler receives a `CommandContext` whose `Window` is null. An item's `CommandData` is passed as the argument. See [Command System](CommandSystem.md#running-without-a-window).

An item is enabled only while its command has a handler that can run. A menu item's shortcut label comes from `Application.Current.InputMap`.

## Clicks

`Click` is raised, and then `Command` runs, when the platform reports a primary activation of the icon apart from opening its menu. `DoubleClick` is raised where the platform reports a double click; the clicks that make it raise `Click` as well. `TrayIconEventArgs.ScreenPosition` is the position on the screen in device pixels, or the icon's position for a keyboard activation.

| Platform | Click | Double click | Menu |
|---|---|---|---|
| Windows | Left click, or Enter or Space on the focused icon | Yes | Right click, Shift+F10, or the menu key |
| macOS | A click, while the icon has no menu | No | A click, while the icon has a menu |
| Linux | A left click, where the panel reports it | No | A right click. Some panels also open the menu on a left click and report no `Click`, so offer every action in the menu as well |

## Application identifier

`WithApplicationId` names the application to the desktop, which uses the name to group its windows, tray icons and launcher entry:

```csharp
Application.Create()
    .WithApplicationId("com.example.MyApp")
    .BuildMainWindow(() => new MainWindow())
    .Run();
```

On Linux the identifier is the window class and the tray item's id, and should match the name of the application's `.desktop` file. Without one, the entry assembly's name is used. On Windows it is the process's AppUserModelID, and is left unset unless the application sets it. macOS uses the bundle identifier instead.
