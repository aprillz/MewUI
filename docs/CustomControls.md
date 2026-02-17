# Custom Controls

## Overview

- This is a **developer reference** for building custom controls in MewUI.
- Layout uses **DIP**, rendering requires **pixel‑aligned geometry**.
- The sample below is a complete `NumericUpDown`, and the comments explain **what each spot is responsible for** from a CustomControl perspective.

---

## Detailed Explanation

### <a id="scope"></a>Scope and Conventions

- Sizes are computed in **DIP**, rendering is **pixel‑snapped**.
- Measure/Arrange must operate in **logical coordinates (DIP)** only; pixel snapping is applied in Render.
- Use `GetDpi()` / `context.DpiScale` to respond to DPI changes.
- **Never do pixel math during Measure.** Mixing pixel snapping into Measure causes layout mismatches.

### <a id="measure"></a>Size Calculation (MeasureContent)

- `MeasureContent` is the **single source of desired size**.
- This stage only computes how much space the control needs; it does not decide placement.
- Text is measured using the display string (format applied).
- The final size includes `Padding`, chrome (button area), and `GetBorderVisualInset()`.
- If the control should align with the theme’s baseline size, set `DefaultMinHeight` to `Theme.Metrics.BaseControlHeight`.
- In that case, `MeasureContent` can return the natural content height; the framework applies `MinHeight`.
- `Format` and `Value` changes can alter text width, so **measure invalidation** is required.
- Even with caching, **invalidate when inputs change** (font, DPI, string, wrapping policy).

Example:
```csharp
protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

protected override Size MeasureContent(Size available)
{
    var textHeight = /* measure text height */;
    double height = textHeight + Padding.VerticalThickness;
    return new Size(Width, height);
}
```

### <a id="arrange"></a>Internal Layout (ArrangeContent)

- This sample does not override `ArrangeContent`; it computes internal layout using the final `Bounds`.
- Controls with children must compute child rects here and call `Arrange` for each child.
- Arrange defines **where** children go inside the allotted space.
- Always assume **DesiredSize from Measure** and **actual Bounds** can differ.

### <a id="render"></a>Rendering (OnRender)

- `GetSnappedBorderBounds` and `LayoutRounding.SnapBoundsRectToPixels` ensure pixel alignment.
- Render order: **background → border → content**.
- Structure code so layout math and rendering share the same rects.
- Render **must not** recompute measurement; it only consumes the final `Bounds`.

### <a id="state"></a>State and Input

- Interaction state (hover/pressed) is **internal state** of the control.
- Capture on MouseDown and release on MouseUp to guarantee input consistency.
- Hit‑test logic must use **the same split geometry** as rendering.
- When state changes, choose **InvalidateVisual** vs **InvalidateMeasure** correctly.
- Gate input using **`IsEffectivelyEnabled`**, not `IsEnabled`.
  - If a parent is disabled, a child with `IsEnabled == true` must still ignore input.
  - For that reason, input handling, visual state, and color decisions should follow `IsEffectivelyEnabled`.

### <a id="theme"></a>Theme and Metrics

- Colors and sizes come from `Theme.Palette.*` and `Theme.Metrics.*`.
- Theme changes invalidate text measurement caches.
- Theme changes can affect **fonts, sizes, and padding rules**, so re‑measuring is safer.

### <a id="utils"></a>Utility Methods (State, Border, and DIP)

- `GetDpi()` returns the effective DPI (`uint`). Use `dpiScale = GetDpi() / 96.0` when converting DIPs to device pixels.
- `GetVisualState(...)` creates a stable snapshot of `enabled/hot/focused/pressed/active` for the current frame.
- `PickAccentBorder(theme, baseBorder, state, hoverMix)` maps that state to a border color (accent on focused/pressed/active; tinted on hover).
- `DrawBackgroundAndBorder(context, bounds, background, borderBrush, cornerRadiusDip)` draws a consistent background + border using the current backend.
- `GetBorderRenderMetrics(bounds, cornerRadiusDip)` returns pixel-snapped border thickness and corner radius so rendering matches layout.
- `LayoutRounding` helpers keep geometry stable across fractional DPI and avoid 1px clipping artifacts:
- `LayoutRounding.SnapBoundsRectToPixels(...)` for background/border/layout boxes.
- `LayoutRounding.SnapViewportRectToPixels(...)` for viewports and clip rectangles (won’t shrink).
- `LayoutRounding.SnapThicknessToPixels(...)` for border thickness that must be whole pixels.
- `LayoutRounding.ExpandClipByDevicePixels(...)` for clip rects that must include the last pixel row/col.

Example: state-driven border + pixel snapping

```csharp
var dpiScale = GetDpi() / 96.0;
var state = GetVisualState(isPressed: isPressed, isActive: isActive);
var border = PickAccentBorder(Theme, BorderBrush, state, hoverMix: 0.6);

var bounds = LayoutRounding.SnapBoundsRectToPixels(Bounds, dpiScale);
DrawBackgroundAndBorder(context, bounds, Background, border, cornerRadiusDip: 0);
```

### <a id="invalidate"></a>Invalidation Rules

- `Format` change: `InvalidateMeasure()` + `InvalidateVisual()`
- `Value` change: text width may change → `InvalidateMeasure()`
- Hover/Pressed change: `InvalidateVisual()`

---

## Full Sample Code

https://github.com/user-attachments/assets/340e24a6-5b39-4d77-a857-69811e857133

```csharp
public sealed class NumericUpDown : RangeBase
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

    // Display format can affect desired size.
    // In a custom control, any state that changes the rendered text
    // must be treated as layout‑affecting.
    private string _format = "0.##";

    // Interaction step does not affect layout but is required for input logic.
    // Keep it as state used by interaction handlers.
    private double _step = 1;

    // Cache text measurement to reduce Measure cost.
    // Measure can be called often; caching stabilizes layout.
    private TextMeasureCache _measureCache;

    // Visual states; they affect rendering but not layout.
    // Keep them internal and drive invalidation accordingly.
    private ButtonPart _hoverPart;
    private ButtonPart _pressedPart;

    public NumericUpDown()
    {
        // Establish default size rules.
        // Border participates in Measure; set a safe default.
        BorderThickness = 1;

        // Set default range.
        Maximum = 100;

        // Separate content from chrome.
        // Custom controls should keep content and chrome areas distinct.
        Padding = new Thickness(8, 4, 8, 4);
    }

    // Provide theme defaults for consistent styling.
    protected override Color DefaultBackground => Theme.Palette.ControlBackground;
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;
    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    // Controls that accept keyboard input must be focusable.
    public override bool Focusable => true;

    public string Format
    {
        get => _format;
        set
        {
            // Avoid redundant invalidation.
            if (_format == value)
            {
                return;
            }

            // Apply display change.
            _format = value;

            // Text metrics are now stale.
            _measureCache.Invalidate();

            // Desired size may change.
            InvalidateMeasure();

            // Visual update is required.
            InvalidateVisual();
        }
    }

    public double Step
    {
        get => _step;
        set
        {
            if (_step.Equals(value))
            {
                return;
            }

            // Update interaction step.
            _step = value;

            // No invalidation when it doesn't affect layout or visuals.
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // Theme changes can alter text metrics, so invalidate cache.
        _measureCache.Invalidate();
    }

    protected override void OnValueChanged(double value, bool fromUser)
    {
        // Displayed text may change width → re‑measure.
        _measureCache.Invalidate();
        InvalidateMeasure();

        // Visual invalidation can be added if needed.
    }

    protected override Size MeasureContent(Size available)
    {
        // MeasureContent defines the desired size of a custom control.
        // Compute in DIP only; pixel snapping belongs to Render.
        var factory = GetGraphicsFactory();
        var font = GetFont(factory);

        // Measure the actual display string.
        string text = Value.ToString(_format);
        var textSize = _measureCache.Measure(factory, GetDpi(), font, text, TextWrapping.NoWrap, 0);

        // Include chrome area in size.
        double buttonAreaWidth = GetButtonAreaWidth();

        // Content + padding + chrome.
        double width = textSize.Width + Padding.HorizontalThickness + buttonAreaWidth;

        // Use natural content height; MinHeight enforces the baseline size.
        double height = textSize.Height + Padding.VerticalThickness;

        // Include border inset in desired size.
        return new Size(width, height).Inflate(new Thickness(GetBorderVisualInset()));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Render runs after final Bounds are known.
        // Use pixel‑snapped geometry to avoid jitter.
        var bounds = GetSnappedBorderBounds(Bounds);

        // Style values come from the theme.
        double radius = Theme.Metrics.ControlCornerRadius;

        // Resolve state‑dependent colors before drawing.
        bool isEnabled = IsEffectivelyEnabled;
        Color bg = isEnabled ? Background : Theme.Palette.DisabledControlBackground;
        Color baseBorder = isEnabled ? BorderBrush : Theme.Palette.ControlBorder;
        var state = GetVisualState(isPressed: _pressedPart != ButtonPart.None, isActive: _pressedPart != ButtonPart.None);
        Color border = PickAccentBorder(Theme, baseBorder, state, hoverMix: 0.6);

        // Draw chrome first.
        DrawBackgroundAndBorder(context, bounds, bg, border, radius);

        // Compute internal layout from final bounds.
        var inner = bounds.Deflate(new Thickness(GetBorderVisualInset()));

        // Split content and chrome areas.
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height);
        var textRect = new Rect(
            inner.X + Padding.Left,
            inner.Y + Padding.Top,
            Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
            Math.Max(0, inner.Height - Padding.VerticalThickness));

        // Snap sub‑rects to pixels as well.
        textRect = LayoutRounding.SnapBoundsRectToPixels(textRect, context.DpiScale);
        buttonRect = LayoutRounding.SnapBoundsRectToPixels(buttonRect, context.DpiScale);

        // Input and rendering must share the same split geometry.
        var decRect = new Rect(buttonRect.X, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        var incRect = new Rect(buttonRect.X + buttonRect.Width / 2, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);

        // Resolve per‑region colors from state.
        Color baseButton = Theme.Palette.ButtonFace;
        Color hoverButton = Theme.Palette.ButtonHoverBackground;
        Color pressedButton = Theme.Palette.ButtonPressedBackground;
        Color disabledButton = Theme.Palette.ButtonDisabledBackground;

        Color decBg = !isEnabled
            ? disabledButton
            : _pressedPart == ButtonPart.Decrement ? pressedButton
            : _hoverPart == ButtonPart.Decrement ? hoverButton
            : baseButton;

        Color incBg = !isEnabled
            ? disabledButton
            : _pressedPart == ButtonPart.Increment ? pressedButton
            : _hoverPart == ButtonPart.Increment ? hoverButton
            : baseButton;

        if (buttonRect.Width > 0)
        {
            // Draw chrome area.
            context.FillRectangle(decRect, decBg);

            var innerRadius = Math.Max(0, radius - GetBorderVisualInset());
            context.Save();
            context.SetClipRoundedRect(
                LayoutRounding.MakeClipRect(inner, context.DpiScale, rightPx: 0, bottomPx: 0),
                innerRadius,
                innerRadius);
            context.FillRectangle(incRect, incBg);
            context.Restore();

            // Visual separators for clarity.
            var x = decRect.Right;
            context.DrawLine(new Point(x, decRect.Y + 2), new Point(x, decRect.Bottom - 2), Theme.Palette.ControlBorder, 1);

            x = decRect.Left;
            context.DrawLine(new Point(x, decRect.Y), new Point(x, decRect.Bottom), Theme.Palette.ControlBorder, 1);
        }

        // Draw text last to sit above chrome.
        var font = GetFont();
        var textColor = isEnabled ? Foreground : Theme.Palette.DisabledText;
        context.DrawText(Value.ToString(_format), textRect, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

        if (buttonRect.Width > 0)
        {
            // Glyph sizes follow theme metrics.
            var chevronSize = Theme.Metrics.BaseControlHeight / 6;
            ChevronGlyph.Draw(context, decRect.Center, chevronSize, textColor, ChevronDirection.Down);
            ChevronGlyph.Draw(context, incRect.Center, chevronSize, textColor, ChevronDirection.Up);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // Block input when disabled.
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        // Map wheel input to a value change.
        double delta = e.Delta > 0 ? _step : -_step;
        Value += delta;

        // Value change must reflect visually.
        InvalidateVisual();
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

        // Store hit‑test result as state.
        var part = HitTestButtonPart(e.Position);
        if (part == ButtonPart.None)
        {
            return;
        }

        _pressedPart = part;

        // Capture guarantees MouseUp delivery.
        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // Update hover state for visual feedback.
        var part = HitTestButtonPart(e.Position);
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
        // Release capture and normalize state here.
        if (e.Button != MouseButton.Left || _pressedPart == ButtonPart.None)
        {
            return;
        }

        // Release capture.
        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        // Commit action only if release is on the same region.
        var releasedPart = HitTestButtonPart(e.Position);
        if (releasedPart == _pressedPart && IsEffectivelyEnabled)
        {
            Value += _pressedPart == ButtonPart.Increment ? _step : -_step;
        }

        _pressedPart = ButtonPart.None;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Keyboard path is independent from mouse path.
        // Check focus/enable state, then invalidate as needed.
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            Value += _step;
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Value -= _step;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    // Centralize chrome width rule.
    private double GetButtonAreaWidth() => Theme.Metrics.BaseControlHeight * 2;

    private (Rect decRect, Rect incRect) GetButtonRects()
    {
        // Hit‑test and render must share the same geometry.
        var inner = GetSnappedBorderBounds(Bounds).Deflate(new Thickness(GetBorderVisualInset()));
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height);
        var decRect = new Rect(buttonRect.X, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        var incRect = new Rect(buttonRect.X + buttonRect.Width / 2, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        return (decRect, incRect);
    }

    private ButtonPart HitTestButtonPart(Point position)
    {
        // Reuse the same hit‑test logic across input handlers.
        var (decRect, incRect) = GetButtonRects();
        if (decRect.Contains(position))
        {
            return ButtonPart.Decrement;
        }
        if (incRect.Contains(position))
        {
            return ButtonPart.Increment;
        }
        return ButtonPart.None;
    }
}
```
