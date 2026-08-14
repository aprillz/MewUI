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
Animated value (transition in progress)
  ↓  if the effective source is not being animated
Local value (control.Background = ...)
  ↓  if not set
ElementTrigger value
  ↓  if no element trigger supplies the property
Binding value
  ↓  if no binding supplies the property
Style value (matching StateTrigger, then base setter)
  ↓  if neither style layer supplies the property
Inherited value (parent chain)
  ↓  if not inherited
Default value
```

### Style resolution order

```
Application style (highest priority):
  StyleName set   → nearest named style
  StyleName unset → nearest type-based rule
  (the two selectors are mutually exclusive)
    ↓ values not supplied by the application style
Nearest framework DefaultStyle for the control's runtime type
    ↓ values not supplied by either style layer
Inherited or property default value
```

The framework default and the selected application style form two layers in the same `Style`
value-source tier. Setters and triggers accumulate from the lower layer upward, so the application
style wins when it defines a property and otherwise inherits the framework value. Transitions use
the same precedence rule: the application style's first matching transition is used, then the
framework default is consulted.

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
// Extend a reusable application style
var myButton = new Style(typeof(Button))
{
    BasedOn = sharedButtonStyle,
    Setters =
    [
        // Only override what you need — rest comes from sharedButtonStyle
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
    ],
};
```

`BasedOn` composes styles within one layer. Independently of `BasedOn`, an ordinary named or
type-rule style is automatically layered over the nearest framework default style for the
control's runtime type. Explicit `BasedOn = Style.ForType<T>()` remains supported, but is normally
unnecessary; if it names the same default style that the runtime layer already selected, it is
applied only once.

### 2.6 Replacing the framework default style

Set `OverridesDefaultStyle = true` when an application style intentionally supplies the complete
look and must not inherit any framework default setters, triggers, or transitions. The style's own
`BasedOn` chain is still applied.

```csharp
var looklessButton = new Style(typeof(Button))
{
    OverridesDefaultStyle = true,
    Setters =
    [
        Setter.Create(Control.TemplateProperty, (ControlTemplate?)myButtonTemplate),
    ],
};
```

This option belongs on `Style`, not on the control. Consequently it applies consistently whether
the style is selected by `StyleName`, a type rule, or directly by framework code. For a control
whose visuals are supplied by its default template (for example `NumericUpDown`, `DropDownButton`,
or `SplitButton`), a replacement style must provide a `Template` or the control will be lookless.

### 2.7 Unset (reverting style values)

Style layering is additive — a higher style normally overrides lower values but does not remove them. `Setter.Unset(property)` fills that gap: at its declaration point it removes the current Style-tier candidate, so the property reverts to the inherited value (or the type default when nothing is inherited), exactly as if no style layer had set it. This mirrors CSS `unset`.

```csharp
// Keep the base chrome (background, border, ...) but let the font follow the ambient/inherited value
var menuDropDown = new Style(typeof(ContextMenu))
{
    Setters =
    [
        Setter.Unset(TextElement.FontFamilyProperty),
        Setter.Unset(TextElement.FontSizeProperty),
        Setter.Unset(TextElement.ForegroundProperty),
    ],
};
```

Scope:

- Acts on the entire **Style tier** for the named property, including a value inherited from the lower framework-default layer. Higher persistent sources (`Local`, `ElementTrigger`, and `Binding`) are unaffected. A later matching application trigger can set the property again.
- For an inherited property (`Foreground`, `Font*`), Unset reverts to the value inherited from ancestors; if no ancestor provides one, to the type default (including any `OverrideDefaultValue`).
- In a nested `BasedOn` chain, an Unset at a more-derived level wins over base setters below it, and a still-more-derived level can re-set the property.
- `Unset` can appear in base setters or trigger setters. In a trigger it takes effect only while that trigger matches; later active declarations can set the property again.

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

When `StyleName` is set, MewUI walks from the control itself up the parent chain, looking up the name in each `FrameworkElement`'s `StyleSheet`. If no parent `StyleSheet` contains the name, `Application.StyleSheet` is checked last. A name that is still unresolved does **not** fall back to a type rule: once the control is attached and all scopes are known, it produces an error identifying the missing name and searched scopes.

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

A type rule is considered only when `StyleName` is not set. A named style and a type rule therefore
never merge implicitly. Use `BasedOn` when the named style should deliberately extend another
application style.

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
| `Local` | Highest | Directly set on the element (e.g., `button.Background = Color.Red`) |
| `ElementTrigger` | Higher | Set by a matching trigger declared directly on the element |
| `Binding` | High | Current value supplied by a binding |
| `Style` | Medium | Final candidate from application/default setters and matching `StateTrigger`s |
| `Inherited` | Low | Inherited from parent (e.g., `Foreground` from `Window`) |
| `Default` | Lowest | Property's default value |

An animation temporarily presents a value over whichever source is currently effective; it is an
overlay rather than another persistent candidate in the table.

### Local values and triggers

When a property has a `Local` value, element triggers, bindings, and style candidates are retained but shadowed for that property. Clearing the local value reveals the next candidate.

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

Use this API when the actual default-style object is needed for explicit composition or inspection;
ordinary partial application styles inherit the runtime default automatically.

## 6. Migration from replacement semantics

Named styles and type rules now preserve unspecified framework defaults. For `Control` itself the
newly inherited surface is currently limited to `CornerRadius` and `BorderThickness`; richer
control defaults can also contribute templates, padding, colors, triggers, and transitions. If an
existing style was intended to erase all of those values, add `OverridesDefaultStyle = true` and
provide every required value, especially `Template` for default-template controls. In DEBUG builds,
the property inspector labels style candidates as `Framework default` or `Application` and marks
framework values that are newly inherited through this cascade.

---

## 7. Complete example

```csharp
// Define styles (static, shared, theme-aware)
var flatButton = new Style(typeof(Button))
{
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
