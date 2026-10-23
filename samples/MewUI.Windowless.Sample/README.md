# MewUI windowless lifecycle sample

This sample starts MewUI without a main window, registers a global `Ctrl+Alt+Space` hotkey, and creates the palette only after the hotkey is pressed. Closing the palette returns to zero user windows while the process and hotkey remain active. Use **Exit application** to release the provider and call `Application.Shutdown()`.

Enable **Hide instead of close** to retain and reuse the palette's native window. The hotkey and the title-bar close button then hide it, and the next hotkey shows the same instance again. A hidden window remains in `Application.Current.AllWindows`, so leave the option disabled when validating the zero-window lifecycle.

On Windows and macOS the sample also adds the app icon to the notification area (Windows) or the menu bar (macOS). The menu is an ordinary MewUI `ContextMenu`, set on the sample's `TrayIcon` with `.ContextMenu(...)` the way an element's context menu is set, and each platform shows it as its own native menu: a Win32 popup menu or an `NSMenu`, rebuilt from the model each time it opens. Text with `_` access keys, separators, submenus, disabled items and checked items carry over; MewUI theming, fonts and item layout do not. A macOS menu follows AppKit and shows the check column only while an item is checked, so unlike the MewUI context menu it does not keep a slot for a checkable item that is off. On Windows a click on the icon toggles the palette; on macOS a click opens the menu. MewUI has no tray API yet, so `Win32TrayIcon` calls `Shell_NotifyIconW` and `MacOSTrayIcon` creates an `NSStatusItem` directly. The menu's commands are registered in `Application.Current.Commands` and run through `Application.Current.CommandRouter`, which works while no window is open. Linux has no tray icon in this sample.

The sample selects `ShutdownMode.OnExplicitShutdown` through `WithShutdownMode`; windowless `Run` does not change the application's shutdown policy automatically.

## Providers and validation

| Environment | Provider | Validation |
|---|---|---|
| Windows | `RegisterHotKey` on a dedicated message-queue thread | Registration and automated window lifecycle verified on 2026-08-10; physical hotkey activation remains manual |
| macOS | Carbon `RegisterEventHotKey` | Registration and `--smoke` lifecycle verified on 2026-10-20 over SSH; physical hotkey activation remains manual |
| Linux/X11 | `XGrabKey` on a separate display connection | Build verified; runtime verification required on X11 |
| Linux/Wayland | GlobalShortcuts portal, with X11/XWayland fallback | Build verified; runtime verification required on a portal-enabled desktop |

Run the sample, confirm the startup log reports `user windows=0`, toggle and close the palette twice, and finally exit through the button. A platform is only considered runtime-verified after that complete sequence succeeds.

| Tray | Provider | Validation |
|---|---|---|
| Windows | `Shell_NotifyIconW` with a hidden window on the UI thread, served by MewUI's message loop | Icon placement, click toggle, the checked item and Exit verified on 2026-10-20 with injected tray messages and access keys; a physical click remains manual |
| macOS | `NSStatusItem` on the main thread | Item placed in the menu bar on 2026-10-20; opening and choosing from the menu remains manual |

Use `--smoke` to automate the window lifecycle after native provider registration: show, close to zero windows, show again, close, dispose the provider, and quit.
