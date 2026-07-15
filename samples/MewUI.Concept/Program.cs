using Aprillz.MewUI;
using Aprillz.MewUI.Concept;

Startup(args);
//Application.Run(FitContentGridTest.Create());
//Application.Run(MeasureConstraintTest.Create()); // issue #173 regression
Application.Run(Issue199FitContentTest.Create()); // issue #199 regression
//Application.Run(MinTrackReleaseTest.Create()); // Win32 min-track release (chrome-less windows)
//Application.Run(StyleChangeTest.Create());
//Application.Run(ZoomPanCanvasTest.Create());

static void Startup(string[] args)
{
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
