using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Terminal;

Startup();

Application.Run(new TerminalWindow(Environment.GetCommandLineArgs()));

// Platform and backend registration: a runtime-specific publish compiles only its own pair; the
// portable build picks by operating system, with --gdi / --vg selecting the Windows backend.
static void Startup()
{
    var args = Environment.GetCommandLineArgs();

#if MEWUI_SAMPLE_WIN
#pragma warning disable CA1416
    Win32Platform.Register();
    RegisterWindowsBackend(args);
#pragma warning restore CA1416
#elif MEWUI_SAMPLE_OSX
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
#elif MEWUI_SAMPLE_LINUX
    X11Platform.Register();
    MewVGX11Backend.Register();
#else
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();
        RegisterWindowsBackend(args);
    }
    else if (OperatingSystem.IsMacOS())
    {
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
    }
    else if (OperatingSystem.IsLinux())
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
    }
#endif
}

#if MEWUI_SAMPLE_WIN || MEWUI_SAMPLE_ALL
static void RegisterWindowsBackend(string[] args)
{
    if (args.Any(argument => argument is "--gdi"))
    {
        GdiBackend.Register();
    }
    else if (args.Any(argument => argument is "--vg"))
    {
        MewVGWin32Backend.Register();
    }
    else
    {
        Direct2DBackend.Register();
    }
}
#endif

/// <summary>A window of terminal tabs, each running its own shell, with a bar of shells to open a tab for.</summary>
public sealed class TerminalWindow : Window
{
    private const int TITLE_LENGTH = 30;
    // Shell icons are shown at this size and read at the larger size, so they stay sharp on scaled displays.
    private const int ICON_SIZE = 16;
    private const int ICON_PIXELS = 32;

    private static readonly Dictionary<string, Task<ImageSource?>> _iconTasks = new();
    private static Task _lastIconLoad = Task.CompletedTask;

    private readonly string[] _args;
    private readonly Dictionary<TabItem, TerminalTab> _tabs = new();
    private TabControl _tabControl = null!;
    private TextBlock _status = null!;
    // The first tab, which the command-line script options drive.
    private TerminalTab? _firstTab;

    public TerminalWindow(string[] args)
    {
        _args = args;
    }

    protected override void OnBuild()
    {
        base.OnBuild();

        _status = new TextBlock().Text(string.Empty).CenterVertical().Margin(8, 0, 0, 0);

        var barItems = new List<Element>();
        foreach (var (label, fileName, arguments) in AvailableShells())
        {
            barItems.Add(new Button()
                .Content(new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(4)
                    .CenterVertical()
                    .Children(
                        ShellIcon(fileName, new GlyphElement().Kind(GlyphKind.Plus).GlyphSize(4)),
                        new TextBlock().Text(label)))
                .ToolTip($"New {label} tab")
                .OnClick(() => AddTab(label, fileName, arguments)));
        }

        barItems.Add(_status);
        var bar = new StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6)
            .Margin(6)
            .Children(barItems.ToArray());

        _tabControl = new TabControl()
            .Padding(2)
            .OnSelectionChanged(_ => OnTabSelected());

        this.Resizable(980, 640)
            .Title("MewUI Terminal")
            .Padding(0)
            .Content(new DockPanel().Children(bar.DockTop(), _tabControl));

        Closed += () =>
        {
            foreach (var tab in _tabs.Values)
            {
                tab.Connection?.Dispose();
            }
        };

        // --shell <program> starts that program in the first tab; otherwise the first shell in the bar.
        int shellIndex = Array.IndexOf(_args, "--shell");
        if (shellIndex >= 0 && shellIndex + 1 < _args.Length)
        {
            _firstTab = AddTab(_args[shellIndex + 1], _args[shellIndex + 1], []);
        }
        else
        {
            var (label, fileName, arguments) = AvailableShells()[0];
            _firstTab = AddTab(label, fileName, arguments);
        }

        RunScript();
    }

    /// <summary>
    /// For checks without a person at the machine, on the first tab: <c>--send text</c> (repeatable) types each line
    /// two seconds apart, <c>--dump-after ms</c> prints the screen rows to stderr, and <c>--exit-after ms</c> closes
    /// the window.
    /// </summary>
    private void RunScript()
    {
        var sends = new List<string>();
        int dumpAfter = -1;
        int exitAfter = -1;
        for (int index = 0; index + 1 < _args.Length; index++)
        {
            switch (_args[index])
            {
                case "--send":
                    sends.Add(_args[index + 1]);
                    break;
                case "--dump-after":
                    dumpAfter = int.Parse(_args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--exit-after":
                    exitAfter = int.Parse(_args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }

        for (int index = 0; index < sends.Count; index++)
        {
            string line = sends[index];
            After(2000 * (index + 1), () => _firstTab?.Connection?.Write(System.Text.Encoding.UTF8.GetBytes(line + "\r")));
        }

        // --bench <file> cats the file into a terminal that is not shown, to measure without drawing.
        int benchIndex = Array.IndexOf(_args, "--bench");
        if (benchIndex >= 0 && benchIndex + 1 < _args.Length)
        {
            string file = _args[benchIndex + 1];
            var offscreen = new TerminalView();
            var connection = new ProcessConnection(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/cat", OperatingSystem.IsWindows() ? ["/c", "type", file] : [file]);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            offscreen.ConnectionClosed += _ =>
            {
                clock.Stop();
                long length = new FileInfo(file).Length;
                Console.Error.WriteLine($"bench offscreen: {length / clock.Elapsed.TotalSeconds / 1_000_000:F1} MB/s ({clock.ElapsedMilliseconds} ms)");
                connection.Dispose();
            };
            offscreen.Connection = connection;
            connection.Start();
        }

        if (Array.IndexOf(_args, "--trace-frames") >= 0)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int frames = 0;
            FrameRendered += () => Console.Error.WriteLine($"frame {++frames} at {clock.ElapsedMilliseconds} ms, cursor row text '{_firstTab?.View.GetRowText(0)}'");
        }

        if (dumpAfter >= 0)
        {
            After(dumpAfter, () =>
            {
                var view = _firstTab?.View;
                if (view == null)
                {
                    Console.Error.WriteLine("no first tab");
                    return;
                }

                view.Flush();
                Console.Error.WriteLine($"tabs {_tabControl.Tabs.Count} selected {_tabControl.SelectedIndex} grid {view.Columns}x{view.Rows} title '{view.Title}'");
                for (int row = 0; row < view.Rows; row++)
                {
                    string text = view.GetRowText(row);
                    if (text.Length != 0)
                    {
                        Console.Error.WriteLine($"{row,3}: {text}");
                    }
                }
            });
        }

        if (exitAfter >= 0)
        {
            After(exitAfter, Close);
        }
    }

    private static void After(int milliseconds, Action action)
    {
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds));
        timer.Tick += () =>
        {
            timer.Stop();
            timer.Dispose();
            action();
        };
        timer.Start();
    }

    /// <summary>Opens a tab running the shell, selects it and gives it the keyboard.</summary>
    private TerminalTab AddTab(string label, string fileName, string[] arguments)
    {
        var view = new TerminalView
        {
            FontFamily = OperatingSystem.IsWindows() ? "Cascadia Mono, Consolas" : (OperatingSystem.IsMacOS() ? "Menlo" : "DejaVu Sans Mono, Monospace"),
            FontSize = 14,
        };

        int fontSizeIndex = Array.IndexOf(_args, "--font-size");
        if (fontSizeIndex >= 0 && fontSizeIndex + 1 < _args.Length)
        {
            view.FontSize = double.Parse(_args[fontSizeIndex + 1], System.Globalization.CultureInfo.InvariantCulture);
        }

        // --option-is-meta sends Option (Alt) combinations as ESC and the key instead of the character Option types.
        view.OptionIsMeta = Array.IndexOf(_args, "--option-is-meta") >= 0;

        var title = new TextBlock().Text(label).CenterVertical();
        TabItem tabItem = null!;
        new TabItem().Ref(out tabItem)
            .Header(new StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(6)
                .Children(
                    ShellIcon(fileName, null),
                    title,
                    new Button()
                        .Margin(8, 0, 0, 0)
                        .Content(new GlyphElement().Kind(GlyphKind.Cross).GlyphSize(3.5))
                        .ToolTip("Close tab")
                        .MinHeight(0)
                        .Size(16, 16)
                        .Padding(new Thickness(0))
                        .CenterVertical()
                        .BorderThickness(0)
                        .OnClick(() => CloseTab(tabItem))))
            .Content(view);

        var tab = new TerminalTab(tabItem, view, title, label);
        _tabs[tabItem] = tab;

        view.TitleChanged += terminalTitle =>
        {
            title.Text = Shorten(terminalTitle.Length == 0 ? label : terminalTitle);
            if (ReferenceEquals(_tabControl.SelectedTab, tabItem))
            {
                UpdateWindowTitle();
            }
        };
        view.HyperlinkActivated += uri => _status.Text = $"Link: {uri}";

        // The shell has ended: its tab goes, and the window with the last one.
        view.ConnectionClosed += exitCode =>
        {
            if (!_tabs.ContainsKey(tabItem))
            {
                return;
            }

            _status.Text = exitCode == 0 ? string.Empty : $"{label} exited with {exitCode}";
            CloseTab(tabItem);
        };

        var connection = new ProcessConnection(fileName, arguments)
        {
            // The shell's name is its title until it sets one, as in Windows Terminal; cmd keeps it.
            StartingTitle = label,
            InitialColumns = Math.Max(view.Columns, 20),
            InitialRows = Math.Max(view.Rows, 5),
        };
        tab.Connection = connection;
        view.Connection = connection;

        _tabControl.AddTab(tabItem);
        _tabControl.SelectedIndex = _tabControl.Tabs.Count - 1;

        try
        {
            connection.Start();
        }
        catch (IOException exception)
        {
            // The message is shown in the tab itself, where a shell would have been.
            view.Write($"{fileName}: {exception.Message}\r\n");
        }

        return tab;
    }

    private void CloseTab(TabItem tabItem)
    {
        if (!_tabs.Remove(tabItem, out var tab))
        {
            return;
        }

        tab.Connection?.Dispose();
        int index = IndexOfTab(tabItem);
        if (index >= 0)
        {
            _tabControl.RemoveTabAt(index);
        }

        if (_tabControl.Tabs.Count == 0)
        {
            Close();
        }
    }

    private int IndexOfTab(TabItem tabItem)
    {
        for (int index = 0; index < _tabControl.Tabs.Count; index++)
        {
            if (ReferenceEquals(_tabControl.Tabs[index], tabItem))
            {
                return index;
            }
        }

        return -1;
    }

    private void OnTabSelected()
    {
        UpdateWindowTitle();

        // The content attaches as the selection commits; focus after that has settled.
        Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            if (_tabControl.SelectedTab is TabItem selected && _tabs.TryGetValue(selected, out var tab))
            {
                tab.View.Focus();
            }
        });
    }

    private void UpdateWindowTitle()
    {
        string title = _tabControl.SelectedTab is TabItem selected && _tabs.TryGetValue(selected, out var tab) ? tab.View.Title : string.Empty;
        Title = title.Length == 0 ? "MewUI Terminal" : title + " - MewUI Terminal";
    }

    private static string Shorten(string title) => title.Length > TITLE_LENGTH ? title[..TITLE_LENGTH] + "..." : title;

    /// <summary>
    /// The icon of the program at the path, shown once the shell has given it; until then, and for good when there is
    /// none, the fallback shows in its place.
    /// </summary>
    private static Element ShellIcon(string path, UIElement? fallback)
    {
        var image = new Image().Size(ICON_SIZE, ICON_SIZE).CenterVertical();
        image.IsVisible = false;
        var host = new Grid().Size(ICON_SIZE, ICON_SIZE).CenterVertical().Children(image);
        if (fallback != null)
        {
            host.Children(fallback);
        }

        LoadIcon(path).ContinueWith(task => Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            if (task.Result != null)
            {
                image.Source = task.Result;
                image.IsVisible = true;
                if (fallback != null)
                {
                    fallback.IsVisible = false;
                }
            }
        }), TaskScheduler.Default);

        return host;
    }

    /// <summary>
    /// Reads a program's own icon off the UI thread, once per path. The reads run one after another: two at once make
    /// the shell icon provider return null for one of them.
    /// </summary>
    private static Task<ImageSource?> LoadIcon(string path)
    {
        lock (_iconTasks)
        {
            if (!_iconTasks.TryGetValue(path, out var task))
            {
                var provider = Application.Current.PlatformServices.ShellIconProvider;
                task = _lastIconLoad.ContinueWith(_ => provider.GetRealIcon(path, ICON_PIXELS), TaskScheduler.Default);
                _lastIconLoad = task;
                _iconTasks[path] = task;
            }

            return task;
        }
    }

    /// <summary>The shells of this system: Command Prompt and the PowerShells on Windows, the user's shell elsewhere.</summary>
    private static List<(string Label, string FileName, string[] Arguments)> AvailableShells()
    {
        var shells = new List<(string Label, string FileName, string[] Arguments)>();
        if (OperatingSystem.IsWindows())
        {
            // The names Windows Terminal gives its default profiles.
            shells.Add(("Command Prompt", ProcessConnection.DefaultShell, []));
            shells.Add(("Windows PowerShell", FindOnPath("powershell.exe") ?? "powershell.exe", ["-NoLogo"]));
            if (FindOnPath("pwsh.exe") is string pwsh)
            {
                shells.Add(("PowerShell", pwsh, ["-NoLogo"]));
            }
        }
        else
        {
            string shell = ProcessConnection.DefaultShell;
            shells.Add((Path.GetFileName(shell), shell, ["-l"]));
            if (shell != "/bin/sh")
            {
                shells.Add(("sh", "/bin/sh", []));
            }
        }

        return shells;
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>A tab's terminal, the shell it runs and the header text that shows the terminal's title.</summary>
    private sealed class TerminalTab(TabItem tabItem, TerminalView view, TextBlock title, string label)
    {
        public TabItem TabItem { get; } = tabItem;
        public TerminalView View { get; } = view;
        public TextBlock Title { get; } = title;
        public string Label { get; } = label;
        public ProcessConnection? Connection { get; set; }
    }
}
