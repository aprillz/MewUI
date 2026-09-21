# Custom Controls

## Overview

- This is a **developer reference** for building custom controls in MewUI.
- Layout uses **DIP**, rendering requires **pixel‑aligned geometry**.
- Public state is declared as `MewProperty`, state-dependent appearance is declared as `StateTrigger`s of a `Style`, and `OnRender` draws only from values whose change invalidates the visual.
- The sample below is a complete self-drawn `SimpleNumericUpDown`, and the comments explain **what each spot is responsible for** from a CustomControl perspective. The built-in `NumericUpDown` composes its parts with a template instead (see [Templates](#template)); the sample draws everything itself so that every override point appears in one place.

---

## Detailed Explanation

### <a id="scope"></a>Scope and Conventions

- Sizes are computed in **DIP**, rendering is **pixel‑snapped**.
- Measure/Arrange must operate in **logical coordinates (DIP)** only; pixel snapping is applied in Render.
- Use `GetDpi()` / `context.DpiScale` to respond to DPI changes.
- **Never do pixel math during Measure.** Mixing pixel snapping into Measure causes layout mismatches.
- `Bounds` is in **window coordinates**, not relative to the parent. Rects passed to a child's `Arrange` and rects drawn in `OnRender` are window coordinates too.

### <a id="base"></a>Choosing a Base Class

| Base class | Use it when |
|---|---|
| `Control` | The control draws itself or builds its visuals from a template. Provides `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Padding`, font properties, `Foreground`, styles, visual states and `Template`. |
| `ContentControl` | The control shows a single `Content` element. Measure, arrange, rendering and hit testing of the content are already implemented. |
| `CommandSourceControl` | A `ContentControl` that runs a `Command` (`Command`, `CommandData`, `QueryCommandCanExecute()`, `InvokeCommand()`). `Button` derives from it. |
| `RangeBase` | The control has `Value`, `Minimum`, `Maximum` with clamping. Override `OnValueChanged(double value)` and `OnCoerceValue(double value)`; change the value from input with `CommitValue(double value)`. |
| `Panel` | The element lays out many children (`Children`, `Add`, `Remove`). A panel is a `FrameworkElement`, not a `Control`: it has no style, template or visual state. |

### <a id="properties"></a>Properties (MewProperty)

- Declare public state with `MewProperty<T>.Register<TOwner>(name, defaultValue, options, changed, coerce, validate)` and expose it through a CLR property that calls `GetValue` / `SetValue`. Only then can the value be styled, bound and animated.
- `MewPropertyOptions` makes invalidation automatic: `AffectsLayout` calls `InvalidateMeasure()`, `AffectsRender` calls `InvalidateVisual()`, `AffectsVisualState` calls `InvalidateVisualState()`, `Inherits` takes the value from the parent chain when the element has none, `BindsTwoWayByDefault` makes `Bind` two-way.
- `changed` receives `(owner, oldValue, newValue)`, `coerce` receives `(owner, proposedValue)` and returns the value to store, `validate` receives `(owner, proposedValue)` and throws to reject it. Call `CoerceValue(property)` when something the coercion depends on changes.
- State that the control derives itself (pressed, selected, editing) is a **read-only** property: `RegisterReadOnly` returns a `MewPropertyKey<T>`, the key stays private, `Key.Property` is exposed publicly, and the control writes with `SetValue(key, value)`.
- A per-type default is set with `SomeProperty.OverrideDefaultValue<TOwner>(value)` in the static constructor. It is a default, so `ClearLocalValue(property)` returns to it and a style can still override it.
- Value precedence is `Local > Animated > Trigger > Style > Inherited > Default`. A value assigned in the constructor is a local value and **blocks style setters and triggers** for that property. Put appearance defaults (colors, padding, minimum size) in the style, not in the constructor.

```csharp
public static readonly MewProperty<string> FormatProperty =
    MewProperty<string>.Register<SimpleNumericUpDown>(nameof(Format), "0.##", MewPropertyOptions.AffectsLayout);

private static readonly MewPropertyKey<bool> IsSelectedPropertyKey =
    MewProperty<bool>.RegisterReadOnly<MyItem>(nameof(IsSelected), false,
        MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

public static readonly MewProperty<bool> IsSelectedProperty = IsSelectedPropertyKey.Property;

public bool IsSelected => GetValue(IsSelectedProperty);

private void SetIsSelected(bool value) => SetValue(IsSelectedPropertyKey, value);
```

### <a id="measure"></a>Size Calculation (MeasureContent)

- `MeasureContent` is the **single source of desired size**. `Width`/`Height`, `MinWidth`/`MaxWidth`, `MinHeight`/`MaxHeight` and `Margin` are applied by the framework around it.
- This stage only computes how much space the control needs; it does not decide placement.
- Text is measured with `MeasureEngineText(text, maxWidth, wrapping)`, which uses the control's font properties and DPI. Measure the display string (format applied).
- The final size includes `Padding`, chrome (button area), and `GetBorderVisualInset()`.
- If the control should align with the theme’s baseline size, set `MinHeight` to `Theme.Metrics.BaseControlHeight` with a theme setter in the control's style.
- In that case, `MeasureContent` can return the natural content height; the framework applies `MinHeight`.
- A property that changes the desired size is registered with `AffectsLayout`. State that is not such a property (here `Value`, which is `AffectsRender` only in `RangeBase`) needs an explicit `InvalidateMeasure()`.

Example:
```csharp
protected override Size MeasureContent(Size availableSize)
{
    var textSize = MeasureEngineText(Value.ToString(Format));
    double width = textSize.Width + Padding.HorizontalThickness;
    double height = textSize.Height + Padding.VerticalThickness;
    return new Size(width, height).Inflate(GetBorderVisualInset());
}
```

### <a id="arrange"></a>Internal Layout (ArrangeContent) and Child Elements

- This sample does not override `ArrangeContent`; it computes internal layout using the final `Bounds`.
- Controls with children must compute child rects in `ArrangeContent(Rect bounds)` and call `Arrange` for each child.
- Arrange defines **where** children go inside the allotted space.
- Always assume **DesiredSize from Measure** and **actual Bounds** can differ.
- A control that hosts child elements itself does four things: attach each child with `AttachChild` / `DetachChild` (the `Parent` setter is not public), implement `IVisualTreeHost.VisitChildren` so the children are visited **topmost first** and only when they take part in the current frame, measure and arrange them in `MeasureContent` / `ArrangeContent`, and draw them in `RenderSubtree`. The base hit test probes children in `VisitChildren` order and takes the first hit.
- A child that belongs to the control logically but may be shown elsewhere (a content slot projected by a template) is owned with `AttachLogicalChild` / `DetachLogicalChild`, or replaced with `ChangeLogicalChild(oldChild, newChild)`. Reject invalid candidates in the property's `validate` callback with `ValidateLogicalChild`.
- Prefer `ContentControl`, `Panel` or a template over hosting children by hand: they already implement all of the above.

### <a id="render"></a>Rendering (OnRender)

- Override `OnRender(IGraphicsContext context)` only. `Render` is sealed; children are drawn in `RenderSubtree`, which runs after `OnRender`.
- `Control.OnRender` draws `Background` and the border from `BorderBrush`, `BorderThickness` and `CornerRadius` (nothing when a template is applied). Call `base.OnRender(context)` for the standard chrome, or call `DrawBackgroundAndBorder` yourself.
- `GetSnappedBorderBounds` and `LayoutRounding.SnapBoundsRectToPixels` ensure pixel alignment.
- Render order: **background → border → content**.
- Structure code so layout math and rendering share the same rects.
- Render **must not** recompute measurement; it only consumes the final `Bounds`. Never call `InvalidateMeasure()` from `OnRender`.
- Text is drawn with `DrawEngineText(context, text, bounds, color, horizontalAlignment, verticalAlignment, wrapping, trimming)`.

**`OnRender` does not run on every frame.** The framework records what `OnRender` draws and replays the recording in later frames. `OnRender` runs again only after the element was invalidated: `InvalidateVisual()`, a change of an `AffectsRender` or `AffectsLayout` property, a new size, or a new arrangement of the element.

- Everything `OnRender` reads must be state whose change invalidates the visual. A `MewProperty` with `AffectsRender` does that by itself. For a private field or external data (a model, a clock) call `InvalidateVisual()` at the place where it changes; otherwise the screen keeps showing the old drawing.
- When an element only moves, its recording is replayed at the new position without calling `OnRender`. Express every coordinate relative to `Bounds`.
- Do not use `OnRender` as a per-frame tick and do not change state in it. Content that changes on every frame calls `InvalidateVisual()` on every frame from where the change happens; an element invalidated on every frame is drawn directly instead of being recorded, with no further work.
- Keep `Save()` / `Restore()` balanced inside `OnRender`, and do not call `BeginFrame`, `EndFrame` or `Clear`. A drawing that does so cannot be recorded.
- A control that draws its children in its own `RenderSubtree` override is recorded together with those children as one drawing, so an invalidation of any child draws the whole control again.

### <a id="visualstate"></a>Visual States and Styles

- State-dependent appearance (hover, pressed, focused, selected, disabled) is declared in a `Style` with `StateTrigger`s. It is **not** implemented with extra color properties or with state checks in `OnRender`: `OnRender` just draws `Background`, `BorderBrush` and `Foreground`, and the triggers change those values.
- `Control.ComputeVisualState()` produces the `VisualStateFlags`: `Enabled` (from `IsEffectivelyEnabled`), `Hot` (mouse over or captured), `Focused` (focused or focus within, in an active window), `Pressed` (`IsPressed`), `Invalid` (binding validation error). Set the pressed state with `SetPressed(bool)`.
- Override `ComputeVisualState()` to add control-specific flags such as `Selected`, `Checked`, `Active` or `ReadOnly`. Every input of the override must be a property registered with `AffectsVisualState`, or the control must call `InvalidateVisualState()` when the input changes.
- A `StateTrigger` matches when all `Match` flags are present and all `Exclude` flags are absent (disabled is `Match = None, Exclude = Enabled`). Triggers are evaluated in declaration order and the last matching declaration wins for each property. `Style.Transitions` animates the change.
- Use theme setters, `Setter.Create(property, theme => value)`, so that one style instance follows theme changes.
- A change of visual state alone does not call `OnRender`; the redraw comes from the `AffectsRender` properties that the triggers change. A control that reads `CurrentVisualState` inside `OnRender` overrides `OnVisualStateChanged` and calls `InvalidateVisual()` there.
- Default styles registered through `DefaultStyles` exist for the framework's own control types. A control outside the core gives itself a default appearance with a type rule on its own `StyleSheet`, which is layered over the nearest framework default style (the `Control` style supplies `CornerRadius` and `BorderThickness` from the theme). An application can still restyle the control with `StyleName`.
- State of a **part** of a self-drawn control (which of two buttons is hovered) has no flag. Either build the part from a real control inside a template, so that it has its own visual state, or keep the part state in a field and call `InvalidateVisual()` whenever it changes, as the sample does.

```csharp
protected override VisualState ComputeVisualState()
{
    var state = base.ComputeVisualState();
    var flags = state.Flags;
    if (IsSelected)
    {
        flags |= VisualStateFlags.Selected;
    }

    return state with { Flags = flags };
}

private static readonly Style _defaultStyle = CreateDefaultStyle();

private static Style CreateDefaultStyle() =>
    new(typeof(MyItem))
    {
        Transitions = [Transition.Create(BackgroundProperty)],
        Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground)],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.ButtonHoverBackground)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Selected,
                Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.Accent)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.None,
                Exclude = VisualStateFlags.Enabled,
                Setters =
                [
                    Setter.Create(BackgroundProperty, theme => theme.Palette.DisabledControlBackground),
                    Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText),
                ],
            },
        ],
    };

public MyItem()
{
    StyleSheet = new StyleSheet();
    StyleSheet.Define<MyItem>(_defaultStyle);
}
```

### <a id="template"></a>Templates

- `Control.Template` (`ControlTemplate?`) replaces the control's own visuals with a built element tree. While a template is applied, `Control.OnRender` draws no chrome, and measure, arrange, rendering and hit testing go to the template root.
- `DelegateControlTemplate<TControl>` takes a build function `(owner, context) => root`. One template object can be applied to many controls; each application builds its own tree.
- In the build function, `context.Register(name, element)` registers a named part, `context.Bind(target, targetProperty, sourceProperty)` keeps a part property in sync with a property of the owner, and `context.BindChrome(target)` forwards `Background`, `BorderBrush`, `BorderThickness` and `CornerRadius` to the part that draws the chrome.
- A `ContentPresenter` inside the template shows an element slot of the owner. With `ContentSource` unset it shows the displayed content of a `ContentControl` owner and stays empty for an owner without a content slot; set `ContentSource` to show another element-typed property.
- The control looks up parts in `OnApplyTemplate()` with `GetTemplateChild<T>(name)`, which returns `null` when the part is missing. The template is built on the first measure and is **rebuilt** when `Template`, the theme or the DPI changes, so `OnApplyTemplate` runs again each time: release what was taken from the previous parts (event handlers) before taking the new ones.
- `Template` is a `MewProperty`. It can be assigned directly, or supplied by a style with `Setter.Create(Control.TemplateProperty, (ControlTemplate?)template)`, which is how the built-in `NumericUpDown` gets its template. A directly assigned template is a local value and wins over a style.

```csharp
public sealed class SearchBox : Control
{
    public const string PART_TEXT_BOX = "PART_TextBox";

    private TextBox? _textBox;

    public SearchBox()
    {
        Template = new DelegateControlTemplate<SearchBox>(BuildTemplate);
    }

    private static Element BuildTemplate(SearchBox owner, ControlTemplateContext context)
    {
        var textBox = new TextBox { BorderThickness = 0, Background = Color.Transparent };
        context.Register(PART_TEXT_BOX, textBox);

        // A templated control draws no chrome of its own, so a part has to draw it.
        var chrome = new Border { Child = textBox, ClipToBounds = true };
        context.BindChrome(chrome);
        context.Bind(chrome, PaddingProperty);
        return chrome;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox != null)
        {
            _textBox.TextChanged -= OnTextChanged;
        }

        _textBox = GetTemplateChild<TextBox>(PART_TEXT_BOX);
        if (_textBox != null)
        {
            _textBox.TextChanged += OnTextChanged;
        }
    }

    private void OnTextChanged(string text)
    {
    }
}
```

### <a id="state"></a>Input

- Override `OnMouseDown` / `OnMouseUp` / `OnMouseMove` / `OnMouseWheel` / `OnMouseEnter` / `OnMouseLeave` / `OnKeyDown` / `OnKeyUp` and call the base method first. Mouse and key events bubble from the target to its ancestors until `e.Handled` is set, so set `e.Handled = true` for input the control consumed. A handled `KeyDown` also drops the text input of that keystroke.
- `e.GetPosition(this)` returns the pointer position relative to the element. Add the `Bounds` origin to compare it with rects computed from `Bounds`. `MouseWheelEventArgs.Delta` is a `Vector`; vertical scrolling is `Delta.Y`.
- Capture on MouseDown and release on MouseUp to guarantee input consistency: `Window.CaptureMouse(element)` / `Window.ReleaseMouseCapture()` on the window from `FindVisualRoot()`. The request is ignored for an element that is not effectively enabled or is hidden, and the capture ends by itself when the holder leaves the tree or becomes disabled or hidden. No MouseUp arrives in that case, so clear press state when `IsMouseCaptured` turns false (`OnMewPropertyChanged` with `IsMouseCapturedProperty`).
- A control that takes keyboard input must be focusable: `FocusableProperty.OverrideDefaultValue<T>(true)` in the static constructor, and `Focus()` on mouse down. `IsTabStop = false` removes a part from the Tab order only.
- Hit‑test logic must use **the same split geometry** as rendering.
- When state changes, choose **InvalidateVisual** vs **InvalidateMeasure** correctly.
- Gate input using **`IsEffectivelyEnabled`**, not `IsEnabled`.
  - If a parent is disabled, a child with `IsEnabled == true` must still ignore input.
  - For that reason, input handling, visual state, and color decisions should follow `IsEffectivelyEnabled`. React to a change in `OnEnabledChanged()`.

### <a id="theme"></a>Theme and Metrics

- Colors and sizes come from `Theme.Palette.*` and `Theme.Metrics.*` (`Theme` is a protected property of `FrameworkElement`).
- In a style, read the theme through theme setters; they are resolved again when the theme changes. In `OnRender`, read `Theme` directly; a theme change invalidates measure and visual of every element.
- Override `OnThemeChanged(Theme oldTheme, Theme newTheme)` only for values the control caches itself. `OnDpiChanged(uint oldDpi, uint newDpi)` plays the same role for DPI.
- An empty `FontFamily` follows the theme's font family. `Foreground` and the font properties are inherited from ancestors.

### <a id="utils"></a>Utility Methods (State, Border, Text, and DIP)

- `GetDpi()` returns the effective DPI (`uint`). Use `dpiScale = GetDpi() / 96.0` when converting DIPs to device pixels.
- `CurrentVisualState` is the visual state resolved for the control (`IsEnabled`, `IsHot`, `IsFocused`, `IsPressed`, `IsActive`, `IsChecked`, `IsIndeterminate`, `Flags`).
- `PickAccentBorder(theme, baseBorder, state, hoverMix)`, `PickButtonBackground(state, normalBackground)` and `PickControlBackground(state, normalBackground)` map a visual state to a color for drawing that a style cannot express (see the redraw note in [Visual States and Styles](#visualstate)).
- `DrawBackgroundAndBorder(context, bounds, background, borderBrush, borderThicknessDip, cornerRadiusDip)` draws a pixel-snapped background + border. An overload takes a `Thickness` and a `CornerRadius` for per-side thickness and per-corner radius.
- `GetBorderVisualInset()` returns the border thickness snapped to whole device pixels; use it for both measuring and the inner rect so rendering matches layout.
- `GetSnappedBorderBounds(bounds)` snaps a box to device pixels.
- `MeasureEngineText(...)` / `DrawEngineText(...)` measure and draw text with the control's font properties; `GetTextRunStyle()` returns those properties for the text engine.
- `LayoutRounding` helpers keep geometry stable across fractional DPI and avoid 1px clipping artifacts:
- `LayoutRounding.SnapBoundsRectToPixels(...)` for background/border/layout boxes.
- `LayoutRounding.SnapViewportRectToPixels(...)` for viewports (won’t shrink).
- `LayoutRounding.MakeClipRect(...)` for clip rectangles passed to `SetClip` / `SetClipRoundedRect`. The caller snaps the clip; the graphics context does not.
- `LayoutRounding.SnapThicknessToPixels(...)` for border thickness that must be whole pixels.
- `LayoutRounding.RoundToPixel(...)` for scalars such as a radius or an offset.
- `LayoutRounding.ExpandClipByDevicePixels(...)` for clip rects that must include the last pixel row/col.

Example: standard chrome + pixel-snapped inner clip

```csharp
var bounds = GetSnappedBorderBounds(Bounds);
DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, CornerRadius);

var inner = bounds.Deflate(GetBorderVisualInset());
context.Save();
context.SetClip(LayoutRounding.MakeClipRect(inner, context.DpiScale));
// draw content
context.Restore();
```

### <a id="invalidate"></a>Invalidation Rules

- `Format` change: registered with `AffectsLayout`, so measure and visual are invalidated automatically.
- `Value` change: `AffectsRender` in `RangeBase`; text width may change → `InvalidateMeasure()` in `OnValueChanged`. `InvalidateMeasure()` on an element also invalidates its visual.
- Hover/pressed of the whole control: `IsMouseOver`, `IsPressed` and the other state properties are `AffectsVisualState`; the style triggers change `AffectsRender` properties.
- Hover/pressed of a part kept in a field: `InvalidateVisual()`.
- A property that affects neither layout nor drawing (`Step`): `MewPropertyOptions.None`.

---

## Full Sample Code

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

public sealed class SimpleNumericUpDown : RangeBase
{
    // This enum keeps interaction state in one place.
    // Custom controls should keep non‑public UI state internal so
    // input handling and rendering share the same state.
    private enum ButtonPart
    {
        None,
        Decrement,
        Increment
    }

    // Display format changes the measured text, so it is a layout-affecting property.
    // The option invalidates measure and visual; no manual invalidation is needed.
    public static readonly MewProperty<string> FormatProperty =
        MewProperty<string>.Register<SimpleNumericUpDown>(nameof(Format), "0.##", MewPropertyOptions.AffectsLayout);

    // Interaction step affects neither layout nor drawing.
    public static readonly MewProperty<double> StepProperty =
        MewProperty<double>.Register<SimpleNumericUpDown>(nameof(Step), 1.0);

    // Default appearance, including every state of the whole control. Shared by all instances.
    private static readonly Style _defaultStyle = CreateDefaultStyle();

    // Part state has no visual state flag. It is kept in fields, and every change
    // calls InvalidateVisual because OnRender reads these fields.
    private ButtonPart _hoverPart;
    private ButtonPart _pressedPart;

    // Per-type defaults go to the static constructor, so ClearLocalValue restores them
    // and a style can still override them.
    static SimpleNumericUpDown()
    {
        FocusableProperty.OverrideDefaultValue<SimpleNumericUpDown>(true);
        MaximumProperty.OverrideDefaultValue<SimpleNumericUpDown>(100.0);
    }

    public SimpleNumericUpDown()
    {
        // A type rule on the control's own StyleSheet is its default style.
        // Do not assign Background/Padding/MinHeight here: a local value blocks style setters and triggers.
        StyleSheet = new StyleSheet();
        StyleSheet.Define<SimpleNumericUpDown>(_defaultStyle);
    }

    public string Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    private static Style CreateDefaultStyle() =>
        new(typeof(SimpleNumericUpDown))
        {
            Transitions =
            [
                Transition.Create(BackgroundProperty),
                Transition.Create(BorderBrushProperty),
            ],
            Setters =
            [
                // Theme setters are resolved again when the theme changes.
                Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground),
                Setter.Create(BorderBrushProperty, theme => theme.Palette.ControlBorder),
                Setter.Create(PaddingProperty, new Thickness(8, 4, 8, 4)),
                Setter.Create(MinHeightProperty, theme => theme.Metrics.BaseControlHeight),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(BorderBrushProperty,
                            theme => Color.Composite(theme.Palette.ControlBorder, theme.Palette.AccentBorderHotOverlay)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(BorderBrushProperty, theme => theme.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(BackgroundProperty, theme => theme.Palette.DisabledControlBackground),
                        Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText),
                    ],
                },
            ],
        };

    protected override void OnValueChanged(double value)
    {
        // Value is AffectsRender only, but the displayed text may change width → re‑measure.
        // InvalidateMeasure also invalidates the visual.
        InvalidateMeasure();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        // A capture that ends without a MouseUp (disabled, hidden, removed) must still end the press.
        if (property == IsMouseCapturedProperty && !IsMouseCaptured)
        {
            ClearPressedPart();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // MeasureContent defines the desired size of a custom control.
        // Compute in DIP only; pixel snapping belongs to Render.
        // Measure the actual display string with the control's font properties.
        var textSize = MeasureEngineText(Value.ToString(Format));

        // Content + padding + chrome.
        double width = textSize.Width + Padding.HorizontalThickness + GetButtonAreaWidth();

        // Use natural content height; the style's MinHeight enforces the baseline size.
        double height = textSize.Height + Padding.VerticalThickness;

        // Include border inset in desired size.
        return new Size(width, height).Inflate(GetBorderVisualInset());
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // OnRender runs when the visual was invalidated, not on every frame.
        // Everything read here is a property with AffectsRender/AffectsLayout, the theme,
        // or a field whose change calls InvalidateVisual.

        // Background and border. The style triggers already resolved the state-dependent colors.
        base.OnRender(context);

        // Input and rendering share the same split geometry.
        GetPartRects(out var inner, out var textRect, out var decRect, out var incRect);

        if (decRect.Width > 0)
        {
            // Clip the button fills to the rounded inner contour. Save/Restore stay balanced.
            double dpiScale = GetDpi() / 96.0;
            double innerRadius = Math.Max(0, LayoutRounding.RoundToPixel(CornerRadius, dpiScale) - GetBorderVisualInset());
            context.Save();
            context.SetClipRoundedRect(LayoutRounding.MakeClipRect(inner, context.DpiScale), innerRadius, innerRadius);
            context.FillRectangle(decRect, GetPartBackground(ButtonPart.Decrement));
            context.FillRectangle(incRect, GetPartBackground(ButtonPart.Increment));
            context.Restore();

            // Visual separators for clarity.
            double x = incRect.Left;
            context.DrawLine(new Point(x, incRect.Y + 2), new Point(x, incRect.Bottom - 2), Theme.Palette.ControlBorder, 1);

            x = decRect.Left;
            context.DrawLine(new Point(x, decRect.Y), new Point(x, decRect.Bottom), Theme.Palette.ControlBorder, 1);
        }

        // Draw text last to sit above chrome. Foreground already holds the disabled color when disabled.
        DrawEngineText(context, Value.ToString(Format), textRect, Foreground,
            TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

        if (decRect.Width > 0)
        {
            // Glyph sizes follow theme metrics.
            double chevronSize = Theme.Metrics.BaseControlHeight / 6;
            Glyph.Draw(context, decRect.Center, chevronSize, Foreground, GlyphKind.ChevronDown);
            Glyph.Draw(context, incRect.Center, chevronSize, Foreground, GlyphKind.ChevronUp);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // Block input when disabled.
        if (!IsEffectivelyEnabled || e.Delta.Y == 0)
        {
            return;
        }

        // Map wheel input to a value change. The value change invalidates by itself.
        CommitValue(Value + (e.Delta.Y > 0 ? Step : -Step));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // Input entry point.
        // Decide what input to accept and establish focus/capture/state.
        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        // Ensure keyboard focus for key handling.
        Focus();

        var part = HitTestButtonPart(e);
        if (part == ButtonPart.None)
        {
            return;
        }

        // Capture guarantees MouseUp delivery. The window may refuse the capture.
        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }

        if (!IsMouseCaptured)
        {
            return;
        }

        // Store hit‑test result as state. SetPressed feeds the Pressed visual state.
        _pressedPart = part;
        SetPressed(true);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // Update hover state for visual feedback.
        var part = HitTestButtonPart(e);
        if (_hoverPart != part)
        {
            _hoverPart = part;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        // Clear hover only when not captured.
        if (_hoverPart != ButtonPart.None && !IsMouseCaptured)
        {
            _hoverPart = ButtonPart.None;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // Input exit point.
        if (e.Button != MouseButton.Left || _pressedPart == ButtonPart.None)
        {
            return;
        }

        var pressedPart = _pressedPart;
        var releasedPart = HitTestButtonPart(e);

        // Releasing the capture clears the press state through OnMewPropertyChanged.
        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }

        ClearPressedPart();

        // Commit action only if release is on the same region.
        if (releasedPart == pressedPart && IsEffectivelyEnabled)
        {
            CommitValue(Value + (pressedPart == ButtonPart.Increment ? Step : -Step));
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Keyboard path is independent from mouse path.
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            CommitValue(Value + Step);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            CommitValue(Value - Step);
            e.Handled = true;
        }
    }

    private void ClearPressedPart()
    {
        if (_pressedPart != ButtonPart.None)
        {
            _pressedPart = ButtonPart.None;
            InvalidateVisual();
        }

        SetPressed(false);
    }

    private Color GetPartBackground(ButtonPart part)
    {
        if (!IsEffectivelyEnabled)
        {
            return Theme.Palette.ButtonDisabledBackground;
        }
        else if (_pressedPart == part)
        {
            return Theme.Palette.ButtonPressedBackground;
        }
        else if (_hoverPart == part)
        {
            return Theme.Palette.ButtonHoverBackground;
        }
        else
        {
            return Theme.Palette.ButtonFace;
        }
    }

    // Centralize chrome width rule.
    private double GetButtonAreaWidth() => Theme.Metrics.BaseControlHeight * 2;

    private void GetPartRects(out Rect inner, out Rect textRect, out Rect decRect, out Rect incRect)
    {
        // Hit‑test and render must share the same geometry. All rects are window coordinates, like Bounds.
        double dpiScale = GetDpi() / 96.0;
        inner = GetSnappedBorderBounds(Bounds).Deflate(GetBorderVisualInset());

        // Split content and chrome areas, and snap sub‑rects to pixels as well.
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = LayoutRounding.SnapBoundsRectToPixels(
            new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height), dpiScale);
        textRect = LayoutRounding.SnapBoundsRectToPixels(
            new Rect(
                inner.X + Padding.Left,
                inner.Y + Padding.Top,
                Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
                Math.Max(0, inner.Height - Padding.VerticalThickness)),
            dpiScale);

        decRect = new Rect(buttonRect.X, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        incRect = new Rect(buttonRect.X + buttonRect.Width / 2, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
    }

    private ButtonPart HitTestButtonPart(MouseEventArgs e)
    {
        // GetPosition is relative to the element; the part rects are window coordinates.
        var local = e.GetPosition(this);
        var point = new Point(Bounds.X + local.X, Bounds.Y + local.Y);

        GetPartRects(out _, out _, out var decRect, out var incRect);
        if (decRect.Contains(point))
        {
            return ButtonPart.Decrement;
        }
        else if (incRect.Contains(point))
        {
            return ButtonPart.Increment;
        }
        else
        {
            return ButtonPart.None;
        }
    }
}
```
