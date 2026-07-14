using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    // Samples tied to the styling work: issue #198 (popup font isolation) and the
    // Setter.Unset primitive (subtractive BasedOn).
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
                            .FontSize(11),
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
                            .FontSize(11),
                        new Button()
                            .Content("Right-click me (22pt)")
                            .FontSize(22)
                            .ContextMenu(contextMenu)
                            .HorizontalAlignment(HorizontalAlignment.Left)
                    )
            ),

            Card(
                "BasedOn + Setter.Unset",
                UnsetDemo()
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
                            .FontSize(11),
                        new TextBlock().Text("Default (theme font):").FontSize(11),
                        MenuDemoBar(),
                        new TextBlock().Text("Inside a FontSize 16 container:").FontSize(11),
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

    private FrameworkElement UnsetDemo()
    {
        var ambient = Color.FromRgb(40, 170, 90);
        var pinnedForeground = Color.FromRgb(210, 60, 60);

        // Base style pins a red foreground on top of the default Button chrome.
        var pinnedStyle = new Style(typeof(Button))
        {
            BasedOn = Style.ForType<Button>(),
            Setters = [Setter.Create(TextElement.ForegroundProperty, pinnedForeground)],
        };

        // Derived style keeps the chrome from BasedOn but unsets the foreground,
        // so it reverts to the inherited (ambient) value.
        var unsetStyle = new Style(typeof(Button))
        {
            BasedOn = pinnedStyle,
            Setters = [Setter.Unset(TextElement.ForegroundProperty)],
        };

        var sheet = new StyleSheet();
        sheet.Define("fg-pinned", () => pinnedStyle);
        sheet.Define("fg-unset", () => unsetStyle);

        // The Border (a Control) provides the ambient Foreground that descendants inherit,
        // and hosts the StyleSheet the named styles resolve against.
        return new Border()
            .Foreground(ambient)
            .Apply(b => b.StyleSheet = sheet)
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("Container Foreground is green. Both buttons derive (BasedOn) from a style that sets red text.")
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(11),
                        new Button()
                            .Content("BasedOn (red text)")
                            .StyleName("fg-pinned")
                            .HorizontalAlignment(HorizontalAlignment.Left),
                        new Button()
                            .Content("BasedOn + Unset (follows ambient)")
                            .StyleName("fg-unset")
                            .HorizontalAlignment(HorizontalAlignment.Left)
                    )
            );
    }
}
