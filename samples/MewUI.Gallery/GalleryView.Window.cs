using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement WindowPage()
    {
        var transparentStatus = new ObservableValue<string>("Transparent: -");
        var manualPositionStatus = new ObservableValue<string>("Manual: -");

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

        var syncStatus = new ObservableValue<string>("Result: -");
        var asyncStatus = new ObservableValue<string>("Result: -");
        var dialogStatus = new ObservableValue<string>("Dialog: -");

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
            NativeMessageHookCard(),

            Card(
                "Synchronous ShowDialog",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(ThemeFontSize.Small)
                            .Text("ShowDialog() blocks this click handler (no await)\nwhile a nested loop keeps input and paint live."),
                        new Button()
                            .Content("Show (sync)")
                            .OnClick(() =>
                            {
                                // Note: this handler is NOT async. ShowDialog blocks here until the dialog closes.
                                var dialog = new SyncDialogWindow();
                                dialog.ShowDialog(window);
                                syncStatus.Value = $"Result: {dialog.Result}, clicks={dialog.ClickCount}";
                            }),
                        new TextBlock().BindText(syncStatus).FontSize(ThemeFontSize.Small)
                    )
            ),

            Card(
                "Asynchronous ShowDialogAsync",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(ThemeFontSize.Small)
                            .Text("ShowDialogAsync() returns a Task on the same loop.\nSame dialog, awaited instead of blocking."),
                        new Button()
                            .Content("Show (async)")
                            .OnClick(async () =>
                            {
                                var dialog = new SyncDialogWindow();
                                await dialog.ShowDialogAsync(window);
                                asyncStatus.Value = $"Result: {dialog.Result}, clicks={dialog.ClickCount}";
                            }),
                        new TextBlock().BindText(asyncStatus).FontSize(ThemeFontSize.Small)
                    )
            ),

            Card(
                "Nested Dialogs (owner)",
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

            PromptDialogCard()
        );
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
#if MEWUI_GALLERY_WIN || (!MEWUI_GALLERY_BROWSER && !MEWUI_GALLERY_LINUX && !MEWUI_GALLERY_OSX)
                case Win32NativeMessageEventArgs win32:
                    hookLog.Value = $"Win32 #{messageCount}: msg=0x{win32.Msg:X4} wParam=0x{win32.WParam:X} lParam=0x{win32.LParam:X}";
                    break;
#endif

#if MEWUI_GALLERY_LINUX || (!MEWUI_GALLERY_BROWSER && !MEWUI_GALLERY_WIN && !MEWUI_GALLERY_OSX)
                case X11NativeMessageEventArgs x11:
                    hookLog.Value = $"X11 #{messageCount}: type={x11.EventType}";
                    break;
#endif

#if MEWUI_GALLERY_OSX || (!MEWUI_GALLERY_BROWSER && !MEWUI_GALLERY_WIN && !MEWUI_GALLERY_LINUX)
                case MacOSNativeMessageEventArgs macos:
                    hookLog.Value = $"macOS #{messageCount}: type={macos.EventType}";
                    break;
#endif
#if MEWUI_GALLERY_BROWSER
                default:
                    hookLog.Value = $"Browser #{messageCount}: native message";
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
