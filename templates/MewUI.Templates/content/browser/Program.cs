using System.Runtime.Versioning;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

[assembly: SupportedOSPlatform("browser")]

BrowserPlatform.Register();
MewVGBrowserBackend.Register();

var clicks = new ObservableValue<int>(0);

// RunAsync returns once the window is shown; the page's frame loop drives it from there.
await Application.Create()
    .BuildMainWindow(() => new Window()
        .Title("MewUIBrowserApp")
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
                        .OnClick(() => clicks.Value++))))
    .RunAsync();
