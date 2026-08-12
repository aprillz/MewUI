using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit.Sample;
 
#if MEWUI_GALLERY_WIN
#pragma warning disable CA1416
    Win32Platform.Register();
    Direct2DBackend.Register();
#pragma warning restore CA1416
#elif MEWUI_GALLERY_OSX
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
#elif MEWUI_GALLERY_LINUX
    X11Platform.Register();
    MewVGX11Backend.Register();
#else
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
#endif


bool smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);

Application
    .Create()
    .UseAccent(Accent.Purple) 
    .BuildMainWindow(() =>
    {
        var window = new MainWindow();
        if (smoke) window.EnableSmokeTest();
        return window;
    })
    .Run();
