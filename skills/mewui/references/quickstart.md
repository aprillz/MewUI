# Build the first application

This is the default path for a new Windows application. It requires only a .NET SDK and the public NuGet package.

## Create and install

Use `net10.0` for a new application when the .NET 10 SDK is available. Keep `net8.0` only when
the consumer's deployment or support policy requires it and the selected package supports it.

```text
dotnet new console -n MewUIApp -f net10.0
cd MewUIApp
dotnet add package Aprillz.MewUI.Windows
```

Change `OutputType` in `MewUIApp.csproj` to `WinExe`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aprillz.MewUI.Windows" Version="THE_VERSION_SELECTED_BY_NUGET" />
  </ItemGroup>
</Project>
```

Do not type a guessed version. `dotnet add package` writes the resolved stable version. When Central Package Management is active, put that version in `Directory.Packages.props` instead.

## Replace Program.cs

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

var name = new ObservableValue<string>("World");
var status = new ObservableValue<string>("Ready");

Application
    .Create()
    .UseWin32()
    .UseDirect2D()
    .BuildMainWindow(() => new Window()
        .Title("My MewUI App")
        .Resizable(640, 420, minWidth: 480, minHeight: 320)
        .Content(
            new StackPanel()
                .Vertical()
                .Spacing(12)
                .Margin(20)
                .Children(
                    new TextBlock()
                        .Text("Welcome")
                        .FontSize(24)
                        .Bold(),
                    new TextBox()
                        .BindText(name),
                    new TextBlock()
                        .BindText(name, value => $"Hello, {value}"),
                    new Button()
                        .Content("Save")
                        .OnClick(() => status.Value = $"Saved for {name.Value}"),
                    new TextBlock()
                        .BindText(status))))
    .Run();
```

## Build and run

```text
dotnet restore
dotnet build --no-restore
dotnet run --no-build
```

The result must show an editable text box, a live greeting, a button, and a status line. Do not proceed to application features until this package-only startup works.

## Other platforms

Use one platform package and its matching registration pair:

| Target | Package | Registration |
| --- | --- | --- |
| Windows Direct2D | `Aprillz.MewUI.Windows` | `.UseWin32().UseDirect2D()` |
| Windows GDI | `Aprillz.MewUI.Windows` | `.UseWin32().UseGdi()` |
| Windows MewVG | `Aprillz.MewUI.Windows` | `.UseWin32().UseMewVGWin32()` |
| Linux X11 | `Aprillz.MewUI.Linux` | `.UseX11().UseMewVGX11()` |
| macOS Metal | `Aprillz.MewUI.MacOS` | `.UseMacOS().UseMewVGMetal()` |

Use `Aprillz.MewUI` only when a single project intentionally carries every platform. Do not register multiple alternative backends in one startup path.
