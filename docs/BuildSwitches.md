# Build Switches

This document lists the MSBuild properties that turn MewUI features on or off, the runtime switch each one sets, and what each does to a trimmed or NativeAOT publish.

---

## 1. How the switches work

Every switch is an MSBuild property in your app project. The `Aprillz.MewUI.Core` package carries the build logic, so the properties work with any combination of MewUI packages.

```xml
<PropertyGroup>
  <MewUIWin32TextEngine>DirectWrite</MewUIWin32TextEngine>
</PropertyGroup>
```

A property can also be passed on the command line: `dotnet publish -p:MewUIWin32TextEngine=DirectWrite`.

At build time each property is written into `runtimeconfig.json` as an `AppContext` switch, and MewUI reads that switch once at startup. Most switches are also handed to the trimmer, so a trimmed or NativeAOT publish removes the code of the side that was not selected instead of only disabling it.

| Property | Values | Default | Runtime switch | Trimmed away when off |
|---|---|---|---|---|
| `MewUIBackend` | `Direct2D`, `Gdi`, `MewVG` | all backends | none (publish filter) | not applicable |
| `MewUIWin32TextEngine` | `Gdi`, `DirectWrite` | `Gdi` | `Aprillz.MewUI.Win32.DirectWriteText.Enabled` | yes, the path not selected |
| `MewUIManagedFileDialogs` | `true`, `false` | `true` | `Aprillz.MewUI.ManagedFileDialogs.Enabled` | yes |
| `MewUIDevTools` | `true`, `false` | `true` in Debug, `false` in Release | `Aprillz.MewUI.DevTools.Enabled` | yes |
| `MewUIHotReload` | `true`, `false` | `true` | `Aprillz.MewUI.HotReload.Enabled` | no |
| `MewUIEnvironmentDebugLogging` | `true`, `false` | `true` in Debug, `false` in Release | `Aprillz.MewUI.EnvironmentDebugLogging.Enabled` | yes |

Set the MSBuild property rather than the runtime switch. A switch changed by hand in `runtimeconfig.json` or with `AppContext.SetSwitch` does not reach the trimmer, so in a trimmed publish the code it asks for may already be gone.

---

## 2. MewUIBackend

Keeps a single rendering backend when an app that references a metapackage is published. See [Installation & Packages](Installation.md) for the values and examples.

---

## 3. MewUIWin32TextEngine

Selects how the `Gdi` and `MewVG` backends draw text on Windows. The `Direct2D` backend always uses DirectWrite and ignores this property, and Linux and macOS are not affected.

| Value | Font matching | Colour glyphs |
|---|---|---|
| `Gdi` (default) | legacy family names | no |
| `DirectWrite` | typographic family names, so every weight of a family is reachable | yes, colour emoji |

Any other value fails the build with an error.

A NativeAOT publish with `DirectWrite` is about 70 to 85 KB larger than the same app with `Gdi`. An app that draws no text is the same size either way.

---

## 4. MewUIManagedFileDialogs

MewUI ships managed file and folder dialogs that are used when `PreferNative` is `false` or when the native dialog is unavailable. Setting the property to `false` removes them from a trimmed or NativeAOT publish for an app that relies on native dialogs only.

With the managed dialogs off, a failure of the native dialog is thrown to the caller instead of falling back, and a request with `PreferNative = false` throws `NotSupportedException`.

---

## 5. MewUIDevTools

Turns on the element inspector, the visual tree window, the frame statistics overlay and the profiler. See [DevTools](DevTools.md).

The tools are never part of a trimmed or NativeAOT publish: the property panel lists members by reflection, which trimming breaks. Publishing such an app with `MewUIDevTools` set to `true` publishes without the tools and reports a build warning. The same app still runs the tools under `dotnet run`.

---

## 6. MewUIHotReload

Hot Reload needs no declaration and is active under `dotnet watch` or an IDE Hot Reload session. Set the property to `false` to opt out. See [Hot Reload](HotReload.md).

---

## 7. MewUIEnvironmentDebugLogging

Enables diagnostic logs that are switched on with environment variables at run time. In Release the logging code is left out unless the property is set to `true`.

Set a variable to `1` or `true` before starting the app. The lines go to standard output and to the debugger output.

| Variable | Logs |
|---|---|
| `MEWUI_GRAPHICS_DEBUG` | graphics context state |
| `MEWUI_IME_DEBUG` | input method traffic on every platform |
| `MEWUI_X11_DEBUG` | X11 errors |
| `MEWUI_XI2_DEBUG` | XInput2 events |
| `MEWUI_ICON_DEBUG` | shell icon lookup on Linux |
| `MEWUI_MACOS_DEBUG` | macOS window class registration |
| `MEWUI_INTEROP_DEBUG` | macOS native interop |
