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
                                new TextBlock()
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
                                                            new TextBlock()
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
                                new TextBlock()
                                    .Text($"StartupLocation.Manual\nLeft: {left}\nTop: {top}"),
                                new TextBlock()
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
                "Native Custom Chrome",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Open Native Chrome Window")
                            .OnClick(() => new NativeCustomWindowSample().Show(window)),
                        new TextBlock()
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Hides the default title bar while keeping\nthe native frame (rounded corners, shadow).")
                    )
            ),

            Card(
                "Custom Chrome Window",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Open CustomWindow")
                            .OnClick(() => new CustomWindowSample().Show(window)),
                        new TextBlock()
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("AllowsTransparency-based custom chrome.\nProvides rounded borders on Win10 and earlier.\nWin32: higher overhead. Prefer NativeCustomWindow.")
                    )
            ),

            Card(
                "Hot-reload",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Modify the code and save to see hot-reload in action.\nThis card will update with the current time."),
                        new TextBlock()
                            .Text($"Loaded: {DateTime.Now}"))
            ),

            Card(
                "ShowDialogAsync",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Open dialog")
                            .OnClick(ShowDialogSample),
                        new TextBlock()
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
                        new TextBlock()
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
                        new TextBlock()
                            .FontSize(11)
                            .Text("Opens a window with StartManualPosition(120, 140)."),
                        new Button()
                            .Content("Open manual-position window")
                            .OnClick(ShowManualPositionSample),
                        new TextBlock()
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

                        new TextBlock()
                            .BindText(openFilesStatus)
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap),

                        new TextBlock()
                            .BindText(saveFileStatus)
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap),

                        new TextBlock()
                            .BindText(folderStatus)
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                    )
            ),

            PromptDialogCard(),

            NativeMessageHookCard(),

            AccessKeyCard(),

            DevToolsCard()
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
                    new TextBlock().Text("Press Alt to show access key underlines (Windows/Linux).").FontSize(11),

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


    private FrameworkElement MenusCard()
    {
        var shortcutLog = new TextBlock()
            .FontSize(11)
            .TextWrapping(TextWrapping.Wrap)
            .Text("Press a shortcut key (e.g. (Ctrl or Cmd)+N, (Ctrl or Cmd)+S, ...)");

        void OnShortcut(string action) => shortcutLog.Text = $"[{DateTime.Now:HH:mm:ss.fff}] {action}";

        return Card(
                "MenuBar (Multi-depth, AccessKeys + Shortcuts)",
                new StackPanel()
                    .Width(290)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        CreateMenu(OnShortcut),

                        new TextBlock()
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Hover to switch menus while a popup is open. Submenus supported."),

                        shortcutLog
                    )
            );
    }

    public static MenuBar CreateMenu(Action<string> OnShortcut)
    {
        var p = ModifierKeys.Primary;
        var fileMenu = new Menu()
            .Item("_New", () => OnShortcut("File > New document created"), shortcut: new KeyGesture(Key.N, p))
            .Item("_Open...", () => OnShortcut("File > Open file dialog"), shortcut: new KeyGesture(Key.O, p))
            .Item("_Save", () => OnShortcut("File > Document saved"), shortcut: new KeyGesture(Key.S, p))
            .Item("Save _As...", () => OnShortcut("File > Save As dialog"))
            .Separator()
            .SubMenu("_Export", new Menu()
                .Item("_PNG", () => OnShortcut("File > Export > PNG format"))
                .Item("_JPEG", () => OnShortcut("File > Export > JPEG format"))
                .SubMenu("_Advanced", new Menu()
                    .Item("With _metadata", () => OnShortcut("File > Export > Advanced > Include metadata"))
                    .Item("_Optimized", () => OnShortcut("File > Export > Advanced > Optimized output"))
                )
            )
            .Separator()
            .Item("E_xit", () => OnShortcut("File > Exit application"));

        var editMenu = new Menu()
            .Item("_Undo", () => OnShortcut("Edit > Undo last action"), shortcut: new KeyGesture(Key.Z, p))
            .Item("_Redo", () => OnShortcut("Edit > Redo last action"), shortcut: new KeyGesture(Key.Y, p))
            .Separator()
            .Item("Cu_t", () => OnShortcut("Edit > Cut to clipboard"), shortcut: new KeyGesture(Key.X, p))
            .Item("_Copy", () => OnShortcut("Edit > Copy to clipboard"), shortcut: new KeyGesture(Key.C, p))
            .Item("_Paste", () => OnShortcut("Edit > Paste from clipboard"), shortcut: new KeyGesture(Key.V, p))
            .Separator()
            .SubMenu("_Find", new Menu()
                .Item("_Find...", () => OnShortcut("Edit > Find > Open find dialog"), shortcut: new KeyGesture(Key.F, p))
                .Item("Find _Next", () => OnShortcut("Edit > Find > Find next occurrence"), shortcut: new KeyGesture(Key.F3))
                .Item("_Replace...", () => OnShortcut("Edit > Find > Open replace dialog"), shortcut: new KeyGesture(Key.H, p))
            );

        var viewMenu = new Menu()
            .Item("_Toggle Sidebar", () => OnShortcut("View > Toggle sidebar visibility"))
            .SubMenu("_Zoom", new Menu()
                .Item("Zoom _In", () => OnShortcut("View > Zoom > Zoom in"), shortcut: new KeyGesture(Key.Add, p))
                .Item("Zoom _Out", () => OnShortcut("View > Zoom > Zoom out"), shortcut: new KeyGesture(Key.Subtract, p))
                .Item("_Reset", () => OnShortcut("View > Zoom > Reset to 100%"), shortcut: new KeyGesture(Key.D0, p))
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

    private FrameworkElement PromptDialogCard()
    {
        var promptStatus = new ObservableValue<string>("Result: -");

        return Card(
            "Prompt Dialog (FitContentHeight)",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
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
                    new TextBlock()
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
                        new TextBlock()
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
        var shortcuts = new TextBlock()
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
            new TextBlock()
                .FontSize(11)
                .Text("DevTools are available in Debug builds only."),
            shortcuts
        );
#endif

        return Card("DevTools", content);
    }

    private FrameworkElement NativeMessageHookCard()
    {
        var hookLog = new ObservableValue<string>("Hook: idle");
        var messageCount = 0;
        bool hookActive = false;

        void OnNativeMessage(NativeMessageEventArgs args)
        {
            messageCount++;
            switch (args)
            {
                case Win32NativeMessageEventArgs win32:
                    hookLog.Value = $"Win32 #{messageCount}: msg=0x{win32.Msg:X4} wParam=0x{win32.WParam:X} lParam=0x{win32.LParam:X}";
                    break;

                case X11NativeMessageEventArgs x11:
                    hookLog.Value = $"X11 #{messageCount}: type={x11.EventType}";
                    break;

                case MacOSNativeMessageEventArgs macos:
                    hookLog.Value = $"macOS #{messageCount}: type={macos.EventType}";
                    break;
            }
        }

        return Card(
            "NativeMessage Hook",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(11)
                        .Text("Subscribes to Window.NativeMessage to observe raw platform messages."),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Start Hook")
                                .OnClick(() =>
                                {
                                    if (!hookActive)
                                    {
                                        hookActive = true;
                                        messageCount = 0;
                                        window.NativeMessage += OnNativeMessage;
                                        hookLog.Value = "Hook: active";
                                    }
                                }),
                            new Button()
                                .Content("Stop Hook")
                                .OnClick(() =>
                                {
                                    if (hookActive)
                                    {
                                        hookActive = false;
                                        window.NativeMessage -= OnNativeMessage;
                                        hookLog.Value = $"Hook: stopped (total {messageCount} messages)";
                                    }
                                })
                        ),
                    new TextBlock()
                        .BindText(hookLog)
                        .FontSize(11)
                        .TextWrapping(TextWrapping.Wrap)
                )
        );
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
            var scale = Math.Max(1.0, window.DpiScale);
            if (OperatingSystem.IsMacOS())
                return new Point(screen.X / scale, -screen.Y / scale); // Cocoa Y (bottom-up) -> top-down

            return new Point(screen.X / scale, screen.Y / scale);
        }
    }
}
