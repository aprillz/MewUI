# Window Visual Layers

`Window` renders its normal content together with window-managed visual layers. These layers are
intended for different ownership and placement models:

- `AdornerLayer` positions visuals relative to a particular element.
- `OverlayLayer` positions visuals relative to the whole window.
- Popups are transient visuals managed by popup-owning controls and normally use a separate native
  surface.

`OverlayLayer` and `OverlayWindow` are different concepts. `OverlayLayer` is a public layer inside a
normal window. The framework-internal `OverlayWindow` is a click-through native window used for
cross-window features such as the drag preview.

## Surface and stacking model

The hierarchy below separates the owner surface from the native surfaces used by the default popup
host. Items under `render stack` are listed from front to back:

```text
Window (owner and visual root)
├── Owner OS surface
│   └── Render stack (front → back)
│       ├── OverlayLayer
│       ├── In-surface popup host (fallback and headless only)
│       ├── AdornerLayer
│       └── Window content / template
└── Owned native surfaces (default popup host)
    └── PopupWindow (one surface per popup)
        └── Portal rendering of PopupChrome and popup content
            └── Parent and visual root remain in the owner Window
```

Rendering walks this list in the opposite direction, from content to overlay. Hit testing starts at
the front: overlay, in-surface popup, adorner, then normal content. Within each layer, later-added
elements render above earlier elements and are hit-tested first.

Native popups are not part of this stack. The default popup host creates a non-activating
`PopupWindow` with its own OS surface so dropdowns, menus, and tooltips can extend outside the owner
window. Consequently, an owner-surface overlay cannot cover a native popup merely by being later in
the owner window's render order.

Debug-only diagnostics may render after the public layers and are not part of the application layer
contract.

## Layer comparison

| Layer | Placement and surface | Input | Typical uses |
| --- | --- | --- | --- |
| Window content | Arranged inside the window padding on the owner surface | Normal visual-tree routing | Application content and control templates |
| `AdornerLayer` | Arranged to the adorned element's bounds on the owner surface | Participates in hit testing; later adorners are tested first | Selection handles, validation marks, resize grips |
| Popup | Placed from owner-client DIPs and normally drawn in a separate native surface | Popup input bubbles back to its owning control; transient popups support light-dismiss | Dropdowns, context menus, submenus, tooltips |
| `OverlayLayer` | Every element is measured and arranged to the full client area on the owner surface | Participates in hit testing; set `IsHitTestVisible = false` for decorative overlays | Busy masks, toasts, full-window progress |

## OverlayLayer

Every `Window` exposes one `OverlayLayer`:

```csharp
var overlay = new ProgressIndicator
{
    IsActive = true,
    IsHitTestVisible = false,
};

window.OverlayLayer.Add(overlay);

// Later:
window.OverlayLayer.Remove(overlay);
```

Adding or removing an overlay requests layout and rendering. The layer sets the element's visual
parent to the window, so the overlay receives the window's visual root, theme, DPI, and inherited
values.

Each overlay receives the full client rectangle. The overlay is responsible for positioning its own
children inside that rectangle. A hit-test-visible full-window overlay is also an input barrier for
the content below it. Decorative overlays should therefore disable hit testing explicitly.

`OverlayLayer` can also hold an `IOverlayService`. A service owns its presenter elements and can be
retrieved through `GetService` or `GetOrCreateService`. Toast and busy-indicator support use this
model.

`Remove` detaches an overlay but does not dispose it. After removal, the caller remains responsible
for any required disposal. When the window itself is disposed, registered services and overlays
still in the layer are disposed by the window.

## AdornerLayer

An adorner is associated with an `AdornedElement` but is parented directly to the window so it can
render above ordinary content without being clipped by the adorned element's subtree.

```csharp
var adorner = new Adorner(target, adornerContent);
var layer = AdornerLayer.GetAdornerLayer(target);

layer?.Add(adorner);

// Later:
layer?.Remove(adorner);
```

`GetAdornerLayer` returns `null` until the target is attached to a `Window`. Add the adorner through
the layer belonging to the same window as the target.

During window layout, the adorner is measured and arranged to the adorned element's bounds in window
coordinates. Adorning the `Window` itself uses the full client rectangle. If either the adorned
element or the adorner is hidden, that layout pass skips the adorner.

The layer renders adorners in insertion order and hit-tests them in reverse insertion order. Use
`IsHitTestVisible = false` when an adorner is visual-only and should not intercept the adorned
element's input.

`Remove` detaches the adorner without disposing it. Adorners that remain attached are disposed when
the window's visual tree is disposed.

## Popups

There is no public `Window.PopupLayer`. Application controls normally use `ComboBox`, `ContextMenu`,
`ToolTip`, or another popup-owning control. Custom dropdown-style controls can derive from
`PopupOwnerBase`, provide popup content through `CreatePopupContent`, and override placement when
needed.

`PopupManager` is the framework-internal policy layer. It tracks open popups, their owners,
placement, focus restoration, light-dismiss, and close notification. Transient popups close on
relevant outside pointer presses, focus changes, scrolling, explicit requests, window deactivation,
or window teardown unless their internal `staysOpen` policy says otherwise.

### Native popup portal

The normal runtime host wraps popup content in `PopupChrome` and draws it in a non-activating native
`PopupWindow`. The popup subtree nevertheless remains rooted in the owner `Window`:

- theme, DPI, styles, inherited properties, and `FindVisualRoot()` continue to use the owner window;
- `ContextParentOverride` resolves inherited context through the popup-owning element;
- rendering and pointer capture use the native popup surface;
- input bubbling crosses the popup root back to the owning control.

This is a portal: visual ownership stays with the owner window while pixels and native input live on
another surface. Framework code that needs the actual input/render surface uses
`ResolveInputHostWindow()` rather than assuming that `FindVisualRoot()` is the surface.

Headless tests and the in-surface fallback use the same popup policy and owner context, but render and
hit-test the popup between the adorner and overlay layers on the owner surface.

## Choosing a layer

- Use normal content when the visual participates in ordinary panel layout.
- Use an adorner when placement follows one specific element.
- Use an overlay when placement covers or otherwise depends on the whole window.
- Use a popup when the visual is transient, needs popup focus/dismissal behavior, or may extend
  outside the owner window.
- Do not use `OverlayLayer` as a substitute for a popup when native surface bounds or popup input
  semantics are required.
