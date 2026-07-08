## What's Changed

v0.18.0 is a "window, dialog, and platform plumbing" release. The dialog story grows up: synchronous `MessageBox` APIs, reliable nested modal dialogs, borderless / fullscreen windows, and a hidden `CursorType.None` mode across every platform. The X11 backend gains an EGL OpenGL path alongside the existing GLX one. The backend / platform registration API is simplified. On the content side, the `Image` control honors EXIF orientation and pools its vector image cache, and the blur / drop-shadow filter graph standardizes on `radius` instead of `sigma`.

### Added

#### Windows / dialogs
* **Synchronous `MessageBox` dialog methods** — `Notify`, `Confirm`, `AskYesNo`, `AskYesNoCancel`, and `Prompt` return the user's answer directly. Configured through `MessageBoxOptions`.
* **Nested modal dialog support** — `Window` gains a synchronous `ShowDialog` alongside the existing `ShowDialogAsync`, and a modal can safely spawn another modal on every platform.
* **Borderless and fullscreen windows** — `Window.Borderless` and `WindowState.FullScreen`, plus matching `Borderless` / `FullScreen` fluent methods. Both states survive window-state transitions.
* **`CursorType.None` (hidden cursor)** — `null` cursor follows the parent / default, and `None` hides the cursor. Behaves the same on every platform.

#### Controls
* **EXIF orientation in the `Image` control** — JPEG EXIF orientation tags are honored during measure and render, so portrait photos no longer display sideways. Behavior is configurable via the `ImageOrientation` and `ImageOrientationMode` enums.

#### Backend / platform
* **EGL OpenGL on Linux/X11** — an EGL-based GL path is available alongside the existing GLX path.
* **`DpiHelper.GetDpiForPoint`** — looks up the monitor DPI for a given screen point.

### Improved

#### Rendering
* **`Image` vector cache rewritten with surface pooling** — vector image sources use a per-control bitmap cache and borrow from a size-keyed offscreen surface pool. Caches only the visible painted region, returns surfaces to the pool on detach and takes them back on reattach, evicts oldest-first, and disposes pooled surfaces with the window's graphics teardown.
* **Blur filters standardize on `radius` (DIPs)** — `BlurFilter` and `DropShadowFilter` take radius, and `BlurKernel` helps convert from sigma.

#### Controls / layout
* **`Easing.Default` changed from `EaseOutCubic` to `EaseInOutBack`** — affects `TransitionContentControl` and any other animation that doesn't override its easing.
* **`Thickness.ToString` simplified for uniform values** — emits a single value (e.g. `"4"`) when all four sides are equal.

#### Backend / platform
* **Linux/X11 cursor handling stabilized** — cursors are shared per display, eliminating latent double-free and redundant-recreation bugs.
* **Linux/X11 owner windows ignore pointer input while a modal dialog is up** — owner windows behind a modal no longer receive pointer events, matching the other platforms.

### Fixed

* **Direct2D and GDI+ image drawing didn't honor the context-level global alpha** — opacity set at the context now flows through image draws, and fully-transparent draws skip work entirely.
* **`ItemsControl.ItemPadding` changes at runtime were not propagated to the presenter** — layout and visual now refresh immediately.
* **`TransitionContentControl` snapped the outgoing layer to black when a crossfade was interrupted** — the outgoing alpha is preserved so the layer fades out cleanly.
* **Windows GDI font family resolution falling back to a different face** — some installs rendered the wrong font face for the requested family.
* **`DispatcherTimer` double-scheduling when a Tick re-arms** — re-arming inside the handler no longer queues two future fires.
* **JPEG progressive scans lost coefficient data** — some progressive JPEGs previously rendered as solid gray.

### ⚠️ Breaking Changes

#### Backend / platform
* **`Application` class API trimmed** — unused id-based backend / platform-host registration / selection along with `SetDefaultGraphicsFactory` / `SetDefaultPlatformHost` are cleaned up.
* **`PlatformHost`-related APIs moved to internal** — surfaces such as DPI lookup and default font access that didn't need to be public are now cleaned up. Use `DpiHelper` for DPI and `Theme.Metrics` for fonts.

#### Conventions
* **`PlatformKeyConfiguration` renamed to `PlatformConventions`** — moved into `Aprillz.MewUI.Platform`, expanded with `ReverseButtonOrder` for dialog button layout.
* **`MacOSKeyConfiguration` replaced by `MacOSConventions`** — gesture formatting, key symbol logic, and other macOS conventions consolidate here.

#### Filters
* **Blur filters take `radius` instead of `sigma`** — `BlurFilter` and `DropShadowFilter` change shape; existing callers must convert. `BlurKernel` provides the helper.

---

**Full Changelog**: https://github.com/aprillz/MewUI/compare/v0.17.0...v0.18.0
