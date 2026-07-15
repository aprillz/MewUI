#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property UseWindowsForms=False

// WPF cannot use AOT compilation.
#:property PublishAot=False

// WPF reference for MewUI issue #199.
//
// MewUI: `this.Content(new DockPanel().Children(new ListBox())).FitContentSize()` produces a
// 1000x18 window - the empty ListBox reports the whole offered width as its desired size, so the
// window always lands on FitContentSize's max (1000) no matter what the content is.
//
// This is the same case in WPF for comparison:
//   SizeToContent.WidthAndHeight  ~= MewUI's FitContentSize()
//   MaxWidth/MaxHeight = 1000     ~= FitContentSize()'s default max
//
// Two windows: with and without the DockPanel, since the issue reports both.
// The measured numbers are reported through the window title - putting a status element inside the
// content would change the very thing being measured.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

const string ReportPath = "wpf_fitcontent.out";
File.Delete(ReportPath);

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

var app = new Application();

var withDock = CreateCase("DockPanel + empty ListBox", useDockPanel: true, left: 40);
var withoutDock = CreateCase("empty ListBox (no DockPanel)", useDockPanel: false, left: 700);

// Chrome matrix: how SizeToContent behaves without the standard caption.
var noneResizable = CreateChromeCase("None + CanResize", ResizeMode.CanResize, transparent: false, left: 40);
var noneFixed = CreateChromeCase("None + NoResize", ResizeMode.NoResize, transparent: false, left: 400);
var noneTransparent = CreateChromeCase("None + AllowsTransparency", ResizeMode.CanResize, transparent: true, left: 760);

withDock.Show();
withoutDock.Show();
noneResizable.Show();
noneFixed.Show();
noneTransparent.Show();

app.Run();

static Window CreateCase(string label, bool useDockPanel, double left)
{
    var list = new ListBox();

    var window = new Window
    {
        Title = label,
        SizeToContent = SizeToContent.WidthAndHeight,
        MaxWidth = 1000,
        MaxHeight = 1000,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = left,
        Top = 80,
        Content = useDockPanel
            ? new DockPanel { Children = { list } }
            : (object)list,
    };

    window.ContentRendered += (_, _) =>
    {
        // Client size two ways: GetClientRect is the OS truth in pixels; the content root's actual
        // size is the same area in DIPs (the analogue of MewUI's Window.ClientSize).
        Native.GetClientRect(new WindowInteropHelper(window).Handle, out var rc);
        double dpiScale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        var content = (FrameworkElement)window.Content;

        string report =
            $"{label}  |  window={window.ActualWidth:0}x{window.ActualHeight:0}" +
            $"  |  client px={rc.Right - rc.Left}x{rc.Bottom - rc.Top} (scale {dpiScale:0.##})" +
            $" dip={content.ActualWidth:0.#}x{content.ActualHeight:0.#}" +
            $"  |  listbox desired={list.DesiredSize.Width:0.#}x{list.DesiredSize.Height:0.#}" +
            $" actual={list.ActualWidth:0.#}x{list.ActualHeight:0.#}";

        window.Title = report;
        // Both windows live in one process, so only one shows up as MainWindowTitle. Also append to a
        // file so every case can be read back.
        File.AppendAllText(ReportPath, report + Environment.NewLine);
    };

    return window;
}

static Window CreateChromeCase(string label, ResizeMode resizeMode, bool transparent, double left)
{
    var list = new ListBox();

    var window = new Window
    {
        Title = label,
        WindowStyle = WindowStyle.None,
        ResizeMode = resizeMode,
        AllowsTransparency = transparent,
        SizeToContent = SizeToContent.WidthAndHeight,
        MaxWidth = 1000,
        MaxHeight = 1000,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = left,
        Top = 320,
        Content = list,
    };

    window.ContentRendered += (_, _) =>
    {
        Native.GetClientRect(new WindowInteropHelper(window).Handle, out var rc);
        double dpiScale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        var content = (FrameworkElement)window.Content;

        string report =
            $"{label}  |  window={window.ActualWidth:0}x{window.ActualHeight:0}" +
            $"  |  client px={rc.Right - rc.Left}x{rc.Bottom - rc.Top} (scale {dpiScale:0.##})" +
            $" dip={content.ActualWidth:0.#}x{content.ActualHeight:0.#}" +
            $"  |  listbox desired={list.DesiredSize.Width:0.#}x{list.DesiredSize.Height:0.#}" +
            $" actual={list.ActualWidth:0.#}x{list.ActualHeight:0.#}";

        File.AppendAllText(ReportPath, report + Environment.NewLine);
    };

    return window;
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hWnd, out RECT lpRect);
}
