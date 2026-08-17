using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement MenuPage() =>
        CardGrid(
            MenusCard(),
            AccessKeyCard()
        );

    private FrameworkElement WindowPage()
    {
        var dialogStatus = new ObservableValue<string>("Dialog: -");
        var transparentStatus = new ObservableValue<string>("Transparent: -");
        var manualPositionStatus = new ObservableValue<string>("Manual: -");

        // owner is the window the button lives in, so a dialog opened from a dialog stacks on it:
        // the parent dialog is disabled and stays behind while the nested one is up.
        async void ShowDialogSample(Window owner)
        {
            dialogStatus.Value = "Dialog: opening...";

            var dialog = new Window()
                .Resizable(420, 220)
                .StartCenterScreen()
                .Build(x => x
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
                                            .OnClick(() => ShowDialogSample(x)),
                                        new Button()
                                            .Content("Close")
                                            .OnClick(() => x.Close())
                                    )
                            )
                    )
                );

            try
            {
                await dialog.ShowDialogAsync(owner);
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
                .Build(x =>
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
                                                .BindSource(Resources.Logo)
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
            var cancelClose = new ObservableValue<bool>(false);

            new Window()
                .Ref(out manual)
                .Resizable(360, 180)
                .OnClosed(() => Console.WriteLine("Window closed"))
                .OnClosing(e => { e.Cancel = cancelClose.Value; Console.WriteLine($"Window closing"); })
                .StartManualPosition(left, top)
                .Build(x => x
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
                                    .FontSize(ThemeFontSize.Small)
                                    .Text("Use this sample to verify startup manual placement against the requested DIP coordinates."),
                                new CheckBox()
                                    .BindIsChecked(cancelClose)
                                    .Content("Cancel Close"),
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
                            .FontSize(ThemeFontSize.Small)
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
                            .FontSize(ThemeFontSize.Small)
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
                            .FontSize(ThemeFontSize.Small)
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
                            .OnClick(() => ShowDialogSample(window)),
                        new TextBlock()
                            .BindText(dialogStatus)
                            .FontSize(ThemeFontSize.Small)
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
                            .FontSize(ThemeFontSize.Small)
                    )
            ),

            Card(
                "StartupManualPosition",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(ThemeFontSize.Small)
                            .Text("Opens a window with StartManualPosition(120, 140)."),
                        new Button()
                            .Content("Open manual-position window")
                            .OnClick(ShowManualPositionSample),
                        new TextBlock()
                            .BindText(manualPositionStatus)
                            .FontSize(ThemeFontSize.Small)
                    )
            ),

            AsyncCloseCard(),

            PromptDialogCard(),

            NativeMessageHookCard(),

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

    private FrameworkElement AsyncCloseCard()
    {
        var status = new ObservableValue<string>("CloseAsync: -");
        Window? sample = null;

        void OpenSample()
        {
            if (sample != null)
            {
                sample.Activate();
                return;
            }

            var count = new ObservableValue<int>(3);
            var sampleWindow = new Window()
                .Title("Async close sample")
                .FitContentHeight(340, 300)
                .Padding(12)
                .Content(
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .TextWrapping(TextWrapping.Wrap)
                                .Text("Closing takes a deferral and asks for confirmation asynchronously. " +
                                      "Try the title-bar close button too - close requests made while the " +
                                      "confirmation is open join the pending decision."),
                            new TextBlock().BindText(count, x => $"Countdown: {x}")));

            sampleWindow.Closing += async args =>
            {
                using (args.GetDeferral())
                {
                    if (!await MessageBox.ConfirmAsync("Close this window?", owner: sampleWindow))
                    {
                        args.Cancel = true;
                    }
                    else
                    {
                        while (count.Value > 0)
                        {
                            await Task.Delay(1000);
                            count.Value--;
                        }

                    }
                }
            };
            sampleWindow.Closed += () => sample = null;

            sample = sampleWindow;
            sampleWindow.Show(window);
        }

        return Card(
            "Async Close (CloseAsync + Closing deferral)",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(ThemeFontSize.Small)
                        .Text("The sample window defers its Closing decision to an async confirmation.\nCloseAsync reports whether it actually closed."),
                    new WrapPanel()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Open sample window")
                                .OnClick(OpenSample),
                            new Button()
                                .Content("CloseAsync")
                                .OnClick(async () =>
                                {
                                    if (sample == null)
                                    {
                                        status.Value = "CloseAsync: no sample window";
                                        return;
                                    }

                                    bool closed = await sample.CloseAsync();
                                    status.Value = closed ? "CloseAsync: closed" : "CloseAsync: cancelled";
                                })),
                    new TextBlock()
                        .BindText(status)
                        .FontSize(ThemeFontSize.Small)));
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
                        .FontSize(ThemeFontSize.Small)
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
                        .FontSize(ThemeFontSize.Small)
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
        var acceptCommand = new Command("gallery.dialog.accept", "OK");

        await new Window()
            .Ref(out dialog)
            .Apply(w => w.Commands.Register(acceptCommand, () =>
            {
                result = input.Text;
                dialog.Close();
            }, () => !string.IsNullOrWhiteSpace(input.Text)))
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
                                    .Command(acceptCommand),
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
            .FontSize(ThemeFontSize.Small)
            .Text("Shortcuts:\n- Inspector: Ctrl/Cmd+Shift+I\n- Visual Tree: Ctrl/Cmd+Shift+T");

        FrameworkElement content;
        if (window.DevTools is WindowDevTools devTools)
        {
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
                    inspectorToggle.IsChecked = devTools.InspectorIsVisible;
                    treeToggle.IsChecked = devTools.VisualTreeIsOpen;
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

                devTools.ToggleInspector();
                UpdateToggles();
            };

            treeToggle.CheckedChanged += _ =>
            {
                if (updating)
                {
                    return;
                }

                devTools.ToggleVisualTree();
                UpdateToggles();
            };

            devTools.InspectorVisibleChanged += _ => UpdateToggles();
            devTools.VisualTreeOpenChanged += _ => UpdateToggles();
            UpdateToggles();

            content = new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    inspectorToggle,
                    treeToggle,
                    shortcuts
                );
        }
        else
        {
            content = new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(ThemeFontSize.Small)
                        .Text("DevTools are off in this build. Set <MewUIDevTools>true</MewUIDevTools> to enable them."),
                    shortcuts
                );
        }

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
#if MEWUI_GALLERY_WIN || (!MEWUI_GALLERY_LINUX && !MEWUI_GALLERY_OSX)
                case Win32NativeMessageEventArgs win32:
                    hookLog.Value = $"Win32 #{messageCount}: msg=0x{win32.Msg:X4} wParam=0x{win32.WParam:X} lParam=0x{win32.LParam:X}";
                    break;
#endif

#if MEWUI_GALLERY_LINUX || (!MEWUI_GALLERY_WIN && !MEWUI_GALLERY_OSX)
                case X11NativeMessageEventArgs x11:
                    hookLog.Value = $"X11 #{messageCount}: type={x11.EventType}";
                    break;
#endif

#if MEWUI_GALLERY_OSX || (!MEWUI_GALLERY_WIN && !MEWUI_GALLERY_LINUX)
                case MacOSNativeMessageEventArgs macos:
                    hookLog.Value = $"macOS #{messageCount}: type={macos.EventType}";
                    break;
#endif
            }
        }

        return Card(
            "NativeMessage Hook",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(ThemeFontSize.Small)
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
                        .FontSize(ThemeFontSize.Small)
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
            // ClientToScreen now returns top-left, Y-down pixels on every platform.
            var screen = window.ClientToScreen(e.GetPosition(window));
            var scale = Math.Max(1.0, window.DpiScale);
            return new Point(screen.X / scale, screen.Y / scale);
        }
    }
}
