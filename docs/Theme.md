# Theme

This document describes MewUI's theme.

---

## 1. Theme inputs

In MewUI, a `Theme` is derived from the following four inputs:

- `ThemeVariant`: `System` / `Light` / `Dark`
- `Accent` (or `Color`): the accent color
- `ThemeSeed`: base color seeds for Light/Dark
- `ThemeMetrics`: "look & feel" metrics such as sizes, padding, strokes, and fonts

All examples below configure **defaults before** calling `Run(...)`.

### 1.1 ThemeVariant

This input selects the Light/Dark mode, or follows the OS setting when using `System`.

```csharp
using Aprillz.MewUI;

// Default is ThemeVariant.System.
// If you are OK with System, you can omit this.
ThemeManager.Default = ThemeVariant.System;
// ThemeManager.Default = ThemeVariant.Light;
// ThemeManager.Default = ThemeVariant.Dark;
```

### 1.2 Accent

This input provides the accent color. You can use the built-in `Accent.*` presets, or supply a custom `Color`.

```csharp
using Aprillz.MewUI;

ThemeManager.DefaultAccent = Accent.Blue;
```

Note: Custom `Color` is typically used more often for **runtime changes** (see section 3.2).

### 1.3 ThemeSeed

This input provides the base color seeds for Light and Dark variants.

Common properties on `ThemeSeed`:
- `WindowBackground`: window background
- `WindowText`: default text color
- `ControlBackground`: control background
- `ButtonFace`: default button background
- `ButtonDisabledBackground`: disabled button background

```csharp
using Aprillz.MewUI;

ThemeManager.DefaultLightSeed = ThemeSeed.DefaultLight;
ThemeManager.DefaultDarkSeed  = ThemeSeed.DefaultDark;
```

### 1.4 ThemeMetrics

This input provides global UI metrics such as base control size, padding, corner radius, and fonts.

Common properties on `ThemeMetrics`:
- `FontFamily`, `FontSize`, `FontWeight`
- `BaseControlHeight`
- `ControlCornerRadius`
- `ItemPadding`
- `ScrollBarThickness`, `ScrollBarHitThickness`, `ScrollBarMinThumbLength`
- `ScrollWheelStep`, `ScrollBarSmallChange`, `ScrollBarLargeChange`

```csharp
using Aprillz.MewUI;

ThemeManager.DefaultMetrics = ThemeMetrics.Default with
{
    ControlCornerRadius = 6,
    FontSize = 13,
    FontFamily = "Noto Sans"
};
```

#### Default font and the system font

When `FontFamily` is not specified, the theme follows the platform's system UI font: the configured UI font on Windows (localized, e.g. Malgun Gothic on Korean Windows), `.AppleSystemUIFont` on macOS, and `sans-serif` on X11. Assign `ThemeMetrics.SystemFontFamily` to state that explicitly, and read `IsSystemFontFamily` to check whether the system font is in effect.

Assigning a family name uses that font instead of the system font, including names that match a platform's own system font such as `"Segoe UI"`.

```csharp
// Pin Segoe UI instead of the system font
ThemeManager.DefaultMetrics = ThemeMetrics.Default with { FontFamily = "Segoe UI" };

// Follow the system font, change only the size
ThemeManager.DefaultMetrics = ThemeMetrics.Default with { FontSize = 13 };
```

`FontFamily` resolves on read, so any read after the platform package registers (`Win32Platform.Register()` and friends) reports the family that will actually be used. `ThemeMetrics.Default.FontFamily` gives the system font; `ThemeManager.DefaultMetrics.FontFamily` gives the final font including your override.

Characters missing from the chosen font are covered by the font fallback chain, so pinning Segoe UI still renders Korean, Japanese, and Chinese text.

---

## 2. Theme setup at startup

Recommended order:
1) Configure `ThemeManager.Default*` first
2) Build your UI
3) Call `Application.Run(...)`

### 2.1 ThemeSeed customization example

```csharp
using Aprillz.MewUI;

// You do not need to re-assign defaults.
// Only override the parts you want to change.

ThemeManager.DefaultLightSeed = ThemeSeed.DefaultLight with
{
    WindowText = Color.FromRgb(20, 20, 20)
};

ThemeManager.DefaultDarkSeed = ThemeSeed.DefaultDark with
{
    WindowText = Color.FromRgb(240, 240, 240)
};

var mainWindow = new Window()
    .Title("Theme Seed Demo")
    .Content(new TextBlock().Text("Hello, MewUI").Bold());

Application.Run(mainWindow);
```

### 2.2 Applying settings via ApplicationBuilder

Included topics:
- Apply theme inputs via builder `UseTheme/UseAccent/UseSeed/UseMetrics`
- (Optional) Chain platform/backend selection (e.g. `UseWin32/UseDirect2D`) when those packages are referenced

Note:
- The builder applies these values to `ThemeManager.Default*` right before `Run(...)`.

```csharp
using Aprillz.MewUI;

var mainWindow = new Window()
    .Title("Theme + Builder")
    .Content(new TextBlock().Text("Hello"));

Application.Create()
    .UseMetrics(ThemeMetrics.Default with { ControlCornerRadius = 6, FontSize = 13, FontFamily = "Noto Sans" })
    .UseSeed(
        ThemeSeed.DefaultLight with { WindowText = Color.FromRgb(20, 20, 20) },
        ThemeSeed.DefaultDark  with { WindowText = Color.FromRgb(240, 240, 240) })
    // If needed, configure mode/accent as well (System/Blue are defaults so you can omit them)
    // .UseTheme(ThemeVariant.System)
    // .UseAccent(Accent.Blue)
    .UseWin32()
    .UseDirect2D()
    .Run(mainWindow);
```

---

## 3. Runtime theme changes

In general, the following two runtime changes are supported:

- switching `ThemeVariant`
- switching `Accent`

### 3.1 Switching ThemeVariant

```csharp
Application.Current.SetTheme(ThemeVariant.Dark);
// Application.Current.SetTheme(ThemeVariant.Light);
// Application.Current.SetTheme(ThemeVariant.System);
```

### 3.2 Switching Accent

```csharp
Application.Current.SetAccent(Accent.Green);

// Custom color
Application.Current.SetAccent(new Color(0xFF, 0x22, 0x88, 0xFF));
```

---

## 4. Theme change callback

When the theme changes (theme variant switch, OS theme change in `System` mode, or a dependent property update), you may want to re-apply some properties based on the current theme.

Use `WithTheme((theme, control) => ...)` for this purpose.

```csharp
var accentButton = new Button()
    .Text("Accent Button")
    .WithTheme((theme, c) =>
    {
        c.Background(theme.Palette.Accent);
        c.Foreground(theme.Palette.AccentText);
    });
```

---

## 5. Theme change event

```csharp
Application.Current.ThemeChanged += (oldTheme, newTheme) =>
{
    // Logging / persistence, etc.
};
```

