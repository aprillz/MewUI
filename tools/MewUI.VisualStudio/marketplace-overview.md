# MewUI Preview

Live preview for [MewUI](https://mewui.aprillz.net) windows and user controls, rendered in a Visual Studio tool window while you edit plain C#.

## How it works

Open **View > Other Windows > MewUI Preview** and press Start. The extension launches your app as a preview session: the real entry point, themes, and fonts run headless, and frames stream into the tool window. From then on every save updates the preview through .NET Hot Reload, typically in under a second, without restarting the process.

## Features

- Previews any `Window` or `UserControl` with a parameterless constructor (`internal` types included), plus the app's main window as-is.
- Opening a file previews the component declared in it; picking a target jumps to its declaration.
- `.DesignSize(w, h)` / `.DesignWidth(w)` / `.DesignHeight(h)` control the preview size, and `Design.IsPreviewMode` guards side effects.
- Interactive: mouse, wheel, keyboard, and typed text reach the running app.
- Target picker, light/dark toggle, zoom (Fit to 200%), refresh, and session restart.
- Renders at the monitor's DPI, so the image stays crisp at any scale.
- No preview code ships in your app: the preview assembly is referenced only during sessions.

## Requirements

- Visual Studio 2022 or 2026 (x64 or Arm64).
- .NET SDK 8.0 or later (10.0 recommended) on PATH.
- A project referencing Aprillz.MewUI 0.20.0 or later.
