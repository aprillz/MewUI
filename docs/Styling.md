# Styling

This document describes MewUI's styling system — a code-first, AOT-friendly approach to reusable, state-aware visual customization.

---

## 1. Overview

MewUI's styling system is built around the following principles:

- **Code-first**: styles are C# objects with typed setters, not XML or CSS
- **AOT-friendly**: no reflection — generic interfaces, typed delegates, and static lambdas
- **Declarative**: state-based visuals are defined via `StateTrigger`, not imperative event handlers
- **Composable**: styles extend other styles via `BasedOn`; containers propagate styles via `StyleSheet`

### Value resolution order

```
Local value (control.Background = ...)
  ↓  if not set
Animated value (transition in progress)
  ↓  if not animating
Trigger value (StateTrigger match)
  ↓  if no trigger matches
Style base setter
  ↓  if no style setter
Inherited value (parent chain)
  ↓  if not inherited
Default value
```

### Style resolution order

```
When StyleName is set:
  Walk parent chain (from self) looking up name in each StyleSheet
    → then Application.StyleSheet
      → if not found, fall through below

When StyleName is unset or name not found:
  Walk parent chain looking up type-based rule in each StyleSheet
    → if not found, fall through below

DefaultStyles (theme default style)    (lowest priority)
```

---

## 2. Style

A `Style` defines base property values, state-conditional triggers, and transitions for a control type.

### 2.1 Basic style

```csharp
var flatButtonStyle = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, Color.Transparent),
        Setter.Create(Control.BorderThicknessProperty, 0.0),
    ],
};
```

### 2.2 Theme-aware setters

Setters can use `Func<Theme, T>` to resolve values dynamically based on the current theme. The style instance is created once and shared — no recreation needed on theme change.

```csharp
var accentButton = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
        Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.AccentText),
        Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.Accent),
    ],
};
```

### 2.3 StateTrigger

Triggers conditionally apply setters when the control's visual state matches. They override base setters for the same property.

```csharp
var accentButton = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
        Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.AccentText),
    ],
    Triggers =
    [
        new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters = [Setter.Create(Control.BackgroundProperty,
                (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15))],
        },
        new StateTrigger
        {
            Match = VisualStateFlags.Pressed,
            Setters = [Setter.Create(Control.BackgroundProperty,
                (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25))],
        },
        new StateTrigger
        {
            Match = VisualStateFlags.None,
            Exclude = VisualStateFlags.Enabled,
            Setters = [
                Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ButtonDisabledBackground),
                Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.DisabledText),
            ],
        },
    ],
};
```

Available flags: `Enabled`, `Hot`, `Focused`, `Pressed`, `Checked`, `Indeterminate`, `Active`, `Selected`, `ReadOnly`.

### 2.4 Transitions

Transitions animate property changes between states (e.g., hover color fade).

```csharp
var style = new Style(typeof(Button))
{
    Transitions =
    [
        Transition.Create(Control.BackgroundProperty),
        Transition.Create(Control.BorderBrushProperty),
        Transition.Create(TextElement.ForegroundProperty),
    ],
    Setters = [...],
    Triggers = [...],
};
```

### 2.5 BasedOn

A style can inherit from another style. The derived style's setters and triggers override the base for the same properties.

```csharp
// Inherit from the default Button theme style
var myButton = new Style(typeof(Button))
{
    BasedOn = Style.ForType<Button>(),
    Setters =
    [
        // Only override what you need — rest comes from BasedOn
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
    ],
};
```

`Style.ForType<T>()` returns the default theme style without requiring a Theme instance.

> **Policy**: if `BasedOn` is not set, only the setters/triggers defined in this style apply. The framework does not auto-merge with the theme style. This matches WPF behavior and keeps styling predictable.

### 2.6 Unset (reverting BasedOn values)

`BasedOn` is additive — a derived style overrides base setters but cannot remove them. `Setter.Unset(property)` fills that gap: within a derived style it unsets the property, so it reverts to the inherited value (or the type default when nothing is inherited), exactly as if no style in the chain had set it. This mirrors CSS `unset`.

```csharp
// Keep the base chrome (background, border, ...) but let the font follow the ambient/inherited value
var menuDropDown = new Style(typeof(ContextMenu))
{
    BasedOn = Style.ForType<ContextMenu>(),
    Setters =
    [
        Setter.Unset(TextElement.FontFamilyProperty),
        Setter.Unset(TextElement.FontSizeProperty),
        Setter.Unset(TextElement.ForegroundProperty),
    ],
};
```

Scope:

- Acts only on the **Style-base tier** of this style chain for the named property. Higher-priority sources still win: a `Local` value, a running animation, and a matching trigger are untouched.
- For an inherited property (`Foreground`, `Font*`), Unset reverts to the value inherited from ancestors; if no ancestor provides one, to the type default (including any `OverrideDefaultValue`).
- In a nested `BasedOn` chain, an Unset at a more-derived level wins over base setters below it, and a still-more-derived level can re-set the property.
- Unset applies to base setters, not to trigger setters.

---

## 3. StyleSheet

`StyleSheet` is a style registry supporting both named styles and type-based rules. Attach it to any `FrameworkElement` (typically a `Window`). It serves two purposes:

1. **Named styles**: Controls with `StyleName` resolve their style from the nearest `StyleSheet` up the element tree.
2. **Type-based rules**: All descendant controls of a given type receive the style automatically, without setting `StyleName` on each one.

### 3.1 Named styles

```csharp
// Define on a window (named styles take a factory, created lazily on first lookup)
window.StyleSheet = new StyleSheet();
window.StyleSheet.Define("accent-button", () => accentButton);
window.StyleSheet.Define("flat-button", () => flatButtonStyle);

// Apply to a control
var btn = new Button { StyleName = "accent-button" };
btn.Content("Save");
```

When `StyleName` is set, MewUI walks from the control itself up the parent chain, looking up the name in each `FrameworkElement`'s `StyleSheet`. If no parent `StyleSheet` contains the name, `Application.StyleSheet` is checked last. If still not found, resolution falls through to type-based rules, then Theme defaults (`DefaultStyles`).

### 3.2 Type-based rules

```csharp
var toolbar = new StackPanel().Horizontal().Spacing(4);
toolbar.StyleSheet = new StyleSheet();
toolbar.StyleSheet.Define<Button>(flatButtonStyle);

// All Buttons inside toolbar get flatButtonStyle automatically
toolbar.Add(new Button().Content("Cut"));
toolbar.Add(new Button().Content("Copy"));
toolbar.Add(new Button().Content("Paste"));
toolbar.Add(new CheckBox().Content("Bold")); // unaffected — only Button is matched
```

Type matching checks exact type first, then base types. `Define<Button>(style)` applies to `Button` and its subclasses.

### 3.3 Nested StyleSheets

Inner StyleSheets override outer ones for the same type. Different types bubble independently.

```csharp
// Outer: all Buttons are flat
outerPanel.StyleSheet = new StyleSheet();
outerPanel.StyleSheet.Define<Button>(flatButtonStyle);

// Inner: Buttons here are accent instead
innerPanel.StyleSheet = new StyleSheet();
innerPanel.StyleSheet.Define<Button>(accentButtonStyle);

// Result:
// outerPanel > Button → flat
// innerPanel > Button → accent
// outerPanel > CheckBox → unaffected (no type rule)
```

---

## 4. Property value sources

Each property value has a source that determines its priority:

| Source | Priority | Description |
|--------|----------|-------------|
| `Local` | Highest | Directly set on the control (e.g., `button.Background = Color.Red`) |
| `Trigger` | High | Set by a matching `StateTrigger` |
| `Style` | Medium | Set by a `Style` base setter |
| `Inherited` | Low | Inherited from parent (e.g., `Foreground` from `Window`) |
| `Default` | Lowest | Property's default value |

### Local values and triggers

When a property has a `Local` value, triggers and style setters are ignored for that property. This matches WPF behavior.

```csharp
var btn = new Button().Content("Red Button");
btn.Background = Color.Red; // Local value — hover trigger won't change this
```

### Foreground and font inheritance

`Foreground`, `FontFamily`, `FontSize`, and `FontWeight` are declared on `TextElement` (the base class for text-bearing elements, above `Control`) with the `Inherits` flag. The `Window` default style sets them, and all descendants inherit them down the tree. Individual controls do **not** set these in their base style. Disabled triggers on specific controls (Button, TextBox, etc.) override `Foreground` with `DisabledText` when needed.

---

## 5. Theme integration

Styles use `Func<Theme, T>` setters to react to theme changes automatically:

```csharp
// This style works in both Light and Dark themes without recreation
Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ButtonFace)
```

When the theme changes:
1. `ResolveAndApplyStyle()` re-runs on each control
2. Same `Style` instance is reused (styles are static/shared)
3. `ResolveValue(newTheme)` produces new colors from the new palette
4. Transitions animate the color change smoothly

### Style.ForType

Since styles are shared globally (not per-theme), you can reference them statically:

```csharp
// No Theme instance needed
var baseStyle = Style.ForType<Button>();
```

---

## 6. Complete example

```csharp
// Define styles (static, shared, theme-aware)
var flatButton = new Style(typeof(Button))
{
    BasedOn = Style.ForType<Button>(),
    Setters =
    [
        Setter.Create(Control.BackgroundProperty,
            (Theme t) => t.Palette.ButtonHoverBackground.WithAlpha(0)),
        Setter.Create(Control.BorderBrushProperty, Color.Transparent),
        Setter.Create(Control.BorderThicknessProperty, 0.0),
    ],
    Triggers =
    [
        new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters = [Setter.Create(Control.BackgroundProperty,
                (Theme t) => t.Palette.ButtonHoverBackground)],
        },
    ],
};

var accentButton = new Style(typeof(Button))
{
    BasedOn = Style.ForType<Button>(),
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
        Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.AccentText),
        Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.Accent),
    ],
    Triggers =
    [
        new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters = [
                Setter.Create(Control.BackgroundProperty,
                    (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15)),
            ],
        },
        new StateTrigger
        {
            Match = VisualStateFlags.Pressed,
            Setters = [
                Setter.Create(Control.BackgroundProperty,
                    (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25)),
            ],
        },
    ],
};

// Register in StyleSheet
window.StyleSheet = new StyleSheet();
window.StyleSheet.Define("accent", () => accentButton);

// Apply via StyleSheet type rule (container-level)
var toolbar = new StackPanel().Horizontal().Spacing(4);
toolbar.StyleSheet = new StyleSheet();
toolbar.StyleSheet.Define<Button>(flatButton);
toolbar.Add(new Button().Content("Cut"));
toolbar.Add(new Button().Content("Copy"));

// Apply via StyleName (per-element)
var saveBtn = new Button { StyleName = "accent" };
saveBtn.Content("Save");
toolbar.Add(saveBtn);

// Local override — ignores all style triggers
var customBtn = new Button().Content("Custom");
customBtn.Background = Color.FromRgb(200, 60, 60);
toolbar.Add(customBtn);
```
