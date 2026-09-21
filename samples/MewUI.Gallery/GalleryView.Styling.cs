using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    // Popup inheritance samples plus StyleSheet scope, type rules, BasedOn and Unset.
    private FrameworkElement StylingPage()
    {
        return CardGrid(
            Card(
                "Named StyleSheet + Setter.Unset",
                NamedStyleUnsetDemo()
            ),

            Card(
                "Scoped StyleSheet type rule",
                TypeRuleDemo()
            )
        );
    }

    private FrameworkElement NamedStyleUnsetDemo()
    {
        // This style explicitly extends the default Button chrome and contributes a
        // Foreground candidate at the Style tier.
        var pinnedStyle = Style.DeriveFromDefault<Button>(
            setters: [Setter.Create(TextElement.ForegroundProperty, t => t.Palette.Error)]);

        // Omitting Foreground does not cancel BasedOn: the Error candidate remains.
        var noOverrideStyle = new Style(typeof(Button)) { BasedOn = pinnedStyle };

        // Unset removes the final Style candidate for Foreground. With no higher-priority
        // Local/ElementTrigger/Binding value, the inherited container value is revealed.
        var unsetStyle = new Style(typeof(Button))
        {
            BasedOn = pinnedStyle,
            Setters = [Setter.Unset(TextElement.ForegroundProperty)],
        };

        var sheet = new StyleSheet();
        sheet.Define("derived-no-override", () => noOverrideStyle);
        sheet.Define("derived-unset", () => unsetStyle);

        // The Border provides both the nearest named-style scope and the inherited candidate.
        return new Border()
            .WithTheme((t, b) => b.Foreground(t.Palette.Accent))
            .StyleSheet(sheet)
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("This container owns a local StyleSheet and provides an Accent Foreground. Both named styles derive from a default Button style that contributes an Error Foreground.")
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(ThemeFontSize.Small),
                        new Button()
                            .Content("No override: BasedOn Error wins")
                            .StyleName("derived-no-override")
                            .HorizontalAlignment(HorizontalAlignment.Left),
                        new Button()
                            .Content("Unset: inherited Accent is revealed")
                            .StyleName("derived-unset")
                            .HorizontalAlignment(HorizontalAlignment.Left),
                        new TextBlock()
                            .Text("Unset does not assign Accent. It removes the Style candidate, so the resolver exposes the next source in the property precedence chain.")
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(ThemeFontSize.Small)
                    )
            );
    }

    private FrameworkElement TypeRuleDemo()
    {
        var sheet = new StyleSheet();
        sheet.Define<Button>(Style.DeriveFromDefault<Button>(
            setters:
            [
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.PaddingProperty, new Thickness(18, 8, 18, 8)),
                Setter.Create(TextElement.FontWeightProperty, FontWeight.Bold),
            ]));

        Border scope = null!;
        var status = new TextBlock()
            .Text("StyleSheet: applied")
            .FontSize(ThemeFontSize.Small);

        scope = new Border()
            .StyleSheet(sheet)
            .Child(
                new Button()
                    .Content("Inside scope: Define<Button>")
                    .HorizontalAlignment(HorizontalAlignment.Left)
            );

        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new TextBlock()
                    .Text("The first button is outside the local sheet. The second is inside a Border that owns a Button type rule, so it receives the square, padded, bold style without a StyleName. Removing the sheet safely returns it to the default Button style.")
                    .TextWrapping(TextWrapping.Wrap)
                    .FontSize(ThemeFontSize.Small),
                new Button()
                    .Content("Outside scope: default Button")
                    .HorizontalAlignment(HorizontalAlignment.Left),
                scope,
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Remove StyleSheet")
                            .OnClick(() =>
                            {
                                scope.StyleSheet = null;
                                status.Text = "StyleSheet: removed (inside button uses the default style)";
                            }),
                        new Button()
                            .Content("Apply StyleSheet")
                            .OnClick(() =>
                            {
                                scope.StyleSheet = sheet;
                                status.Text = "StyleSheet: applied";
                            })
                    ),
                status
            );
    }
}
