using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.TextBoxPerformance;

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
    else if (OperatingSystem.IsLinux())
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
    }
}

Startup();

bool autoRun = Environment.GetCommandLineArgs().Any(a => a is "--auto");

var window = new Window()
    .Title("TextBox Performance Test")
    .Resizable(1000, 700);

var view = new PerformanceTestView(echoLog: autoRun);
window.Content = view;

if (autoRun)
{
    // Unattended mode: run every scenario once the window is up, then exit.
    window.Loaded += async () =>
    {
        await view.RunAutoAsync();
        window.Close();
    };
}

Application.Run(window);
