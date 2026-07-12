# Layout (Measure / Arrange / Render) & DPI Pixel Snapping

This document describes the layout/rendering contract used by MewUI, and the rules to keep layout stable across DPIs while avoiding 1px clipping artifacts. It targets developers writing custom controls or panels against `Element` / `FrameworkElement` / `UIElement`.

## Terms

- **DIP**: Device-independent pixels (logical units). Most layout coordinates/sizes are expressed in DIP.
- **Px**: Device pixels (physical pixels). `px = dip * dpiScale`.
- **dpiScale**: `Dpi / 96.0` (`Window.DpiScale`).
- **Constraint**: The `Size availableSize` passed into `Measure(...)`.
- **DesiredSize**: The element's preferred size after `Measure`, in DIP.
- **Bounds**: The element's arranged rectangle after `Arrange`, in DIP.

### Coordinate space: Bounds is window-absolute, not parent-relative

`Element.Bounds` is expressed in **window-absolute coordinates**, not the parent's local coordinate space. Panels arrange children by offsetting from their own absolute `Bounds` (e.g. `child.Arrange(new Rect(Bounds.X + offsetX, Bounds.Y + offsetY, ...))`); they do not reset to `(0, 0)` for each child. Element transforms are translate-only, so this stays consistent through the tree. Custom panels must pass window-absolute rects to `Arrange`.

If you want a parent-relative accessor, use `RenderSize` (just the `Size`, no origin) or convert between two elements' coordinate spaces with `TranslatePoint` / `TranslateRect` / `TransformToAncestor` / `TransformToDescendant`.

## Pipeline overview

MewUI renders in immediate mode (every frame is repainted), but keeps the layout/control tree retained for hit-testing and for reusing Measure/Arrange results between frames.

The pass order is:

1) **Measure**: computes `DesiredSize`, recursing top-down (a parent's `MeasureOverride`/`MeasureContent` calls `Measure` on its children before returning its own size).
2) **Arrange**: assigns `Bounds`, recursing top-down the same way.
3) **Render**: draws using `Bounds` and current visual state (`OnRender` for the element's own visuals, then `RenderSubtree` for children).

### When each pass runs

- `InvalidateMeasure()`: marks `IsMeasureDirty` **and** `IsArrangeDirty` true, then propagates to `Parent.InvalidateMeasure()` unconditionally (even if this element was already dirty - a stale ancestor flag must still be re-notified, and this is also what wakes the `Window`). It then calls `InvalidateVisual()`.
- `InvalidateArrange()`: marks only `IsArrangeDirty` true and propagates to `Parent.InvalidateArrange()` the same way. Does **not** imply Measure is invalid.
- `InvalidateVisual()`: just propagates up to `Parent.InvalidateVisual()`. On `Window`, this schedules a repaint only (`RequestRender()`) - it does **not** schedule a layout pass.
- `InvalidateVisualState()` (on `UIElement`/`Control`): queues the element for visual-state reconciliation (style triggers, state transitions) and calls `Window.RequestUpdatePass()`. This is distinct from the two above: a property that only `AffectsVisualState` (not layout or render) still needs the *update pass* to run, because trigger-resolved values may feed into layout or render that hasn't happened yet.

Both `Window.InvalidateMeasure()` and `Window.InvalidateArrange()` additionally call `RequestUpdatePass()`.

**Rule of thumb**:
- **Scrolling** should be *Arrange + Render only* - see [Scrolling](#scrolling-layout-expectations).
- **Content/size-affecting property changes** require **Measure**.

### The update pass (`Window.PerformLayout`)

`RequestUpdatePass()` posts a merged, single callback to the dispatcher at `DispatcherPriority.Layout` (repeated calls within the same tick coalesce into one `PerformLayout()` call, followed by a render request). `PerformLayout()` does, in order:

1) `UpdateVisualStates()`: resolves queued visual-state changes (style triggers/animations) before layout reads state-dependent values (e.g. a trigger changing Padding).
2) Resolve the window's own style and apply its template (the `Window` itself bypasses `MeasureOverride`, so template application happens here).
3) For `FitContent*` window-sizing modes, measure content against the max constraints first and resize the window to match.
4) Skip check: if the client size, padding, and content reference are unchanged, and no element in the tree has `IsMeasureDirty`/`IsArrangeDirty` set (a full tree walk, since a container may clear its own dirty flag while a virtualized descendant stays dirty), and no popup/adorner/overlay layout is dirty, the pass returns without re-running Measure/Arrange.
5) Otherwise, Measure and Arrange run in a loop capped at 8 passes, stopping early once nothing is left dirty. This lets a re-arrange triggered from inside the same pass (e.g. `ScrollIntoView`) settle without a full extra frame - see [Anti-patterns](#anti-patterns-cause-performance-problems-or-layout-thrashing) for what happens if it never settles.
6) Adorners, popups, and the overlay layer are laid out last (they hang off `Window.Parent` but are not part of the `Content` tree, so they need their own dirty check).

### Custom composited hosts

A custom control that composes private child elements outside its own template (e.g. an internal presenter, similar to what `ScrollViewer`/virtualizing item presenters do) should implement the marker interface `ISubtreeInvalidationHost` (in addition to `IVisualTreeHost`). When present, `InvalidateMeasure()`/`InvalidateArrange()` cascade the dirty flag into the private visual subtree unconditionally, so those children are not silently skipped by `Measure`'s same-constraint short-circuit.

## Measure

### Purpose

Measure calculates how large an element *wants* to be under a given constraint.

### Inputs / outputs

- Input: `availableSize` in **DIP**.
- Output: `DesiredSize` in **DIP** - clamped to `availableSize` on each axis that is finite (an infinite constraint axis passes the measured value through unclamped), then pixel-rounded (see below).

### Rules

- Measure must be **pure** with respect to layout state:
  - Avoid unconditionally setting layout-affecting properties inside `MeasureOverride`/`MeasureContent`. If you do it during Measure and it dirties the element again, the re-dirty happens *before* `Measure()` unconditionally clears `IsMeasureDirty` at the end of the call - so the new dirty flag is silently discarded, not looped forever. The practical failure mode is a missed re-measure, not an infinite loop.
  - It should not depend on previously arranged `Bounds`.
- Skip condition (exact): a call to `Measure(availableSize)` returns the cached `DesiredSize` without invoking `MeasureCore`/`MeasureOverride` when **all** of: the element is not measure-dirty, a previous constraint was recorded, and it equals `availableSize`. New elements default to measure-dirty, so the first call always measures.
- **Templated controls**: when a `Control` has an applied template, `MeasureContent`/`ArrangeContent` forward to the template's visual root (`root.Measure(availableSize)` / return `root.DesiredSize`) instead of running the control's own content-measure logic. Control authors overriding `MeasureContent` only need to worry about the untemplated (code-only) path.

### DPI rounding in Measure

`DesiredSize` is automatically pixel-rounded by the framework: `Element.Measure` rounds the clamped size via a size-only rounding helper, driven by `Window.DpiScale`, whenever `Window.UseLayoutRounding` is true (the default). Control/panel authors do not need to round their own `MeasureOverride`/`MeasureContent` return value.

What *is* the author's responsibility is rounding intermediate values computed for children, so the numbers a Measure pass hands to a child's constraint match what Arrange later computes for the same child. `ScrollViewer.MeasureContent` does this for its viewport size before measuring content, using the same `dpiScale` it will reuse in `ArrangeContent` - without that, the viewport computed in Measure can differ from the one computed in Arrange by a device pixel at fractional DPI, causing content clipping.

## Arrange

### Purpose

Arrange assigns the final position/size (`Bounds`, in window-absolute coordinates) for each element and places its children.

### Inputs / outputs

- Input: `finalRect`, window-absolute, in **DIP**.
- Output: `Bounds`, window-absolute, in **DIP**.

### Rules

- Skip condition (exact): `Arrange(finalRect)` recomputes the element's arranged rect from `finalRect` (via `GetArrangedBounds`, which resolves `Margin`/alignment/`Min`/`Max` on `FrameworkElement`) and rounds it; if the element is not arrange-dirty **and** the result equals the current `Bounds`, `ArrangeCore` is skipped. Note this comparison always recomputes the candidate rect first, so `GetArrangedBounds` runs on every call regardless of the dirty flag.
- Arrange is responsible for placing children (`ArrangeContent`/`ArrangeCore` on panels/content hosts).

### DPI rounding in Arrange (pixel snapping)

`Bounds` is automatically pixel-rounded by the framework, the same way `DesiredSize` is: `Element.Arrange` rounds the arranged rect using `Window.DpiScale` whenever `UseLayoutRounding` is true. Importantly, this rounding treats **position and size independently** (round `x`, `y`, `width`, `height` each on their own), not by rounding the left/right edges and deriving width from the difference - the latter would shrink or grow the size by up to 1px depending on where the edges land, which shows up as jitter as an element moves across the tree. Custom `Arrange`/`ArrangeCore` overrides do not need to re-round their own `Bounds`; the framework already did it.

What authors *do* need to snap by hand is any additional rect they compute for painting or clipping - the framework provides distinct helpers on `LayoutRounding` for distinct intents, because "round the edges" and "never let it shrink" are different requirements:

| Helper | Rounding | Use for |
|---|---|---|
| `SnapBoundsRectToPixels(rect, dpiScale)` | Rounds each edge independently (may shrink/grow by up to 1px) | Border/background paint geometry, e.g. `FrameworkElement.GetSnappedBorderBounds` |
| `SnapConstraintRectToPixels(rect, dpiScale)` | Same algorithm as above | Measure-time constraint rects (see `ScrollViewer.MeasureContent`) |
| `SnapViewportRectToPixels(rect, dpiScale)` | Floors left/top, ceils right/bottom (never shrinks) | Scroll viewports and clip rects, e.g. `ScrollViewer.GetContentViewportBounds` |
| `MakeClipRect(rect, dpiScale, rightPx = 0, bottomPx = 0)` | Outward snap, optionally expanded by whole device pixels on right/bottom | Render-time clip rects; used with the defaults (pure outward snap) by `TextBase`, `ContextMenu`, `GridView` |
| `SnapThicknessToPixels(thicknessDip, dpiScale, minPixels)` | Rounds to an integer pixel count with a floor | Border/stroke thickness that must stay visible at fractional DPI |
| `RoundSizeToPixels` / `RoundRectToPixels` | Position and size rounded independently | What the framework uses internally for `DesiredSize`/`Bounds` |

## Render

### Purpose

Render draws the element using `Bounds` and current visual state.

### Rules

- Render must not perform layout: `UIElement.Render` is `sealed` and never calls `Measure`/`Arrange`. Avoid triggering `InvalidateMeasure()`/`InvalidateArrange()` from inside `OnRender` - it won't corrupt the current frame, but it schedules another update pass right after this one finishes, which can turn into continuous re-layout every frame.
- Render should draw using already-snapped geometry whenever possible (see the Arrange rounding table above).
- Elements outside the window's client viewport are culled: `Render` releases any bitmap cache and returns without calling `OnRender` when the element's `Bounds` don't intersect `new Rect(root.ClientSize)`. Set `SkipViewportCull` (an inherited property) on subtrees rendered under a parent-applied transform, since their `Bounds` don't reflect their actual visible area and would otherwise get culled incorrectly.

### Clipping rules (1px artifacts)

Text, strokes, and antialiasing can extend half a pixel outside logical bounds. If an ancestor clips exactly at the child's bounds, that overhang gets cut and looks like a missing right/bottom pixel.

The pattern used in practice (see `ScrollViewer.GetContentClipBounds`):

1) Compute the viewport/content rect in DIP.
2) Snap it outward (`SnapViewportRectToPixels`, never shrinks).
3) If overhang is expected (e.g. a child's border stroke can sit exactly on the viewport edge), expand the rect by up to 1 device pixel into whatever unused padding/room is available on that side, then snap outward again via `MakeClipRect`. Bound the expansion by the actual room available (`Math.Min(onePx, room)`) - expanding past the outer chrome bounds, or into negative coordinates, can shift the clip and eat a different pixel instead.
4) Apply the resulting rect as the clip.

`MakeClipRect`'s own `rightPx`/`bottomPx` parameters can also perform the expansion directly, but the codebase's current callers all pass the defaults (`0, 0`, i.e. a pure outward snap) and do any expansion by pre-inflating the rect as in step 3.

## DPI propagation and caching

### DPI source of truth

- `Window.Dpi` (default 96) and `Window.DpiScale => Dpi / 96.0` are the source of truth.
- Elements resolve their effective DPI by walking up to the owning `Window` (`GetDpiCached`/`FrameworkElement.GetDpi()`), caching the result per-context-version so repeated calls in a render loop are O(1); the cache only re-walks the chain after the element's parent chain has actually changed. When detached from any `Window`, it falls back to the OS system DPI.
- When the OS reports a DPI change, `Window.RaiseDpiChanged` clears the cached DPI for the whole visual tree, then calls `NotifyDpiChanged` (which calls `OnDpiChanged` then `InvalidateMeasure()` + `InvalidateVisual()`) on every attached `FrameworkElement`, plus popups, adorners, and the overlay layer.

### Caching

`TextMeasureCache`'s cache key is `(text, family, size, weight, wrapping, maxWidthDip, dpi)`: any change to text, font, wrapping, the wrap constraint, or DPI naturally invalidates the cache by producing a different key - there is no separate manual invalidation to remember. Separately, `TextBlock.OnDpiChanged` disposes its native `IFont` so glyphs are rebuilt at the new DPI (a resource-lifecycle concern, not the measure cache).

## Scrolling: Layout expectations

### Offsets

`ScrollViewer.HorizontalOffset`/`VerticalOffset` setters call `InvalidateArrange()` only, never `InvalidateMeasure()`: pure offset changes stay Arrange+Render only. Extent/viewport (which do require Measure) are only recomputed when content or available size actually changes.

`ScrollViewer.MeasureContent` deliberately does **not** mutate `_scroll` state (metrics, offset) or the scrollbars' `IsVisible`/`ViewportSize`/`Max`: Measure can run with a hypothetical/unconstrained size (e.g. a popup owner probing natural size every frame), and mutating shared scroll state there would corrupt the displayed scrollbar or reset the user's scroll offset. All of that mutation happens in `ArrangeContent`, where the viewport reflects the size actually being displayed.

### What updates during scroll

1) `ArrangeContent` arranges the child at `viewport - offset` (for plain content), or calls `IScrollContent.SetViewport`/`SetOffset` and arranges the child at the viewport rect unchanged (for virtualizing/scroll-aware content, which positions itself internally from the given offset rather than being translated by `Arrange`).
2) Content renders under a viewport clip (see Clipping rules above).
3) Scrollbar ranges/values are synced to the current offset/viewport (`SyncBars`).

## Anti-patterns (cause performance problems or layout thrashing)

- Setting layout-affecting properties during Measure/Arrange without comparing old/new values first: at best a wasted re-measure is discarded (see [Measure rules](#rules)); at worst the property never stabilizes and the element re-dirties itself every frame, one full update pass at a time.
- Triggering `InvalidateMeasure()`/`InvalidateArrange()` from `OnRender`: safe for the current frame (Render can't be interrupted), but schedules a fresh update pass immediately after, which repeats every frame if done unconditionally.
- Re-implementing DPI resolution by walking `Parent` in a hot path instead of calling the cached `GetDpi()`/`GetDpiCached()`.
- Calling `InvalidateMeasure()` on every scroll tick when only the offset changed - use offset-only mutation (`InvalidateArrange()`) as `ScrollViewer` does.
