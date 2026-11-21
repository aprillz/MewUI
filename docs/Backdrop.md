# Backdrop

## Element backdrop

`BackdropFilter` runs a filter over what is drawn behind an element, inside the element's layout box. The element's own background and content are drawn on top, so a translucent `Background` tints the result, as with frosted glass.

```csharp
new Border()
    .CornerRadius(12)
    .Padding(12)
    .Background(Color.FromArgb(70, 255, 255, 255))
    .BackdropFilter(new BlurFilter(12, 12))
    .Child(new TextBlock().Text("Frosted glass"));
```

The filter's source input is the pixels behind the element; a `BlurFilter` with no input blurs them. Set `BackdropFilter` to `null` to remove the backdrop.

### What the backdrop reads

- Only the element's layout box. The blur takes the pixels at the edge of the box for whatever lies beyond it, so nothing outside the box shows in the backdrop, and a change beside the box does not redraw it.
- What is drawn behind the element in the same window, including other backdrops behind it. It is not what is behind the window; for that, see [System backdrop](#system-backdrop).
- Inside a faded element (`Opacity` below 1) or an element with a `CacheMode`, what that element has drawn so far, not the window behind it.

The backdrop is cut to the element's `CornerRadius` and to any rounded clip the element is drawn in.

An element that itself has `Opacity` below 1 reads what is behind it and fades together with its backdrop.

### When the backdrop cannot be read

Some places cannot read what is behind the element. `BackdropFallbackBrush` fills the backdrop there instead; without one, nothing is drawn in its place and only the element's own background shows.

```csharp
border.BackdropFallbackBrush = new SolidColorBrush(Color.FromArgb(220, 40, 40, 48));
```

Give the fallback a color that reads well under the element's content on its own, since nothing behind shows through it.

### Cost

A frame redraws the backdrop only where something under its box changed, and only as far as the blur reaches from the change. Inside a faded element or a cached element the backdrop is read and filtered whole whenever anything in its box is redrawn. A larger box and a larger blur radius cost more.

## System backdrop

A window or a context menu can ask the platform to fill its transparent parts with a material: what is behind the window, blurred, or the blurred desktop wallpaper. The content draws on top, so a translucent `Background` tints the material.

```csharp
new Window()
    .AllowsTransparency()
    .Background(Color.FromArgb(96, 32, 32, 32))
    .SystemBackdrop(SystemBackdrop.Acrylic)
    .BackdropFallbackBrush(Color.FromRgb(32, 32, 32));
```

On a window, `SystemBackdrop` applies only while `AllowsTransparency` is set; an opaque window covers whatever a material would show. It can change at any time, before or after the window is shown, and `SystemBackdrop.None` removes it.

| Value | What shows behind the content |
|---|---|
| `None` | Nothing: transparent parts show what is behind as it is |
| `Blur` | What is behind, blurred |
| `Acrylic` | What is behind, blurred more heavily and saturated |
| `Mica` | The blurred desktop wallpaper, which stays still when the window moves over other windows |

### What each platform shows

A platform that cannot show the material asked for lowers it to the nearest one it can.

| Platform | Blur | Acrylic | Mica |
|---|---|---|---|
| Windows 11, Direct2D backend | Blur | Acrylic | Mica |
| Windows, MewVG backend | None | None | None |
| macOS | Blur | Acrylic | Acrylic |
| Linux, KDE Plasma (X11) | Blur | Blur | Blur |
| Other Linux desktops, browser, framebuffer | None | None | None |

Earlier Windows versions show what their system compositor offers, lowering `Mica` first. With the MewVG backend a transparent window hands Windows its pixels as a layered bitmap, which the compositor's material would hide rather than show through, so it shows none. Mica is a Windows 11 material; macOS has no counterpart and shows the acrylic. On macOS the materials are system materials that follow the light and dark appearance (popover and HUD window). On KDE Plasma the material appears only while the desktop's Blur effect is enabled.

### When there is no material

`BackdropFallbackBrush` is the one fallback for both kinds of backdrop. Where the material cannot be shown, it fills the window under the `Background`, so a translucent tint still reads:

```csharp
window.SystemBackdrop = SystemBackdrop.Mica;
window.BackdropFallbackBrush = Color.FromRgb(32, 32, 32);
```

Without a fallback brush, nothing is drawn in the material's place.

### Context menus

```csharp
new ContextMenu()
    .SystemBackdrop(SystemBackdrop.Acrylic)
    .Item("Cut")
    .Item("Copy");
```

Submenus opened from the menu follow its material. Where the menu is hosted decides how the material is made:

- A menu in its own window gets the platform's material as in the table above, on the menu's box and cut to its corners. Its shadow stays outside the material.
- A menu drawn inside its owner's surface, as in the browser, gets the same look from what that surface holds: `Blur` is a blur, and `Acrylic` a heavier blur with more saturation. `Mica` shows as `Acrylic` there, since a surface cannot read the wallpaper.

While a material is set, the theme's opaque menu background becomes a translucent tint so the material shows through it. A `Background` set on the menu is used as given. The menu's `BackdropFallbackBrush` defaults to the theme's menu background.

### Cost

The system compositor redraws the material whenever what is behind the window changes, and a larger window costs more. While nothing behind the window changes, the material costs nothing. A menu drawn inside its owner's surface costs what an element backdrop of its size costs.
