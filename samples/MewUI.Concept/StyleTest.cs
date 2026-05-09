using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

internal static class StyleTest
{
    internal static Window Create()
    {
        var window = new Window()
            .Resizable(850, 700)
            .Title("Style System Test");

        BuildContent(window);
        return window;
    }

    private static void BuildContent(Window window)
    {
        var root = new StackPanel().Vertical().Spacing(16).Padding(16);

        // 1. Theme default
        root.Add(Section("1. Theme Default",
            new Button().Content("Default Button"),
            new CheckBox().Content("Default CheckBox"),
            new TextBlock { Text = "Default TextBlock (inherits Foreground from Window)" }
        ));

        // 2. StyleSheet type rule — flat button
        var flatButtonStyle = CreateFlatButtonStyle();

        var scopePanel = new StackPanel().Vertical().Spacing(8);
        scopePanel.StyleSheet = new StyleSheet();
        scopePanel.StyleSheet.Define<Button>(flatButtonStyle);
        scopePanel.Add(new Button().Content("Flat Button (via StyleSheet type rule)"));
        scopePanel.Add(new Button().Content("Another Flat Button"));
        scopePanel.Add(new CheckBox().Content("CheckBox (unaffected by Button rule)"));

        root.Add(Section("2. StyleSheet Type Rule — Flat Button", scopePanel));

        // 3. StyleName + StyleSheet
        var accentButtonStyle = CreateAccentButtonStyle();

        var sheetPanel = new StackPanel().Vertical().Spacing(8);
        sheetPanel.StyleSheet = new StyleSheet();
        sheetPanel.StyleSheet.Define("accent-button", accentButtonStyle);

        var namedBtn = new Button().Content("Accent Button (StyleName)");
        namedBtn.StyleName = "accent-button";
        sheetPanel.Add(namedBtn);
        sheetPanel.Add(new Button().Content("Normal Button (no StyleName)"));

        root.Add(Section("3. StyleName + StyleSheet", sheetPanel));

        // 4. Disabled — Foreground inheritance
        var disabledPanel = new StackPanel().Vertical().Spacing(8);
        disabledPanel.IsEnabled = false;
        disabledPanel.Add(new Button().Content("Disabled Button (gray text)"));
        disabledPanel.Add(new TextBlock { Text = "TextBlock in disabled container (should NOT be gray)" });
        disabledPanel.Add(new CheckBox().Content("Disabled CheckBox (gray text)"));

        root.Add(Section("4. Disabled — Foreground Inheritance", disabledPanel));

        // 5. Local value priority
        var localBtn = new Button().Content("Local Background=Red (hover should NOT change bg)");
        localBtn.Background = Color.FromRgb(200, 60, 60);
        localBtn.Foreground = Color.White;

        root.Add(Section("5. Local Value Priority",
            localBtn,
            new Button().Content("Normal Button (hover works)")
        ));

        // 6. Nested StyleSheet type rules
        var outerPanel = new StackPanel().Vertical().Spacing(8);
        outerPanel.StyleSheet = new StyleSheet();
        outerPanel.StyleSheet.Define<Button>(flatButtonStyle);

        var innerBorder = new Border { Padding = new Thickness(8) };
        innerBorder.WithTheme((t, c) => c.Background = t.Palette.ContainerBackground);
        var innerStack = new StackPanel().Vertical().Spacing(4);
        innerStack.StyleSheet = new StyleSheet();
        innerStack.StyleSheet.Define<Button>(accentButtonStyle);
        innerStack.Add(new Button().Content("Inner: Accent Button"));
        innerStack.Add(new TextBlock { Text = "Inner type rule overrides outer" });
        innerBorder.Child = innerStack;

        outerPanel.Add(new Button().Content("Outer: Flat Button"));
        outerPanel.Add(innerBorder);

        root.Add(Section("6. Nested StyleSheet Type Rules", outerPanel));

        // 7. Theme switch
        var themePanel = new StackPanel().Horizontal().Spacing(8);
        themePanel.Add(new Button().Content("Light").OnClick(() => Application.Current!.SetTheme(ThemeVariant.Light)));
        themePanel.Add(new Button().Content("Dark").OnClick(() => Application.Current!.SetTheme(ThemeVariant.Dark)));
        themePanel.Add(new Button().Content("System").OnClick(() => Application.Current!.SetTheme(ThemeVariant.System)));
        themePanel.Add(new TextBlock { Text = "  ← Switch theme and verify all sections update" }
            .CenterVertical());

        root.Add(Section("7. Theme Switch", themePanel));

        window.Content = new ScrollViewer { Content = root };
    }

    private static Style CreateFlatButtonStyle()
    {
        return new Style(typeof(Button))
        {
            BasedOn = Style.ForType<Button>(),
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t=>t.Palette.ButtonHoverBackground.WithAlpha(0)),
                Setter.Create(Control.BorderBrushProperty, Color.Transparent),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t  => t.Palette.ButtonHoverBackground),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.ForegroundProperty, t  => t.Palette.DisabledText),
                    ],
                },
            ],
        };
    }

    private static Style CreateAccentButtonStyle()
    {
        return new Style(typeof(Button))
        {
            BasedOn = Style.ForType<Button>(),
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t  => t.Palette.Accent),
                Setter.Create(Control.ForegroundProperty, t  => t.Palette.AccentText),
                Setter.Create(Control.BorderBrushProperty, t  => t.Palette.Accent),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t  => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15)),
                        Setter.Create(Control.BorderBrushProperty, t  => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t  => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25)),
                        Setter.Create(Control.BorderBrushProperty, t  => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25)),
                    ],
                },
            ],
        };
    }

    private static FrameworkElement Section(string title, params FrameworkElement[] children)
    {
        var panel = new StackPanel().Vertical().Spacing(4);
        panel.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        });

        var border = new Border { Padding = new Thickness(12, 8) };
        border.WithTheme((t, c) =>
        {
            c.BorderBrush = t.Palette.ControlBorder;
            c.BorderThickness = 1;
            c.CornerRadius = t.Metrics.ControlCornerRadius;
        });

        var inner = new StackPanel().Vertical().Spacing(8);
        foreach (var child in children)
            inner.Add(child);

        border.Child = inner;
        panel.Add(border);
        return panel;
    }
}
