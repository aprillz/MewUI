#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property UseWindowsForms=False

// WPF cannot use AOT compilation.
#:property PublishAot=False

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

var app = new Application();
app.Run(CreateWindow());

static Window CreateWindow()
{
    var window = new Window
    {
        Title = "WPF Non-Uniform Border Test",
        Width = 1000,
        Height = 800,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildContent(),
        },
    };
    return window;
}

static UIElement BuildContent()
{
    var root = Stack(Orientation.Vertical, 0);
    root.Margin = new Thickness(24);

    root.Children.Add(Label("Non-Uniform Border Visual Test Cases (WPF Reference)"));

    // === 1. Uniform (regression) ===
    root.Children.Add(Section("1. Uniform (Regression)",
        Bdr("No radius", new Thickness(2), new CornerRadius(0), C(60,120,200), C(230,240,250)),
        Bdr("Uniform radius=8", new Thickness(2), new CornerRadius(8), C(60,120,200), C(230,240,250)),
        Bdr("Uniform radius=20", new Thickness(4), new CornerRadius(20), C(60,120,200), C(230,240,250)),
        Bdr("Thick border=8", new Thickness(8), new CornerRadius(12), C(200,60,60), C(255,230,230))
    ));

    // === 2. Non-uniform thickness ===
    root.Children.Add(Section("2. Non-Uniform Thickness",
        Bdr("Bottom only (0,0,0,3)", new Thickness(0,0,0,3), new CornerRadius(0), C(60,120,200), C(230,240,250)),
        Bdr("Top=1, Bottom=4", new Thickness(0,1,0,4), new CornerRadius(0), C(60,120,200), C(230,240,250)),
        Bdr("Left=6, others=1", new Thickness(6,1,1,1), new CornerRadius(0), C(200,60,60), C(255,230,230)),
        Bdr("All different (2,4,6,8)", new Thickness(2,4,6,8), new CornerRadius(0), C(60,160,60), C(230,250,230))
    ));

    // === 3. Non-uniform corner radius ===
    root.Children.Add(Section("3. Non-Uniform CornerRadius",
        Bdr("TL=16 only", new Thickness(2), new CornerRadius(16,0,0,0), C(60,120,200), C(230,240,250)),
        Bdr("Top corners=12", new Thickness(2), new CornerRadius(12,12,0,0), C(60,120,200), C(230,240,250)),
        Bdr("Diagonal TL=16, BR=16", new Thickness(2), new CornerRadius(16,0,16,0), C(200,60,60), C(255,230,230)),
        Bdr("All different (4,8,16,24)", new Thickness(2), new CornerRadius(4,8,16,24), C(60,160,60), C(230,250,230))
    ));

    // === 4. Non-uniform thickness + radius ===
    root.Children.Add(Section("4. Non-Uniform Thickness + Radius",
        Bdr("T=1,B=4 + Top corners=12", new Thickness(1,1,1,4), new CornerRadius(12,12,0,0), C(60,120,200), C(230,240,250)),
        Bdr("L=6,R=2 + TL=20,BR=20", new Thickness(6,2,2,2), new CornerRadius(20,0,20,0), C(200,60,60), C(255,230,230)),
        Bdr("All diff (2,4,6,8) + (4,8,16,24)", new Thickness(2,4,6,8), new CornerRadius(4,8,16,24), C(60,160,60), C(230,250,230)),
        Bdr("Thick (8,2,8,2) + (16,16,16,16)", new Thickness(8,2,8,2), new CornerRadius(16), C(160,60,160), C(245,230,250))
    ));

    // === 5. Radius clamping ===
    root.Children.Add(Section("5. Radius Clamping (radius > size/2)",
        Bdr("Pill shape (radius=999)", new Thickness(2), new CornerRadius(999), C(60,120,200), C(230,240,250), 120, 40),
        Bdr("TL=100, TR=100 on 120x60", new Thickness(2), new CornerRadius(100,100,0,0), C(200,60,60), C(255,230,230), 120, 60),
        Bdr("All=50 on 60x60 (circle-ish)", new Thickness(3), new CornerRadius(50), C(60,160,60), C(230,250,230), 60, 60)
    ));

    // === 6. ClipToBounds ===
    root.Children.Add(Section("6. ClipToBounds",
        ClipBdr("Uniform clip r=12", new Thickness(2), new CornerRadius(12), C(60,120,200), C(230,240,250)),
        ClipBdr("Non-uniform clip (12,12,0,0)", new Thickness(2), new CornerRadius(12,12,0,0), C(200,60,60), C(255,230,230)),
        ClipBdr("Thick+radius clip (4,1,4,1)+(8)", new Thickness(4,1,4,1), new CornerRadius(8), C(60,160,60), C(230,250,230))
    ));

    // === 7. Border only (no background) ===
    root.Children.Add(Section("7. Border Only (no background)",
        Bdr("Uniform border only", new Thickness(2), new CornerRadius(8), C(60,120,200), null),
        Bdr("Non-uniform (2,4,6,8)", new Thickness(2,4,6,8), new CornerRadius(12), C(200,60,60), null),
        Bdr("Radius only (0,0,16,16)", new Thickness(2), new CornerRadius(0,0,16,16), C(60,160,60), null)
    ));

    // === 8. Background only (no border) ===
    root.Children.Add(Section("8. Background Only (no border)",
        Bdr("Uniform bg only", new Thickness(0), new CornerRadius(8), null, C(230,240,250)),
        Bdr("Non-uniform radius bg (12,0,12,0)", new Thickness(0), new CornerRadius(12,0,12,0), null, C(255,230,230))
    ));

    // === 9. Elliptical inner radius ===
    root.Children.Add(Section("9. Elliptical Inner Radius",
        Bdr("L=12,T=2 + TL=16 (inner rx=4, ry=14)", new Thickness(12,2,2,2), new CornerRadius(16,8,8,8), C(60,120,200), C(230,240,250)),
        Bdr("T=12,L=2 + TL=16 (inner rx=14, ry=4)", new Thickness(2,12,2,2), new CornerRadius(16,8,8,8), C(200,60,60), C(255,230,230)),
        Bdr("Thick>radius (T=20, TL=10 -> inner ry=0)", new Thickness(2,20,2,2), new CornerRadius(10,10,8,8), C(60,160,60), C(230,250,230))
    ));

    return root;
}

static Border Bdr(string label, Thickness thickness, CornerRadius radius,
    Color? borderColor, Color? bgColor, double width = 160, double height = 80)
{
    return new Border
    {
        BorderThickness = thickness,
        CornerRadius = radius,
        BorderBrush = borderColor.HasValue ? new SolidColorBrush(borderColor.Value) : null,
        Background = bgColor.HasValue ? new SolidColorBrush(bgColor.Value) : null,
        Width = width,
        Height = height,
        Child = new TextBlock
        {
            Text = label,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4),
        },
    };
}

static Border ClipBdr(string label, Thickness thickness, CornerRadius radius,
    Color borderColor, Color bgColor)
{
    return new Border
    {
        BorderThickness = thickness,
        CornerRadius = radius,
        BorderBrush = new SolidColorBrush(borderColor),
        Background = new SolidColorBrush(bgColor),
        ClipToBounds = true,
        Width = 160,
        Height = 80,
        Child = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(128, 255, 100, 100)),
            Width = 200,
            Height = 120,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4),
            },
        },
    };
}

static UIElement Section(string title, params UIElement[] items)
{
    var panel = Stack(Orientation.Vertical, 0);

    panel.Children.Add(Label(title));

    var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 16) };
    foreach (var item in items)
    {
        var spacer = new Border { Margin = new Thickness(0, 0, 12, 12), Child = item };
        wrap.Children.Add(spacer);
    }
    panel.Children.Add(wrap);
    return panel;
}

static StackPanel Stack(Orientation orientation, double margin)
{
    return new StackPanel { Orientation = orientation, Margin = new Thickness(margin) };
}

static TextBlock Label(string text) => new TextBlock
{
    Text = text,
    FontSize = 14,
    FontWeight = FontWeights.Bold,
    Margin = new Thickness(0, 0, 0, 4),
};

static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
