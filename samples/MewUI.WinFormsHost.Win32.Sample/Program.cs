using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WF = System.Windows.Forms;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

try
{
    string backend = args.Contains("--gdi") ? "GDI" : args.Contains("--vg") ? "MewVG" : "Direct2D";

    Win32Platform.Register();
    switch (backend)
    {
        case "GDI":
            GdiBackend.Register();
            break;
        case "MewVG":
            MewVGWin32Backend.Register();
            break;
        default:
            Direct2DBackend.Register();
            break;
    }

    WF.Application.EnableVisualStyles();

    // Must run before any Windows Forms control exists. Without it Windows Forms creates its handles
    // DPI-unaware, so Windows bitmap-stretches the hosted controls and they never pick up the
    // monitor scale.
    WF.Application.SetHighDpiMode(WF.HighDpiMode.PerMonitorV2);

    AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject);
    Application.DispatcherUnhandledException += e =>
    {
        ShowFatal(e.Exception);
        e.Handled = true;
    };

    var calendar = new WF.MonthCalendar();
    var trackBar = new WF.TrackBar { Minimum = 0, Maximum = 100, Value = 40, TickFrequency = 10 };
    var propertyGrid = new WF.PropertyGrid { SelectedObject = new SampleSettings() };

    // Several tab stops in one host: Tab cycles through them without leaving the host.
    var formPanel = new WF.Panel();
    formPanel.Controls.Add(new WF.TextBox { Text = "first", Location = new System.Drawing.Point(8, 8), Width = 160 });
    formPanel.Controls.Add(new WF.TextBox { Text = "second", Location = new System.Drawing.Point(180, 8), Width = 160 });
    formPanel.Controls.Add(new WF.CheckBox { Text = "third", Location = new System.Drawing.Point(352, 8) });
    formPanel.Controls.Add(new WF.Button { Text = "fourth", Location = new System.Drawing.Point(440, 6) });

    Window window = null!;
    WinFormsHost calendarHost = null!;
    WinFormsHost trackBarHost = null!;
    WinFormsHost propertyGridHost = null!;
    Label statusLabel = null!;
    ScrollViewer scroller = null!;

    Application.Create()
        .UseAccent(Accent.Purple)
        .BuildMainWindow(() =>
        {
            return new Window()
                .Ref(out window)
                .Resizable(880, 660)
                .Title($"MewUI.WinFormsHost.Win32 Sample ({backend})")
                .Padding(8)
                .Content(
                    new DockPanel()
                        .Spacing(8)
                        .Children(
                            new StackPanel()
                                .DockTop()
                                .Horizontal()
                                .Spacing(8)
                                .CenterVertical()
                                .Children(
                                    new Button()
                                        .Content("Maximize")
                                        .OnClick(() => window.WindowState = WindowState.Maximized),
                                    new Button()
                                        .Content("Restore")
                                        .OnClick(() => window.WindowState = WindowState.Normal),
                                    new CheckBox()
                                        .Content("Clip to ancestors")
                                        .IsChecked(true)
                                        .OnCheckedChanged(value =>
                                        {
                                            calendarHost.ClipToAncestors = value;
                                            trackBarHost.ClipToAncestors = value;
                                            propertyGridHost.ClipToAncestors = value;
                                        }),
                                    new Label()
                                        .Ref(out statusLabel)
                                        .CenterVertical()
                                        .Text("Scroll to see clipping at the viewport edges")),
                            new TextBox()
                                .DockTop()
                                .Text("MewUI TextBox: click a hosted control, then click here and type")
                                .OnGotFocus(() => statusLabel.Text = "Focus: MewUI TextBox")
                                .OnKeyDown(_ => statusLabel.Text = "Typing reached the MewUI TextBox"),
                            BuildTabs()))
                .WithTheme((theme, _) =>
                {
                    // Scroll bars are drawn over the content, so a hosted window would cover them
                    // unless their hit width is reserved as padding.
                    scroller.Padding = new Thickness(0, 0, theme.Metrics.ScrollBarHitThickness, 0);
                });
        })
        .Run();

    TabControl BuildTabs()
    {
        var tabs = new TabControl().Padding(4);
        tabs.AddTabs(
            new TabItem()
                .Header("Scrolled hosts")
                .Content(
                    new ScrollViewer()
                        .Ref(out scroller)
                        .Content(
                            new StackPanel()
                                .Spacing(12)
                                .Children(
                                    SectionLabel("Tab runs through these four controls, then leaves for the next MewUI element"),
                                    new WinFormsHost()
                                        .Child(formPanel)
                                        .Height(48),
                                    SectionLabel("System.Windows.Forms.MonthCalendar"),
                                    new WinFormsHost()
                                        .Ref(out calendarHost)
                                        .Child(calendar)
                                        .Height(200),
                                    SectionLabel("System.Windows.Forms.TrackBar"),
                                    new WinFormsHost()
                                        .Ref(out trackBarHost)
                                        .Child(trackBar)
                                        .Height(60),
                                    SectionLabel("System.Windows.Forms.PropertyGrid"),
                                    new WinFormsHost()
                                        .Ref(out propertyGridHost)
                                        .Child(propertyGrid)
                                        .Height(320),
                                    SectionLabel("MewUI content below the hosted windows"),
                                    new Button().Content("Plain MewUI button").Height(40)))),
            new TabItem()
                .Header("Host in a tab")
                .Content(
                    new StackPanel()
                        .Spacing(8)
                        .Children(
                            SectionLabel("The host must disappear while another tab is selected"),
                            new WinFormsHost()
                                .Child(new WF.ListView
                                {
                                    View = WF.View.List,
                                    Items =
                                    {
                                        "hosted item 1",
                                        "hosted item 2",
                                        "hosted item 3",
                                    },
                                })
                                .Height(180))),
            new TabItem()
                .Header("MewUI only")
                .Content(
                    new StackPanel()
                        .Spacing(8)
                        .Children(
                            SectionLabel("No hosted window here; nothing from the other tabs may show"),
                            new Button().Content("MewUI button").Height(40))));

        return tabs;
    }

    static Label SectionLabel(string text) => new Label().Text(text);
}
catch (Exception ex)
{
    ShowFatal(ex);
}

static void ShowFatal(object? error)
{
    try
    {
        NativeMessageBox.Show(
            error?.ToString() ?? "Unknown error",
            "Unhandled exception",
            NativeMessageBoxButtons.Ok,
            NativeMessageBoxIcon.Error);
    }
    catch
    {
    }
}

internal sealed class SampleSettings
{
    public string Title { get; set; } = "Hosted PropertyGrid";

    public int Count { get; set; } = 3;

    public bool Enabled { get; set; } = true;

    public DateTime Created { get; set; } = new DateTime(2026, 1, 1);
}
