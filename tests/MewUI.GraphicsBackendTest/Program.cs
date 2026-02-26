using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.GraphicsBackendTest;

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

var window = new Window()
    .Title("MewUI.GraphicsBackendTest")
    .Size(1000, 800)
    .Content(new GraphicsBackendTestView());

Application.Run(window);
