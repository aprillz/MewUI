# Render Loop Concept

This document describes MewUI's render-loop model: how rendering is scheduled relative to the
message loop and layout, how continuous/on-request rendering is controlled, and how each
backend maps that control onto its present/vsync mechanism. It is an internal guide for
backend/platform behavior and scheduling.

---

## 1. Goals

- Keep the UI responsive by decoupling rendering from message/layout processing.
- Support both:
  - **On-request rendering** (the default): a window renders only when something invalidates it.
  - **Continuous rendering**: the loop renders every window every iteration, for animations and
    max-FPS scenarios.
- Give backend-level vsync control with consistent behavior across the Win32, X11, and macOS
  platform hosts and the Direct2D, MewVG (OpenGL/Metal), and GDI graphics backends.

---

## 2. RenderLoopSettings

The loop is configured via `Application.Current.RenderLoopSettings` (`Aprillz.MewUI.RenderLoopSettings`):

- `Continuous` (`bool`) - user flag that forces continuous rendering. This is the only direct way
  to request continuous rendering; the animation system requests it on its own (see below) while
  clocks are running. `SetContinuous(bool)` is a convenience setter for the same flag.
- `TargetFps` (`int`) - frame cap for continuous rendering. `0` (the default) means uncapped.
- `VSyncEnabled` (`bool`) - backend present/swap behavior. Defaults to `true`. Turning it off also
  forces continuous rendering (see `IsContinuous` below), since there is no vsync to pace on.
- `IsContinuous` (`bool`, read-only) - `true` when the loop should run continuously:

  ```
  IsContinuous = !VSyncEnabled || Continuous || AnimationActive
  ```

  This is the single value the platform host reads every loop iteration; nothing sets the loop
  mode directly.
- `AnimationActive` (internal) - driven by `AnimationManager`: set while one or more animation
  clocks are active, cleared when idle. Not a user-facing knob.

There is no separate "mode" enum - on-request vs. continuous is entirely the computed
`IsContinuous` value. By default (`Continuous = false`, `VSyncEnabled = true`, no active
animations) `IsContinuous` is `false`, i.e. on-request rendering.

A real example, from the Gallery sample's "Max FPS" toggle:

```csharp
var scheduler = Application.Current.RenderLoopSettings;
scheduler.TargetFps = 0;
scheduler.VSyncEnabled = !maxFpsEnabled;
scheduler.SetContinuous(maxFpsEnabled);
```

---

## 3. On-request vs. continuous scheduling

Both the Win32 and X11 platform hosts (`Win32PlatformHost`, `X11PlatformHost`) run a single
message/render loop (`PumpLoop`) per process, shared by every registered window - there is no
per-window OS loop. Each iteration branches on `RenderLoopSettings.IsContinuous`:

### 3.1 On-request (`IsContinuous == false`)

- The loop blocks (`MsgWaitForMultipleObjectsEx` on Win32, the platform equivalent on X11) until
  either an OS message arrives or a window has requested a render.
- When woken, only windows whose backend has `NeedsRender == true` are rendered
  (`RenderIfNeeded`), then pending OS messages are drained.
- If a window still needs a render afterward (e.g. an invalidation happened while draining
  messages), the render-request event is re-armed for the next iteration.
- Multiple invalidations before the next wake-up coalesce into a single render, similar to
  WPF-style render-request merging.

### 3.2 Continuous (`IsContinuous == true`)

- Every iteration processes pending messages, then renders **every** window unconditionally
  (`RenderNow`), regardless of whether it was invalidated.
- If `TargetFps > 0`, the loop sleeps/waits out the remainder of the frame budget after
  rendering; `TargetFps == 0` means it renders as fast as it can loop.
- This is what animations rely on (see `AnimationActive` above) and what "Max FPS" profiling
  toggles use.

The macOS platform host (`MacOSPlatformHost`) implements the same `NeedsRender` /
`RenderIfNeeded` / `RenderNow` / `IsContinuous` / `TargetFps` contract.

---

## 4. Message loop, update pass, and render

A window's invalidation and rendering funnel through the UI dispatcher's priority queue
(`DispatcherPriority`: `Idle` < `Background` < `Render` < `Layout` < `Input` < `Normal`, higher
runs first). Each dispatcher drain (`DispatcherQueue.Process`) processes queues from highest
priority to lowest, so anything a `Layout`-priority action enqueues at `Render` priority is still
drained within the same pass:

- `UIElement.InvalidateVisualState()` queues the element on its window
  (`Window.RegisterVisualStateDirty`) for reconciliation.
- `Window.InvalidateMeasure()` / `InvalidateArrange()` call `RequestUpdatePass()`, which posts a
  merged action at `DispatcherPriority.Layout`. That action runs `Window.PerformLayout()` - which
  reconciles queued visual-state invalidations (`UpdateVisualStates()`), resolves the window's own
  style, applies the template, then measures/arranges - and finally calls `RequestRender()`.
- `RequestRender()` (internal) posts a merged action at `DispatcherPriority.Render` that calls
  `_backend.Invalidate(true)`, which sets the backend's `NeedsRender` flag and asks the platform
  host to wake the render loop.
- `Window.Invalidate()` and `Window.InvalidateVisual()` (public) both call `RequestRender()`
  directly, skipping the layout stage - use these when layout is already known to be current.

So the effective per-frame order is: **OS/dispatcher messages -> update pass (visual-state
reconciliation, then measure/arrange, only if something is dirty) -> platform host render call**.
Inside the actual render (`Window.RenderFrame` -> `RenderFrameCore`), `AnimationManager.Instance
.Update()` runs first to advance active animation clocks before the draw traversal, so animated
values are current for that frame's paint.

### Win32 specifics

- `WM_PAINT` is still handled (`Win32WindowBackend.HandlePaint`) and renders immediately inside
  `BeginPaint`/`EndPaint` (via `UpdateLayeredWindow` instead for per-pixel-alpha windows) - the
  platform host does not force `WM_PAINT` for its own request-driven renders, and calls
  `ValidateRect` after presenting so it doesn't generate a redundant repaint.
- OS-driven modal loops (interactive resize/move, or a native menu - `WM_ENTERSIZEMOVE` /
  `WM_ENTERMENULOOP`) run inside `DefWindowProc` and suspend `PumpLoop`. To keep animations and
  pending invalidations progressing during that time, the backend starts a `WM_TIMER` (8 ms) on
  entry and stops it on `WM_EXITSIZEMOVE` / `WM_EXITMENULOOP`.

---

## 5. Backend vsync behavior

| Backend | VSyncEnabled = true | VSyncEnabled = false |
|---|---|---|
| Direct2D (swap-chain present) | DXGI `Present` sync interval `1` | sync interval `0` |
| Direct2D (HwndRenderTarget present) | `D2D1_PRESENT_OPTIONS.NONE` | `D2D1_PRESENT_OPTIONS.IMMEDIATELY` |
| MewVG / OpenGL (Win32 WGL, X11 GLX/EGL) | swap interval `1` | swap interval `0` |
| MewVG / Metal (macOS) | `CAMetalLayer.displaySyncEnabled = true` | `displaySyncEnabled = false` |
| GDI | no effect - GDI has no vsync concept | no effect |

GDI still fully participates in on-request/continuous scheduling (that mechanism lives in the
platform host, not the graphics backend) - `VSyncEnabled` just has nothing to control there.

---

## 6. FPS and frame diagnostics

- `Window.FrameRendered` (`event Action?`) fires after every rendered frame, in both scheduling
  modes. `Window.FirstFrameRendered` fires once, the first time a frame is rendered.
- `Window.LastFrameStats` (`RenderStats`) holds the previous frame's `DrawCalls`, `CullCount`,
  `RenderedCalls`, and `CullRatio`.
- The Sample and Gallery apps use `FrameRendered` to accumulate frames over a one-second window
  and compute/display an FPS readout, and read `LastFrameStats` to show draw/cull counts.

---

## 7. Design notes

- Rendering is kept separate from invalidation so continuous/max-FPS mode is just "always treat
  every window as needing a render," without a different code path.
- On-request renders are coalesced (merged dispatcher posts, `NeedsRender` flag) so redundant
  invalidations collapse into one render per wake-up.
- Continuous mode renders even without invalidation so animations - and anything else driving
  `AnimationActive` - can advance every frame.
