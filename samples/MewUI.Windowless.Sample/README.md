# MewUI windowless lifecycle sample

This sample starts MewUI without a main window, registers a global `Ctrl+Alt+Space` hotkey, and creates the palette only after the hotkey is pressed. Closing the palette returns to zero user windows while the process and hotkey remain active. Use **Exit application** to release the provider and call `Application.Shutdown()`.

Enable **Hide instead of close** to retain and reuse the palette's native window. The hotkey and the title-bar close button then hide it, and the next hotkey shows the same instance again. A hidden window remains in `Application.Current.AllWindows`, so leave the option disabled when validating the zero-window lifecycle.

On Windows and macOS the sample also adds the app icon to the notification area (Windows) or the menu bar (macOS) with MewUI's `TrayIcon`. Its menu is an ordinary MewUI `ContextMenu`, set with `.ContextMenu(...)` the way an element's context menu is set. On Windows MewUI draws that menu with the app's theme; on macOS it becomes a native menu read from the model each time it opens. A click on the icon runs the icon's command, which toggles the palette, on Windows; on macOS a click opens the menu. The commands are registered in `Application.Current.Commands` and run through `Application.Current.CommandRouter`, which works while no window is open. Linux has no tray icon in this sample. See [Tray Icon](../../docs/TrayIcon.md).

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
| Windows | `TrayIcon` (notification area, MewUI-drawn menu) | Covered by `TrayIconTests` in the window automation suite: placement, Explorer restart, real left and right clicks on the icon, and the menu navigated and closed from the keyboard and by an outside click |
| macOS | `TrayIcon` (menu bar status item, native menu) | Covered by `TrayIconTests` in the window automation suite: placement, the menu read from the model, and a chosen item running its command; opening the menu with a physical click remains manual |

Use `--smoke` to automate the window lifecycle after native provider registration: show, close to zero windows, show again, close, dispose the provider, and quit.
