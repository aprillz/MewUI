# Style controls and switch themes

Use theme palette values for application colors. Do not hard-code a light background and dark text when the application supports theme switching.

```csharp
var card = new Border()
    .Padding(16)
    .WithTheme((theme, border) =>
    {
        border.Background(theme.Palette.ControlBackground);
        border.BorderBrush(theme.Palette.ControlBorder);
        border.BorderThickness(theme.Metrics.ControlBorderThickness);
        border.CornerRadius(theme.Metrics.ControlCornerRadius);
    })
    .Child(new TextBlock().Text("Theme-aware card"));
```

Configure the initial theme on the application builder:

```csharp
Application
    .Create()
    .UseWin32()
    .UseDirect2D()
    .UseTheme(ThemeVariant.System)
    .BuildMainWindow(BuildMainWindow)
    .Run();
```

Switch it at runtime:

```csharp
var themeButtons = new StackPanel()
    .Horizontal()
    .Spacing(8)
    .Children(
        new Button().Content("System")
            .OnClick(() => Application.Current.SetThemeMode(ThemeVariant.System)),
        new Button().Content("Light")
            .OnClick(() => Application.Current.SetThemeMode(ThemeVariant.Light)),
        new Button().Content("Dark")
            .OnClick(() => Application.Current.SetThemeMode(ThemeVariant.Dark)));
```

## Reusable styles and visual states

Use `Style.DeriveFromDefault<T>` when an application style must retain the package theme's normal control chrome. Add typed setters and `StateTrigger` entries for state-dependent values.

```csharp
var actionStyle = Style.DeriveFromDefault<Button>(
    setters:
    [
        Setter.Create(Control.PaddingProperty, new Thickness(18, 8)),
        Setter.Create(TextElement.FontWeightProperty, FontWeight.Bold),
    ],
    triggers:
    [
        new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters =
            [
                Setter.Create(
                    Control.BackgroundProperty,
                    theme => theme.Palette.ButtonFace),
            ],
        },
        new StateTrigger
        {
            Exclude = VisualStateFlags.Enabled,
            Setters =
            [
                Setter.Create(UIElement.OpacityProperty, 0.55),
            ],
        },
    ]);

var sheet = new StyleSheet();
sheet.Define<Button>(actionStyle);

var scope = new Border()
    .StyleSheet(sheet)
    .Child(new Button().Content("Run"));
```

MewUI resolves application styles through `StyleSheet`: define a type rule for every matching control in a subtree, or define a named style and select it with `.StyleName(name)`. There is no per-control `.Style(style)` fluent method.

Use `StateTrigger` for hot, focused, pressed, checked, active, selected, invalid, and read-only appearance. Do not implement visual-state colors with ad hoc pointer handlers. There is no `DynamicResource`; use theme resolvers, style setters, or `.WithTheme(...)`.
