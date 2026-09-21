using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement MenuPage()
    {
        var contextMenu = new ContextMenu()
            .Item("Cut")
            .Item("Copy")
            .Item("Paste")
            .Separator()
            .Item("Select All");

        return CardGrid(
            MenusCard(),

            Card(
                "ContextMenu",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Right-click me")
                            .ContextMenu(
                                new ContextMenu()
                                    .Item("Copy")
                                    .Item("Paste")
                                    .Separator()
                                    .SubMenu("Transform", new ContextMenu()
                                        .Item("Uppercase")
                                        .Item("Lowercase")
                                        .Separator()
                                        .SubMenu("More", new ContextMenu()
                                            .Item("Trim")
                                            .Item("Normalize")
                                            .Item("Sort"))
                                    )
                                    .SubMenu("View", new ContextMenu()
                                        .Item("Zoom In")
                                        .Item("Zoom Out")
                                        .Item("Reset Zoom")
                                    )
                                    .Separator()
                                    .Item("Disabled", isEnabled: false)
                            ),

                        new Button()
                            .Content("Right-click: opens below")
                            .ContextMenu(
                                new ContextMenu { Placement = MenuPlacement.Below, PlacementOffset = new Point(0, 2) }
                                    .Item("First")
                                    .Item("Second")
                                    .Item("Third")
                            )
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
            ),

            AccessKeyCard()
        );
    }

    private FrameworkElement MenusCard()
    {
        var copyPresentation = new ObservableValue<string>("_Copy");
        var shortcutLog = new TextBlock()
            .FontSize(ThemeFontSize.Small)
            .TextWrapping(TextWrapping.Wrap)
            .Text("Focus the TextBox inside the highlighted scope, then press a shortcut.");

        void OnShortcut(string action) => shortcutLog.Text = $"[{DateTime.Now:HH:mm:ss.fff}] {action}";

        var inputScope = new Border()
            .BorderThickness(2)
            .CornerRadius(6)
            .Padding(8)
            .WithTheme((theme, border) => border.BorderBrush(theme.Palette.Accent));

        var scopeState = new TextBlock().FontSize(ThemeFontSize.Small).Bold();
        scopeState.Bind(
            TextBlock.TextProperty,
            inputScope,
            UIElement.IsFocusWithinProperty,
            active => active
                ? "Gallery local InputMap scope — ACTIVE"
                : "Gallery local InputMap scope — INACTIVE");

        inputScope.Child(
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    scopeState,
                    CreateMenu(window.Commands, inputScope.InputMap, OnShortcut, copyPresentation),
                    new TextBlock()
                        .FontSize(ThemeFontSize.Small)
                        .TextWrapping(TextWrapping.Wrap)
                        .Text("The menu handlers live in Window.Commands. Shortcut gestures live only in this bordered InputMap scope."),
                    new Button()
                        .Content("Toggle Copy presentation")
                        .OnClick(() => copyPresentation.Value =
                            copyPresentation.Value == "_Copy" ? "복사(_C)" : "_Copy"),
                    new TextBox()
                        .Placeholder("Focus here: Ctrl/Cmd + N, S, numpad + or -"),
                    shortcutLog));

        return Card(
                "MenuBar (Command scope vs InputMap scope)",
                new StackPanel()
                    .Width(290)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(ThemeFontSize.Small)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Focus inside the border to activate its local shortcuts. Move focus to NavigationView or another card to leave the scope."),
                        inputScope
                    )
            );
    }

    public static MenuBar CreateMenu(Element commandHost, Action<string> onShortcut)
        => CreateMenu(commandHost.Commands, commandHost.InputMap, onShortcut, copyPresentation: null);

    private static MenuBar CreateMenu(
        CommandScope commands,
        InputMap inputMap,
        Action<string> onShortcut,
        ObservableValue<string>? copyPresentation)
    {
        var p = ModifierKeys.Primary;
        IconTemplate MenuIcon(string name)
        {
            // Looked up when the menu is built rather than captured here, so a late-arriving icon
            // dictionary still reaches it: menus are created when the user opens them.
            return new IconTemplate(size =>
            {
                var all = IconResource.GetAll(Resources.Icons.Value);
                var entry = Array.Find(all, x => x.Name == name);
                var geometry = PathGeometry.Parse(entry?.PathData ?? FALLBACK_ICON);
                geometry.Freeze();

                var icon = new PathShape()
                    .Data(geometry)
                    .Size(size.Dip)
                    .Stretch(Stretch.Uniform);
                icon.Bind(Shape.FillProperty, icon, TextElement.ForegroundProperty,
                    (Color color) => (Brush)new SolidColorBrush(color));
                return icon;
            });
        }

        Command MenuCommand(string id, string text, string message, KeyGesture? gesture = null, IconTemplate? icon = null)
        {
            var command = new Command($"gallery.menu.{id}", text, icon);
            commands.Register(command, () => onShortcut(message));
            if (gesture is KeyGesture keyGesture)
                inputMap.Map(command, keyGesture);
            return command;
        }

        var fileMenu = new Menu()
            .Item(MenuCommand("file.new", "_New", "File > New document created", new KeyGesture(Key.N, p)))
            .Item(MenuCommand("file.open", "_Open...", "File > Open file dialog", new KeyGesture(Key.O, p)))
            .Item(MenuCommand("file.save", "_Save", "File > Document saved", new KeyGesture(Key.S, p)))
            .Item(MenuCommand("file.saveAs", "Save _As...", "File > Save As dialog"))
            .Separator()
            .SubMenu("_Export", new Menu()
                .Item(MenuCommand("file.export.png", "_PNG", "File > Export > PNG format"))
                .Item(MenuCommand("file.export.jpeg", "_JPEG", "File > Export > JPEG format"))
                .SubMenu("_Advanced", new Menu()
                    .Item(MenuCommand("file.export.metadata", "With _metadata", "File > Export > Advanced > Include metadata"))
                    .Item(MenuCommand("file.export.optimized", "_Optimized", "File > Export > Advanced > Optimized output"))
                )
            )
            .Separator()
            .Item(MenuCommand("file.exit", "E_xit", "File > Exit application"));

        var copyCommand = MenuCommand(
            "edit.copy",
            "_Copy",
            "Edit > Copy to clipboard",
            new KeyGesture(Key.C, p),
            MenuIcon("copy_regular"));
        if (copyPresentation != null)
        {
            copyCommand.BindText(copyPresentation);
        }

        var editMenu = new Menu()
            .Item(MenuCommand("edit.undo", "_Undo", "Edit > Undo last action", new KeyGesture(Key.Z, p)))
            .Item(MenuCommand("edit.redo", "_Redo", "Edit > Redo last action", new KeyGesture(Key.Y, p)))
            .Separator()
            .Item(MenuCommand("edit.cut", "Cu_t", "Edit > Cut to clipboard", new KeyGesture(Key.X, p), MenuIcon("cut_regular")))
            .Item(copyCommand)
            .Item(MenuCommand("edit.paste", "_Paste", "Edit > Paste from clipboard", new KeyGesture(Key.V, p), MenuIcon("clipboard_paste_regular")))
            .Separator()
            .SubMenu("_Find", new Menu()
                .Item(MenuCommand("edit.find", "_Find...", "Edit > Find > Open find dialog", new KeyGesture(Key.F, p)))
                .Item(MenuCommand("edit.findNext", "Find _Next", "Edit > Find > Find next occurrence", new KeyGesture(Key.F3)))
                .Item(MenuCommand("edit.replace", "_Replace...", "Edit > Find > Open replace dialog", new KeyGesture(Key.H, p)))
            );

        var viewMenu = new Menu()
            .Item(MenuCommand("view.toggleSidebar", "_Toggle Sidebar", "View > Toggle sidebar visibility"))
            .SubMenu("_Zoom", new Menu()
                .Item(MenuCommand("view.zoomIn", "Zoom _In", "View > Zoom > Zoom in", new KeyGesture(Key.Add, p)))
                .Item(MenuCommand("view.zoomOut", "Zoom _Out", "View > Zoom > Zoom out", new KeyGesture(Key.Subtract, p)))
                .Item(MenuCommand("view.zoomReset", "_Reset", "View > Zoom > Reset to 100%", new KeyGesture(Key.D0, p)))
            );
        var menu = new MenuBar()
                            .Height(28)
                            .Items(
                                new MenuItem("_File").Menu(fileMenu),
                                new MenuItem("_Edit").Menu(editMenu),
                                new MenuItem("_View").Menu(viewMenu)
                            );
        return menu;
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

    private FrameworkElement AccessKeyCard()
    {
        var nameBox = new TextBox().Placeholder("Name").Width(160);

        return Card(
            "AccessKey & Shortcuts",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock().Text("Press Alt to show access key underlines (Windows/Linux).").FontSize(ThemeFontSize.Small),

                    new StackPanel().Horizontal().Spacing(8).Children(
                        new Label().CenterVertical().Text("_Name:").AccessKeyTarget(nameBox),
                        nameBox
                    ),

                    new StackPanel().Horizontal().Spacing(8).Children(
                        new Button().Content("_OK"),
                        new Button().Content("_Cancel")
                    ),

                    new StackPanel().Vertical().Spacing(4).Children(
                        new CheckBox().Content("_Remember me"),
                        new CheckBox().Content("_Auto-save")
                    ),

                    new StackPanel().Vertical().Spacing(4).Children(
                        new RadioButton().Content("_Small").GroupName("size"),
                        new RadioButton().Content("_Medium").GroupName("size"),
                        new RadioButton().Content("_Large").GroupName("size")
                    )
                )
        );
    }
}
