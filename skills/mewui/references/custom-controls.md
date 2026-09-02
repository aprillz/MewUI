# Create an application control

Prefer composition first. Derive a control only when the application needs reusable state, behavior, layout, or drawing that composition cannot express cleanly.

Choose a public base type:

- `FrameworkElement`: layout-aware element without standard control chrome
- `Control`: styled control with common visual properties
- `ContentControl`: one content value
- `Panel`: multiple layout children

## Bindable content control

```csharp
sealed class StatusBadge : ContentControl
{
    public static readonly MewProperty<string> StatusProperty =
        MewProperty<string>.Register<StatusBadge>(
            nameof(Status),
            string.Empty,
            MewPropertyOptions.AffectsLayout,
            static (badge, _, _) => badge.InvalidateEffectiveContent());

    public string Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    protected override Element SelectEffectiveContent()
        => new Border()
            .Padding(10, 5)
            .WithTheme((theme, border) =>
            {
                border.Background(
                    theme.Palette.Accent.Lerp(theme.Palette.WindowBackground, 0.82));
                border.BorderBrush(theme.Palette.Accent);
                border.BorderThickness(theme.Metrics.ControlBorderThickness);
                border.CornerRadius(theme.Metrics.ControlCornerRadius);
            })
            .Child(new TextBlock().Text(Status).Bold());
}

static class StatusBadgeExtensions
{
    public static StatusBadge BindStatus(
        this StatusBadge badge,
        ObservableValue<string> source)
        => badge.Bind(StatusBadge.StatusProperty, source);
}
```

Use it with normal application state:

```csharp
var status = new ObservableValue<string>("Ready");
var badge = new StatusBadge().BindStatus(status);
```

Select only property flags that match actual effects: `AffectsRender`, `AffectsLayout`, `AffectsVisualState`, `Inherits`, or `BindsTwoWayByDefault`. Use `RegisterReadOnly` with a private key for externally read-only state.

## Custom layout and drawing

The public layout hooks are `MeasureOverride(Size availableSize)` and `ArrangeOverride(Size finalSize)`. A parent measures each child, returns its desired size, and arranges each child with a final rectangle. Derive from `Panel` when owning multiple layout children.

For a drawing-only element, override `OnRender(IGraphicsContext)` using backend-neutral graphics APIs:

```csharp
using Aprillz.MewUI.Rendering;

sealed class StatusLight : FrameworkElement
{
    public Color LightColor { get; set; } = Color.Green;

    protected override Size MeasureOverride(Size availableSize)
        => new(16, 16);

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);
        context.FillEllipse(Bounds, LightColor);
    }
}
```

Call `InvalidateMeasure` after state changes that affect desired size and `InvalidateVisual` after changes that affect drawing only. Make `LightColor` a `MewProperty<Color>` when callers need styling or binding. Do not call Direct2D, GDI, OpenGL, Metal, or other backend-specific APIs from a reusable application control.
