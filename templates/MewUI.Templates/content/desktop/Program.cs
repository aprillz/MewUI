using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

if (OperatingSystem.IsWindows())
{
    Win32Platform.Register();
    Direct2DBackend.Register();
}
else if (OperatingSystem.IsLinux())
{
    X11Platform.Register();
    MewVGX11Backend.Register();
}
else if (OperatingSystem.IsMacOS())
{
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
}
else
{
    throw new PlatformNotSupportedException("MewUI runs on Windows, Linux and macOS.");
}

var clicks = new ObservableValue<int>(0);

var window = new Window()
    .Title("MewUIApp")
    .Resizable(480, 320)
    .Padding(16)
    .Content(
        new StackPanel()
            .Spacing(8)
            .Children(
                new Label()
                    .Text("Hello, MewUI")
                    .FontSize(18)
                    .Bold(),
                new Label()
                    .BindText(clicks, count => $"Clicked {count} times"),
                new Button()
                    .Content("Click")
                    .OnClick(() => clicks.Value++)));

Application.Run(window);
