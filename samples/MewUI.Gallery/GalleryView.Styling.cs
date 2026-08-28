using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    // Popup inheritance samples plus StyleSheet scope, type rules, BasedOn and Unset.
    private FrameworkElement StylingPage()
    {
        var contextMenu = new ContextMenu()
            .Item("Cut")
            .Item("Copy")
            .Item("Paste")
            .Separator()
            .Item("Select All");

        return CardGrid(
            Card(
                "Tooltip font isolation",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("The button is 20pt Consolas. Hover it: the tooltip keeps the theme font, not the button's font. A popup no longer inherits the triggering control's font.")
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(ThemeFontSize.Small),
                        new Button()
                            .Content("Hover me (20pt / Consolas)")
                            .FontSize(20)
                            .FontFamily("Consolas")
                            .ToolTip("This tooltip stays in the theme font.")
                            .HorizontalAlignment(HorizontalAlignment.Left)
                    )
            ),

            Card(
                "ContextMenu font isolation",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("Right-click the button. It renders at 22pt, but the context menu stays in the theme font.")
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(ThemeFontSize.Small),
                        new Button()
                            .Content("Right-click me (22pt)")
                            .FontSize(22)
                            .ContextMenu(contextMenu)
                            .HorizontalAlignment(HorizontalAlignment.Left)
                    )
            ),

            Card(
                "Named StyleSheet + Setter.Unset",
                NamedStyleUnsetDemo()
            ),

            Card(
                "Scoped StyleSheet type rule",
                TypeRuleDemo()
            ),

            Card(
                "MenuBar dropdown font",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("Both menu bars are identical. The second sits in a FontSize 16 container. Open the menus: the dropdown follows the ambient font.")
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(ThemeFontSize.Small),
                        new TextBlock().Text("Default (theme font):").FontSize(ThemeFontSize.Small),
                        MenuDemoBar(),
                        new TextBlock().Text("Inside a FontSize 16 container:").FontSize(ThemeFontSize.Small),
                        new Border()
                            .FontSize(16)
                            .Child(MenuDemoBar())
                    )
            )
        );
    }

    private MenuBar MenuDemoBar()
    {
        var fileMenu = new Menu()
            .Item("New")
            .Item("Open")
            .Separator()
            .SubMenu("Export", new Menu()
                .Item("PNG")
                .Item("JPEG"));

        var editMenu = new Menu()
            .Item("Undo")
            .Item("Redo");

        // No fixed Height: the bar auto-sizes to the (inherited) font so the font-size effect shows.
        return new MenuBar()
            .Items(
                new MenuItem("File").Menu(fileMenu),
                new MenuItem("Edit").Menu(editMenu)
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
