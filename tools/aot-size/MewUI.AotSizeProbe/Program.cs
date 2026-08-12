using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

#if MEWUI_PLATFORM_WIN32
Win32Platform.Register();
#if MEWUI_BACKEND_DIRECT2D
Direct2DBackend.Register();
#elif MEWUI_BACKEND_MEWVG
MewVGWin32Backend.Register();
#else
GdiBackend.Register();
#endif
#elif MEWUI_PLATFORM_LINUX
X11Platform.Register();
MewVGX11Backend.Register();
#elif MEWUI_PLATFORM_MACOS
MacOSPlatform.Register();
MewVGMacOSBackend.Register();
#else
#error A platform define must be supplied by Measure-AotSize.ps1.
#endif

Element? content = null;

#if MEWUI_AOT_SIZE_TEXT
content = new TextBlock { Text = "Hello, MewUI" };
#elif MEWUI_AOT_SIZE_BUTTON
content = new Button();
#elif MEWUI_AOT_SIZE_IMAGE
content = new Image
{
    Source = ImageSource.FromBgraPixels(1, 1, [0x80, 0x40, 0x20, 0xff]),
};
#endif

Application.Run(new Window
{
    Title = "MewUI NativeAOT size probe",
    WindowSize = WindowSize.Fixed(320, 180),
    Content = content,
});
