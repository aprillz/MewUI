using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement WindowsMenuPage()
    {
        var dialogStatus = new ObservableValue<string>("Dialog: -");
        var transparentStatus = new ObservableValue<string>("Transparent: -");
        var manualPositionStatus = new ObservableValue<string>("Manual: -");
        var openFilesStatus = new ObservableValue<string>("Open Files: -");
        var saveFileStatus = new ObservableValue<string>("Save File: -");
        var folderStatus = new ObservableValue<string>("Select Folder: -");

        async void ShowDialogSample()
        {
            dialogStatus.Value = "Dialog: opening...";

            var dlg = new Window()
                .Resizable(420, 220)
                .StartCenterScreen()
                .OnBuild(x => x
                    .Title("ShowDialog sample")
                    .Padding(16)
                    .Content(
                        new StackPanel()
                            .Vertical()
                            .Spacing(10)
                            .Children(
                                new Label()
                                    .Text("This is a modal window. The owner is disabled until you close this dialog."),

                                new StackPanel()
                                    .Horizontal()
                                    .Spacing(8)
                                    .Children(
                                        new Button()
                                            .Content("Open dialog")
                                            .OnClick(ShowDialogSample),
                                        new Button()
                                            .Content("Close")
                                            .OnClick(() => x.Close())
                                    )
                            )
                    )
                );

            try
            {
                await dlg.ShowDialogAsync(window);
                dialogStatus.Value = "Dialog: closed";
            }
            catch (Exception ex)
            {
                dialogStatus.Value = $"Dialog: error ({ex.GetType().Name})";
            }
        }

        void ShowTransparentSample()
        {
            transparentStatus.Value = "Transparent: opening...";

            Window tw = null!;

            new Window()
                .Ref(out tw)
                .FitContentHeight(520)
                .Background(Color.Pink.WithAlpha(64))
                .StartCenterOwner()
                .OnBuild(x =>
                {
                    x.Title = "Transparent window sample";
                    x.AllowsTransparency = true;
                    x.Padding = new Thickness(20);
                    x.Content =
                            new DockPanel()
                                .Children(
                                    new Border()
                                        .DockBottom()
                                        .Background(Color.Green.WithAlpha(64))
                                        .Child(
                                            new Image()
                                                .Source(logo)
                                                .Apply(x => EnableWindowDrag(tw, x))
                                                .Width(500)
                                                .Height(128)
                                                .ImageScaleQuality(ImageScaleQuality.HighQuality)
                                                .StretchMode(Stretch.Uniform)),
                                    new Border()
                                        .Padding(16)
                                        .Top()
                                        .WithTheme((t, b) => b.Background(t.Palette.Accent.WithAlpha(32)))
                                        .CornerRadius(10)
                                        .Child(
                                            new StackPanel()
                                                .Vertical()
                                                .Spacing(10)
                                                .Children(

                                                    new StackPanel()
                                                        .Vertical()
                                                        .Spacing(6)
                                                        .Children(
                                                            new Label()
                                                                .TextWrapping(TextWrapping.Wrap)
                                                                .Text("Wrapped label followed by a button. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                                                            new Button()
                                                                .Content("Close")
                                                                .OnClick(() => x.Close())
                                                            )
                                                )
                                        )
                            );
                });

            try
            {
                tw.Show(window);
                transparentStatus.Value = "Transparent: shown";
            }
            catch (Exception ex)
            {
                transparentStatus.Value = $"Transparent: error ({ex.GetType().Name})";
            }
        }

        void ShowManualPositionSample()
        {
            const double left = 120;
            const double top = 140;

            manualPositionStatus.Value = $"Manual: opening at ({left}, {top})";

            Window manual = null!;

            new Window()
                .Ref(out manual)
                .Resizable(360, 180)
                .StartManualPosition(left, top)
                .OnBuild(x => x
                    .Title("StartupManualPosition sample")
                    .Padding(16)
                    .Content(
                        new StackPanel()
                            .Vertical()
                            .Spacing(10)
                            .Children(
                                new Label()
                                    .Text($"StartupLocation.Manual\nLeft: {left}\nTop: {top}"),
                                new Label()
                                    .FontSize(11)
                                    .Text("Use this sample to verify startup manual placement against the requested DIP coordinates."),
                                new Button()
                                    .Content("Close")
                                    .OnClick(() => x.Close())
                            )
                    )
                );

            try
            {
                manual.Show();
                manualPositionStatus.Value = $"Manual: shown at requested ({left}, {top})";
            }
            catch (Exception ex)
            {
                manualPositionStatus.Value = $"Manual: error ({ex.GetType().Name})";
            }
        }

        return CardGrid(
            MenusCard(),

            Card(
                "ShowDialogAsync",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Open dialog")
                            .OnClick(ShowDialogSample),
                        new Label()
                            .BindText(dialogStatus)
                            .FontSize(11)
                    )
            ),

            Card(
                "Transparent Window",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Open transparent window")
                            .OnClick(ShowTransparentSample),
                        new Label()
                            .BindText(transparentStatus)
                            .FontSize(11)
                    )
            ),

            Card(
                "StartupManualPosition",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Label()
                            .FontSize(11)
                            .Text("Opens a window with StartManualPosition(120, 140)."),
                        new Button()
                            .Content("Open manual-position window")
                            .OnClick(ShowManualPositionSample),
                        new Label()
                            .BindText(manualPositionStatus)
                            .FontSize(11)
                    )
            ),

            Card(
                "File Dialogs",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new WrapPanel()
                            .Spacing(6)
                            .Children(
                                new Button()
                                    .Content("Open Files...")
                                    .OnClick(() =>
                                    {
                                        var files = FileDialog.OpenFiles(new OpenFileDialogOptions
                                        {
                                            Owner = window.Handle,
                                            Filter = "All Files (*.*)|*.*"
                                        });

                                        if (files is null || files.Length == 0)
                                        {
                                            openFilesStatus.Value = "Open Files: canceled";
                                        }
                                        else if (files.Length == 1)
                                        {
                                            openFilesStatus.Value = $"Open Files: {files[0]}";
                                        }
                                        else
                                        {
                                            openFilesStatus.Value = $"Open Files: {files.Length} files";
                                        }
                                    }),
                                new Button()
                                    .Content("Save File...")
                                    .OnClick(() =>
                                    {
                                        var file = FileDialog.SaveFile(new SaveFileDialogOptions
                                        {
                                            Owner = window.Handle,
                                            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                                            FileName = "demo.txt"
                                        });
                                        saveFileStatus.Value = file is null ? "Save File: canceled" : $"Save File: {file}";
                                    }),
                                new Button()
                                    .Content("Select Folder...")
                                    .OnClick(() =>
                                    {
                                        var folder = FileDialog.SelectFolder(new FolderDialogOptions
                                        {
                                            Owner = window.Handle
                                        });
                                        folderStatus.Value = folder is null ? "Select Folder: canceled" : $"Select Folder: {folder}";
                                    })
                            ),

                        new Label()
                            .BindText(openFilesStatus)
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap),

                        new Label()
                            .BindText(saveFileStatus)
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap),

                        new Label()
                            .BindText(folderStatus)
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                    )
            ),

            PromptDialogCard(),

            DevToolsCard()
        );
    }


    private FrameworkElement MenusCard()
    {
        var fileMenu = new Menu()
            .Item("New", shortcutText: "Ctrl+N")
            .Item("Open...", shortcutText: "Ctrl+O")
            .Item("Save", shortcutText: "Ctrl+S")
            .Item("Save As...")
            .Separator()
            .SubMenu("Export", new Menu()
                .Item("PNG")
                .Item("JPEG")
                .SubMenu("Advanced", new Menu()
                    .Item("With metadata")
                    .Item("Optimized")
                )
            )
            .Separator()
            .Item("Exit");

        var editMenu = new Menu()
            .Item("Undo", shortcutText: "Ctrl+Z")
            .Item("Redo", shortcutText: "Ctrl+Y")
            .Separator()
            .Item("Cut", shortcutText: "Ctrl+X")
            .Item("Copy", shortcutText: "Ctrl+C")
            .Item("Paste", shortcutText: "Ctrl+V")
            .Separator()
            .SubMenu("Find", new Menu()
                .Item("Find...", shortcutText: "Ctrl+F")
                .Item("Find Next", shortcutText: "F3")
                .Item("Replace...", shortcutText: "Ctrl+H")
            );

        var viewMenu = new Menu()
            .Item("Toggle Sidebar")
            .SubMenu("Zoom", new Menu()
                .Item("Zoom In", shortcutText: "Ctrl++")
                .Item("Zoom Out", shortcutText: "Ctrl+-")
                .Item("Reset", shortcutText: "Ctrl+0")
            );

        return Card(
                "MenuBar (Multi-depth)",
                new StackPanel()
                    .Width(290)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new MenuBar()
                            .Height(28)
                            .Items(
                                new MenuItem("File").Menu(fileMenu),
                                new MenuItem("Edit").Menu(editMenu),
                                new MenuItem("View").Menu(viewMenu)
                            ),
                        new Label()
                            .FontSize(11)
                            .Text("Hover to switch menus while a popup is open. Submenus supported.")
                    )
            );
    }

    private FrameworkElement PromptDialogCard()
    {
        var promptStatus = new ObservableValue<string>("Result: -");

        return Card(
            "Prompt Dialog (FitContentHeight)",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Label()
                        .FontSize(11)
                        .Text("Opens a FitContentHeight dialog.\nWindow height adjusts to content."),
                    new Button()
                        .Content("Show Prompt")
                        .OnClick(async () =>
                        {
                            var result = await ShowPromptAsync(
                                window,
                                "Input",
                                "Enter your name:",
                                "Name...");
                            promptStatus.Value = result is null
                                ? "Result: canceled"
                                : $"Result: {result}";
                        }),
                    new Label()
                        .BindText(promptStatus)
                        .FontSize(11)
                )
        );
    }

    private async Task<string?> ShowPromptAsync(
        Window owner,
        string title,
        string message,
        string? placeholder = null)
    {
        string? result = null;
        TextBox input = null!;
        Window dialog = null!;

        await new Window()
            .Ref(out dialog)
            .Title(title)
            .FitContentHeight(300, 300)
            .Padding(12)
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(12)
                    .Children(
                        new Label()
                            .Text(message),
                        new TextBox()
                            .Ref(out input)
                            .Placeholder(placeholder ?? string.Empty),
                        new StackPanel()
                            .Horizontal()
                            .Right()
                            .Spacing(6)
                            .Children(
                                new Button()
                                    .Content("OK")
                                    .OnCanClick(() => !string.IsNullOrWhiteSpace(input.Text))
                                    .OnClick(() =>
                                    {
                                        result = input.Text;
                                        dialog.Close();
                                    }),
                                new Button()
                                    .Content("Cancel")
                                    .OnClick(dialog.Close)
                            )
                    )
            ).ShowDialogAsync(owner);

        return result;
    }

    private FrameworkElement DevToolsCard()
    {
        var shortcuts = new Label()
            .FontSize(11)
            .Text("Shortcuts:\n- Inspector: Ctrl/Cmd+Shift+I\n- Visual Tree: Ctrl/Cmd+Shift+T");

        FrameworkElement content;
#if DEBUG
        bool updating = false;
        var inspectorToggle = new ToggleButton()
            .Content("Inspector Overlay");
        var treeToggle = new ToggleButton()
            .Content("Visual Tree Window");

        void UpdateToggles()
        {
            updating = true;
            try
            {
                inspectorToggle.IsChecked = window.DevToolsInspectorIsOpen;
                treeToggle.IsChecked = window.DevToolsVisualTreeIsOpen;
            }
            finally
            {
                updating = false;
            }
        }

        inspectorToggle.CheckedChanged += _ =>
        {
            if (updating)
            {
                return;
            }

            window.DevToolsToggleInspector();
            UpdateToggles();
        };

        treeToggle.CheckedChanged += _ =>
        {
            if (updating)
            {
                return;
            }

            window.DevToolsToggleVisualTree();
            UpdateToggles();
        };

        window.DevToolsInspectorOpenChanged += _ => UpdateToggles();
        window.DevToolsVisualTreeOpenChanged += _ => UpdateToggles();
        UpdateToggles();

        content = new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                inspectorToggle,
                treeToggle,
                shortcuts
            );
#else
    content = new StackPanel()
        .Vertical()
        .Spacing(8)
        .Children(
            new Label()
                .FontSize(11)
                .Text("DevTools are available in Debug builds only."),
            shortcuts
        );
#endif

        return Card("DevTools", content);
    }

    private void EnableWindowDrag(Window window, UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        bool dragging = false;
        Point dragStartScreenDip = default;
        Point windowStartDip = default;

        element.MouseDown += e =>
        {
            if (e.Button != MouseButton.Left)
            {
                return;
            }

            var local = e.GetPosition(element);
            if (local.X < 0 || local.Y < 0 || local.X >= element.RenderSize.Width || local.Y >= element.RenderSize.Height)
            {
                if (element.IsMouseCaptured)
                {
                    window.ReleaseMouseCapture();
                }
                return;
            }

            dragging = true;
            dragStartScreenDip = GetScreenDip(window, e);
            windowStartDip = window.Position;

            window.CaptureMouse(element);
            e.Handled = true;
        };

        element.MouseMove += e =>
        {
            if (!dragging)
            {
                return;
            }

            if (!e.LeftButton)
            {
                dragging = false;
                window.ReleaseMouseCapture();
                return;
            }

            var screenDip = GetScreenDip(window, e);
            double dx = screenDip.X - dragStartScreenDip.X;
            double dy = screenDip.Y - dragStartScreenDip.Y;

            window.MoveTo(windowStartDip.X + dx, windowStartDip.Y + dy);

            e.Handled = true;
        };

        element.MouseUp += e =>
        {
            if (e.Button != MouseButton.Left)
            {
                return;
            }

            if (!dragging)
            {
                return;
            }

            dragging = false;
            window.ReleaseMouseCapture();
            e.Handled = true;
        };

        static Point GetScreenDip(Window window, MouseEventArgs e)
        {
            var screen = window.ClientToScreen(e.GetPosition(window));
            if (OperatingSystem.IsWindows())
            {
                var scale = Math.Max(1.0, window.DpiScale);
                return new Point(screen.X / scale, screen.Y / scale);
            }
            return screen;
        }
    }
}
