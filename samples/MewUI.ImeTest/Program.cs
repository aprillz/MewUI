using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

if (OperatingSystem.IsWindows())
{
    Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
    Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
}

Startup();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try { Console.WriteLine(e.ExceptionObject?.ToString()); } catch { }
};

Application.DispatcherUnhandledException += e =>
{
    try { Console.WriteLine(e.Exception.ToString()); } catch { }
    e.Handled = true;
};

var logBox = new MultiLineTextBox
{
    Wrap = true,
    IsHitTestVisible = false,
};

var logLines = 0;
void AppendLog(string line)
{
    if (!Application.IsRunning)
    {
        return;
    }

    var text = $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n";
    logBox.AppendText(text, scrollToCaret: true);
    logLines++;

    // Keep the log bounded so the sample doesn't grow unbounded during long IME sessions.
    if (logLines > 2000)
    {
        logLines = 0;
        logBox.Text = string.Empty;
    }
}

var handlePreviewComposition = new ObservableValue<bool>(false);
var handleBubblingComposition = new ObservableValue<bool>(false);
var handleTextInput = new ObservableValue<bool>(false);

TextBox singleLine = null!;
MultiLineTextBox multiLine = null!;

Application.Create()
    .UseAccent(Accent.Purple)
    .BuildMainWindow(() => new Window()
        .Resizable(900, 700)
        .Title("MewUI IME Test")
        .Padding(16)
        .OnPreviewTextCompositionStart(e =>
        {
            AppendLog($"PreviewCompositionStart text='{e.Text}'");
            if (handlePreviewComposition.Value)
            {
                e.Handled = true;
                AppendLog("  -> handled (preview)");
            }
        })
        .OnPreviewTextCompositionUpdate(e =>
        {
            AppendLog($"PreviewCompositionUpdate text='{e.Text}'");
            if (handlePreviewComposition.Value)
            {
                e.Handled = true;
                AppendLog("  -> handled (preview)");
            }
        })
        .OnPreviewTextCompositionEnd(e =>
        {
            AppendLog($"PreviewCompositionEnd text='{e.Text}'");
            if (handlePreviewComposition.Value)
            {
                e.Handled = true;
                AppendLog("  -> handled (preview)");
            }
        })
        .OnPreviewTextInput(e =>
        {
            AppendLog($"PreviewTextInput text='{e.Text}'");
            if (handleTextInput.Value)
            {
                e.Handled = true;
                AppendLog("  -> handled (preview text input)");
            }
        })
        .Content(
            new StackPanel()
                .Vertical()
                .Spacing(12)
                .Children(
                    new Label()
                        .Text($"Backend: {Application.SelectedGraphicsBackend} | OS: {GetOsName()}"),

                    new Border()
                        .CornerRadius(8)
                        .Padding(12)
                        .WithTheme((t, b) => b.Background(t.Palette.ControlBackground))
                        .Child(
                            new StackPanel()
                                .Vertical()
                                .Spacing(8)
                                .Children(
                                    new Label()
                                        .Text("How to test:")
                                        .Bold(),

                                    new Label().Text("- Click a textbox and type with IME (Korean/Japanese/Chinese)."),
                                    new Label().Text("- On Win32/macOS you should see composition start/update/end."),
                                    new Label().Text("- On X11 this sample currently logs committed TextInput only (preedit callbacks are not wired yet).")
                                )
                        ),

                    new StackPanel()
                        .Horizontal()
                        .Spacing(12)
                        .Children(
                            new CheckBox()
                                .Content("Handle preview composition")
                                .BindIsChecked(handlePreviewComposition),
                            new CheckBox()
                                .Content("Handle bubbling composition")
                                .BindIsChecked(handleBubblingComposition),
                            new CheckBox()
                                .Content("Handle text input")
                                .BindIsChecked(handleTextInput),
                            new Button()
                                .Content("Clear log")
                                .OnClick(() =>
                                {
                                    logLines = 0;
                                    logBox.Text = string.Empty;
                                })
                        ),

                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new Label().Text("Single-line TextBox").Bold(),
                            new TextBox()
                                .Ref(out singleLine)
                                .Placeholder("Type here...")
                                .OnTextCompositionStart(e =>
                                {
                                    AppendLog($"TextBox CompositionStart text='{e.Text}'");
                                    if (handleBubblingComposition.Value)
                                    {
                                        e.Handled = true;
                                        AppendLog("  -> handled (textbox)");
                                    }
                                })
                                .OnTextCompositionUpdate(e =>
                                {
                                    AppendLog($"TextBox CompositionUpdate text='{e.Text}'");
                                    if (handleBubblingComposition.Value)
                                    {
                                        e.Handled = true;
                                        AppendLog("  -> handled (textbox)");
                                    }
                                })
                                .OnTextCompositionEnd(e =>
                                {
                                    AppendLog($"TextBox CompositionEnd text='{e.Text}'");
                                    if (handleBubblingComposition.Value)
                                    {
                                        e.Handled = true;
                                        AppendLog("  -> handled (textbox)");
                                    }
                                })
                                .OnTextInput(e => AppendLog($"TextBox TextInput text='{e.Text}'")),

                            new Label().Text("Multi-line TextBox").Bold(),

                            new MultiLineTextBox()
                                .Ref(out multiLine)
                                .Wrap(true)
                                .Text("Type here...\n(try long composition and commits)")
                                .OnTextCompositionStart(e =>
                                {
                                    AppendLog($"MultiLine CompositionStart text='{e.Text}'");
                                    if (handleBubblingComposition.Value)
                                    {
                                        e.Handled = true;
                                        AppendLog("  -> handled (multiline)");
                                    }
                                })
                                .OnTextCompositionUpdate(e =>
                                {
                                    AppendLog($"MultiLine CompositionUpdate text='{e.Text}'");
                                    if (handleBubblingComposition.Value)
                                    {
                                        e.Handled = true;
                                        AppendLog("  -> handled (multiline)");
                                    }
                                })
                                .OnTextCompositionEnd(e =>
                                {
                                    AppendLog($"MultiLine CompositionEnd text='{e.Text}'");
                                    if (handleBubblingComposition.Value)
                                    {
                                        e.Handled = true;
                                        AppendLog("  -> handled (multiline)");
                                    }
                                })
                                .OnTextInput(e => AppendLog($"MultiLine TextInput text='{e.Text}'"))
                                .Height(160),

                            new Label().Text("Event log (read-only)").Bold(),
                            logBox
                                .Height(260)
                        )
                )
        )
        .OnLoaded(() =>
        {
            AppendLog("Loaded.");
            // Make it easy to start typing immediately.
            singleLine.Focus();
        })
    )
    .Run();

static string GetOsName()
{
    if (OperatingSystem.IsWindows()) return "Windows";
    if (OperatingSystem.IsMacOS()) return "macOS";
    if (OperatingSystem.IsLinux()) return "Linux";
    return Environment.OSVersion.Platform.ToString();
}

static void Startup()
{
    var args = Environment.GetCommandLineArgs();

    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();

        if (args.Any(a => a is "--gdi"))
        {
            GdiBackend.Register();
        }
        else if (args.Any(a => a is "--vg"))
        {
            MewVGWin32Backend.Register();
        }
        else
        {
            Direct2DBackend.Register();
        }
    }
    else if (OperatingSystem.IsMacOS())
    {
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
    }
    else
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
    }
}

