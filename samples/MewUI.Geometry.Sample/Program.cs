using Aprillz.MewUI;
using Aprillz.MewUI.Geometry;

Startup();
GeometryServices.EnableShapeHitTesting();
Application.Run(new GeometryOperationsWindow());

static void Startup()
{
    string[] args = Environment.GetCommandLineArgs();

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
