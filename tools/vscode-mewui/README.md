# MewUI Preview

Live preview for [Aprillz.MewUI](https://github.com/aprillz/MewUI) windows and user controls, rendered inside VS Code while you edit plain C#.

## How it works

Run **MewUI: Start Preview** from a C# file. The extension launches your app as a preview session (`dotnet watch` under the hood): the app's real entry point, themes, and fonts run headless, and frames stream into an editor panel. From then on every save updates the preview, typically in under a second, without restarting the process.

## Features

- Previews any `Window`/`UserControl` with a parameterless constructor (`internal` types included), plus the app's main window as-is.
- Opening a file automatically previews the component declared in it.
- `.DesignSize(w, h)` / `.DesignWidth(w)` / `.DesignHeight(h)` hints control the preview size; `Design.IsPreviewMode` guards side effects.
- Interactive: mouse, wheel, keyboard, and text typed on the preview canvas reach the running app.
- Toolbar: target picker, light/dark theme toggle, zoom (Fit-200%), refresh, session restart.
- Renders at the editor's DPI for pixel-crisp output on any monitor scale.
- Closing the panel keeps the session warm (configurable), so reopening is instant.
- No preview code ships in your app: the preview assembly is referenced only during sessions.

## Requirements

- .NET SDK 8.0 or later (10.0 recommended) on PATH.
- A project referencing Aprillz.MewUI 0.20.0 or later (package reference or repo project reference).
- A trusted workspace (the preview builds and runs your project code).

## Commands

| Command | Description |
|---|---|
| `MewUI: Start Preview` | Start a preview session for the active file's project |
| `MewUI: Stop Preview` | Stop the session and close the panel |
| `MewUI: Refresh Preview Target` | Rebuild the current target (process kept) |
| `MewUI: Restart Preview Session` | Full process restart (state reset) |

## Settings

| Setting | Default | Description |
|---|---|---|
| `mewui.preview.reloadDriver` | `auto` | `watch` (hot reload), `buildRestart` (restart on save), or `auto` fallback |
| `mewui.preview.sessionStartTimeoutSeconds` | `60` | Wait before retrying with a shim session that skips the app entry point; `0` disables |
| `mewui.preview.autoSelectTarget` | `true` | Follow the active editor file |
| `mewui.preview.keepSessionMinutes` | `10` | Keep the session running after the panel closes; `0` stops it immediately |
