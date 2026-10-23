using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Windowless.Sample;

RegisterPlatformAndBackend();

IHotkeyProvider? hotkey = null;
TrayIcon? tray = null;
PaletteController? palette = null;
DispatcherTimer? smokeTimer = null;

Application.Create()
    .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
    .OnStartup(args =>
    {
        bool runSmoke = args.Contains("--smoke", StringComparer.Ordinal);
        var dispatcher = Application.Current.Dispatcher
            ?? throw new InvalidOperationException("The UI dispatcher was not installed before startup.");

        void Exit()
        {
            smokeTimer?.Dispose();
            smokeTimer = null;
            hotkey?.Dispose();
            hotkey = null;
            tray?.Dispose();
            tray = null;
            palette?.PrepareToExit();
            Application.Shutdown();
        }

        byte[] iconFile = LoadAppIcon();
        var hideOnClose = new ObservableValue<bool>(false);
        palette = new PaletteController(Exit, IconSource.FromStream(new MemoryStream(iconFile)), hideOnClose);

        // The tray menu is an ordinary MewUI ContextMenu; each platform shows it as its own native menu. Clicks and
        // commands run on the next dispatcher turn, after the native menu has returned, because Exit removes the tray.
        var togglePalette = new Command("windowless.togglePalette", "_Show or hide palette");
        var toggleHideOnClose = new Command("windowless.hideOnClose", "_Hide instead of close");
        var exit = new Command("windowless.exit", "E_xit");
        var trayMenu = new ContextMenu()
            .Items(
                new MenuItem(togglePalette),
                new MenuItem(toggleHideOnClose).IsCheckable().BindIsChecked(hideOnClose))
            .Separator()
            .SubMenu("_About", new ContextMenu()
                .Item("MewUI windowless sample", isEnabled: false)
                .Item("Press Ctrl+Alt+Space to toggle the palette", isEnabled: false))
            .Separator()
            .Item(exit);

        // Application-scope handlers: the tray runs them through Application.CommandRouter while no window is open.
        var commands = Application.Current.Commands;
        commands.Register(togglePalette, palette.Toggle);
        commands.Register(toggleHideOnClose, () => hideOnClose.Value = !hideOnClose.Value);
        commands.Register(exit, Exit);

        tray = new TrayIcon(action => dispatcher.BeginInvoke(action))
            .Icon(iconFile)
            .ToolTip("MewUI Windowless Sample")
            .ContextMenu(trayMenu)
            .OnClick(palette.Toggle);
        tray = ShowTray(tray);
        try
        {
            hotkey = HotkeyProviderFactory.Create();
            hotkey.Start(() => dispatcher.BeginInvoke(palette.Toggle));
            Console.Error.WriteLine($"[windowless] {hotkey.Name} registered; user windows={Application.Current.AllWindows.Count}");
            Console.Error.WriteLine("[windowless] press Ctrl+Alt+Space to toggle the palette");
            if (runSmoke)
            {
                int phase = 0;
                smokeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400));
                smokeTimer.Tick += () =>
                {
                    phase++;
                    switch (phase)
                    {
                        case 1:
                        case 2:
                        case 3:
                            palette.Toggle();
                            break;
                        default:
                            palette.Toggle();
                            Console.Error.WriteLine("[windowless] smoke lifecycle completed");
                            Exit();
                            break;
                    }
                };
                smokeTimer.Start();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[windowless] hotkey registration failed: {ex}");
            Exit();
        }
    })
    .Run();

hotkey?.Dispose();
tray?.Dispose();

static TrayIcon? ShowTray(TrayIcon tray)
{
    // The tray is a convenience next to the hotkey: where it is missing or fails, the sample still runs.
    try
    {
        if (tray.Show())
        {
            Console.Error.WriteLine($"[windowless] {tray.BackendName} added");
            return tray;
        }

        Console.Error.WriteLine("[windowless] no tray icon on this platform");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[windowless] tray icon failed: {ex}");
    }

    tray.Dispose();
    return null;
}

static byte[] LoadAppIcon()
{
    using var stream = typeof(PaletteController).Assembly.GetManifestResourceStream("Aprillz.MewUI.Windowless.Sample.appicon.ico")
        ?? throw new InvalidOperationException("The app icon resource is missing.");
    var bytes = new byte[stream.Length];
    stream.ReadExactly(bytes);
    return bytes;
}

static void RegisterPlatformAndBackend()
{
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();
        Direct2DBackend.Register();
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
    else
    {
        throw new PlatformNotSupportedException();
    }
}
